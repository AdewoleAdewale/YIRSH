using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRSHospital.Services;

namespace YIRSHospital.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class PatientTransaction : ContentPage
    {
        private CancellationTokenSource _cancellationTokenSource;
        private string _currentPatientId;
        private PatientTransactionResponse _currentResponse;

        public PatientTransaction()
        {
            InitializeComponent();
            InitializeUI();
        }

        private void InitializeUI()
        {
            ReceiptContainer.IsVisible = false;
            ShareButton.IsVisible = false;
            Search.TextChanged += OnSearchTextChanged;
            searchButton.IsEnabled = false;
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            var entry = sender as Entry;
            var text = entry?.Text?.Trim() ?? string.Empty;

            searchButton.IsEnabled = IsValidPatientId(text);

            if (!string.IsNullOrEmpty(text) && ReceiptContainer.IsVisible)
            {
                ClearResults();
            }
        }

        private bool IsValidPatientId(string patientId)
        {
            if (string.IsNullOrWhiteSpace(patientId))
                return false;

            return Regex.IsMatch(patientId, @"^[A-Za-z0-9]{1,20}$");
        }

        private void ClearResults()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                ReceiptContainer.IsVisible = false;
                ShareButton.IsVisible = false;
                PatientNameLabel.Text = string.Empty;
                PatientIdLabel.Text = string.Empty;
                TotalTransactionsLabel.Text = string.Empty;
                TotalAmountLabel.Text = string.Empty;
                TransactionsStack.Children.Clear();
            });
        }

        private async void Button_Clicked(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button != null) button.IsEnabled = false;

            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();

                var patientId = Search.Text?.Trim();
                _currentPatientId = patientId;

                if (!IsValidPatientId(patientId))
                {
                    await DisplayAlert("Invalid Input", "Please enter a valid Patient ID.", "OK");
                    return;
                }

                if (Connectivity.NetworkAccess != NetworkAccess.Internet)
                {
                    await DisplayAlert("No Internet", "Please check your network connection.", "OK");
                    return;
                }

                await SearchPatientTransactions(patientId, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PatientTransaction] Error: {ex.Message}");
                await DisplayAlert("Error", "An unexpected error occurred.", "OK");
            }
            finally
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    if (button != null) button.IsEnabled = true;
                });
            }
        }

        private async Task SearchPatientTransactions(string patientId, CancellationToken cancellationToken)
        {
            IProgressDialog progressDialog = null;

            try
            {
                progressDialog = UserDialogs.Instance.Loading(
                    "Fetching patient transactions...",
                    maskType: MaskType.Black,
                    cancelText: "Cancel"
                );

                // Pass HospitalContext.Code to route correctly to Potiskum, Damagum, or Specialist
                var apiResult = await HospitalApiService.GetPatientTransactionsAsync(
                    patientId,
                    hospitalCode: HospitalContext.Code,
                    ct: cancellationToken
                );

                if (apiResult.Success && apiResult.Data != null)
                {
                    _currentResponse = apiResult.Data;
                    if (_currentResponse.Code == "00" && _currentResponse.Transactions?.Any() == true)
                    {
                        DisplayPatientInformation(_currentResponse);
                    }
                    else
                    {
                        ShowNoDataFoundMessage();
                    }
                }
                else
                {
                    await DisplayAlert("Service Error", apiResult.ErrorMessage ?? "Patient records not found.", "OK");
                }
            }
            catch (OperationCanceledException)
            {
                // Request cancelled
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchPatientTransactions] {ex.Message}");
                await DisplayAlert("Error", "Failed to retrieve transactions.", "OK");
            }
            finally
            {
                progressDialog?.Dispose();
            }
        }

        private void DisplayPatientInformation(PatientTransactionResponse result)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                PatientNameLabel.Text = result.PatientName ?? "N/A";
                PatientIdLabel.Text = $"ID: {result.PatientNo ?? "N/A"}";
                TotalTransactionsLabel.Text = result.TotalTransactions.ToString();
                TotalAmountLabel.Text = $"₦{result.TotalAmount:N2}";
                GeneratedDate.Text = $"Generated: {DateTime.Now:dd MMM yyyy hh:mm tt}";

                TransactionsStack.Children.Clear();

                foreach (var transaction in result.Transactions.OrderByDescending(t => t.RawDate))
                {
                    var view = CreateTransactionView(transaction);
                    TransactionsStack.Children.Add(view);
                }

                ReceiptContainer.IsVisible = true;
                ReceiptContainer.FadeTo(1, 300);

                ShareButton.IsVisible = true;
                ShareButton.FadeTo(1, 300);

                UserDialogs.Instance.Toast("Transactions loaded successfully!", TimeSpan.FromSeconds(2));
            });
        }

        private View CreateTransactionView(Transaction transaction)
        {
            var container = new Frame
            {
                BackgroundColor = Color.White,
                BorderColor = Color.FromHex("#E0E0E0"),
                CornerRadius = 12,
                HasShadow = true,
                Padding = 15,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var mainStack = new StackLayout { Spacing = 10 };

            // Header
            var serviceHeader = new Frame
            {
                BackgroundColor = Color.FromHex("#004225"),
                CornerRadius = 8,
                Padding = new Thickness(12, 8),
                HasShadow = false,
                Content = new Label
                {
                    Text = transaction.ServiceTypeName ?? "Service Item",
                    TextColor = Color.White,
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 15,
                    HorizontalTextAlignment = TextAlignment.Center
                }
            };
            mainStack.Children.Add(serviceHeader);

            // Details Grid
            var detailsStack = new StackLayout { Spacing = 6 };
            detailsStack.Children.Add(CreateDetailRow("Transaction ID:", transaction.TransactionId));
            detailsStack.Children.Add(CreateDetailRow("Date:", transaction.FormattedDate));
            detailsStack.Children.Add(CreateDetailRow("Amount:", $"₦{transaction.Amount:N2}"));

            if (!string.IsNullOrWhiteSpace(transaction.Department))
                detailsStack.Children.Add(CreateDetailRow("Department:", transaction.Department));

            if (!string.IsNullOrWhiteSpace(transaction.PaymentMethod))
                detailsStack.Children.Add(CreateDetailRow("Method:", transaction.PaymentMethod));

            detailsStack.Children.Add(CreateDetailRow("Status:", transaction.Status ?? "N/A"));

            mainStack.Children.Add(detailsStack);
            container.Content = mainStack;
            return container;
        }

        private View CreateDetailRow(string label, string value)
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(110) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                }
            };

            var labelView = new Label
            {
                Text = label,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromHex("#666666")
            };
            Grid.SetColumn(labelView, 0);

            var valueView = new Label
            {
                Text = value ?? "N/A",
                FontSize = 12,
                TextColor = Color.FromHex("#333333")
            };
            Grid.SetColumn(valueView, 1);

            grid.Children.Add(labelView);
            grid.Children.Add(valueView);
            return grid;
        }

        private void ShowNoDataFoundMessage()
        {
            ClearResults();
            DisplayAlert("No Records", "No transactions found for the specified Patient ID.", "OK");
        }

        private async void ShareButton_Clicked(object sender, EventArgs e)
        {
            try
            {
                UserDialogs.Instance.ShowLoading("Preparing receipt...");
                var screenshot = await Screenshot.CaptureAsync();

                if (screenshot != null)
                {
                    var stream = await screenshot.OpenReadAsync();
                    var fileName = $"Receipt_{_currentPatientId}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);

                    using (var fileStream = File.Create(filePath))
                    {
                        await stream.CopyToAsync(fileStream);
                    }

                    await Share.RequestAsync(new ShareFileRequest
                    {
                        Title = "Patient Transaction Receipt",
                        File = new ShareFile(filePath)
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Share] Error: {ex.Message}");
            }
            finally
            {
                UserDialogs.Instance.HideLoading();
            }
        }

        private async void OnBackNavClicked(object sender, EventArgs e)
        {
            try { await Navigation.PopAsync(); } catch { }
        }
    
        private async Task HandleUnexpectedError(Exception ex)
        {
            Debug.WriteLine($"Unexpected error: {ex}");
            string userMessage = "An unexpected error occurred. Please try again.";

            if (ex is ArgumentException)
                userMessage = "Invalid input provided.";
            else if (ex is InvalidOperationException)
                userMessage = "Operation not allowed at this time.";
            else if (ex is NotSupportedException)
                userMessage = "This operation is not supported.";

            await ShowErrorAlert("Error", userMessage);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        private async Task ShowErrorAlert(string title, string message)
        {
            await Device.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert(title, message, "OK");
            });
        }

        private async Task ShowInfoAlert(string title, string message)
        {
            await Device.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert(title, message, "OK");
            });
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Device.BeginInvokeOnMainThread(() =>
            {
                Search?.Focus();
            });
        }

      
    }



    // Response Models
    public class PatientTransactionResponse
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("patientName")]
        public string PatientName { get; set; }

        [JsonProperty("patientNo")]
        public string PatientNo { get; set; }

        [JsonProperty("hospitalId")]
        public int HospitalId { get; set; }

        [JsonProperty("hospitalName")]
        public string HospitalName { get; set; }

        [JsonProperty("hospitalCode")]
        public string HospitalCode { get; set; }

        [JsonProperty("gender")]
        public string Gender { get; set; }

        [JsonProperty("phoneNumber")]
        public string PhoneNumber { get; set; }

        [JsonProperty("totalTransactions")]
        public int TotalTransactions { get; set; }

        [JsonProperty("totalAmount")]
        public decimal TotalAmount { get; set; }

        [JsonProperty("transactions")]
        public List<Transaction> Transactions { get; set; }
    }

    public class Transaction
    {
        // ISO Date from GetHospitalPatientTransactions
        [JsonProperty("date")]
        public string Date { get; set; }

        // Date string from GetPatientTransactions (DEFAULT)
        [JsonProperty("datelIst")]
        public string DateList { get; set; }

        [JsonProperty("transactionId")]
        public string TransactionId { get; set; }

        [JsonProperty("serviceTypeName")]
        public string ServiceTypeName { get; set; }

        [JsonProperty("hospitalNo")]
        public string HospitalNo { get; set; }

        [JsonProperty("department")]
        public string Department { get; set; }

        [JsonProperty("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("payer")]
        public string Payer { get; set; }

        [JsonProperty("revenueHead")]
        public string RevenueHead { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("paymentMethod")]
        public string PaymentMethod { get; set; }

        // Helper property to resolve whichever date string is populated
        [JsonIgnore]
        public string RawDate => !string.IsNullOrWhiteSpace(Date) ? Date : DateList;

        [JsonIgnore]
        public string FormattedDate
        {
            get
            {
                string raw = RawDate;
                if (string.IsNullOrWhiteSpace(raw)) return "N/A";

                if (DateTime.TryParse(raw, out DateTime parsed))
                    return parsed.ToString("dd MMM yyyy, hh:mm tt");

                return raw;
            }
        }
    }
}