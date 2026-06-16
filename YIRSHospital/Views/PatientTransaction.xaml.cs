using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace YIRSHospital.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class PatientTransaction : ContentPage
    {
        private HttpClient _httpClient;
        private CancellationTokenSource _cancellationTokenSource;
        private const string API_BASE_URL = "https://yobe.osoftpay.net/api/Agents/GetPatientTransactions";
        private const int REQUEST_TIMEOUT = 30;
        private const int CONNECTION_TIMEOUT = 30000;
        private string _currentPatientId;
        private PatientTransactionResponse _currentResponse;

        public PatientTransaction()
        {
            InitializeComponent();
            InitializeUI();

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                {
                    // Accept all certificates (adjust for production)
                    return true;
                },
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls11
            };


            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(CONNECTION_TIMEOUT)
            };
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

            var pattern = @"^[A-Za-z0-9]{3,20}$";
            return Regex.IsMatch(patientId, pattern);
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
            if (button == null) return;

            button.IsEnabled = false;

            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();

                var patientId = Search.Text?.Trim();
                _currentPatientId = patientId;

                if (!IsValidPatientId(patientId))
                {
                    await ShowErrorAlert("Invalid Input", "Please enter a valid Patient ID (3-20 alphanumeric characters).");
                    return;
                }

                if (!IsNetworkAvailable())
                {
                    await ShowErrorAlert("No Internet Connection", "Please check your internet connection and try again.");
                    return;
                }

                await SearchPatientTransactions(patientId, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                await HandleUnexpectedError(ex);
            }
            finally
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    button.IsEnabled = true;
                });
            }
        }

        private bool IsNetworkAvailable()
        {
            try
            {
                var networkAccess = Connectivity.NetworkAccess;
                return networkAccess == NetworkAccess.Internet;
            }
            catch
            {
                return false;
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

                var url = $"{API_BASE_URL}?patientId={Uri.EscapeDataString(patientId)}";

                Debug.WriteLine($"Making API request to: {url}");

                using (var response = await _httpClient.GetAsync(url, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonContent = await response.Content.ReadAsStringAsync();

                        if (string.IsNullOrWhiteSpace(jsonContent))
                        {
                            await ShowErrorAlert("Server Error", "Received empty response from server.");
                            return;
                        }

                        await ProcessApiResponse(jsonContent);
                    }
                    else
                    {
                        await HandleHttpError(response);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                await ShowInfoAlert("Request Cancelled", "The search operation was cancelled.");
            }
            catch (HttpRequestException ex)
            {
                await ShowErrorAlert("Network Error", $"Failed to connect to server: {ex.Message}");
            }
            catch (JsonException ex)
            {
                await ShowErrorAlert("Data Error", "Failed to process server response. Please try again.");
                Debug.WriteLine($"JSON parsing error: {ex.Message}");
            }
            catch (Exception ex)
            {
                await HandleUnexpectedError(ex);
            }
            finally
            {
                progressDialog?.Dispose();
            }
        }

        private async Task ProcessApiResponse(string jsonContent)
        {
            try
            {
                var result = JsonConvert.DeserializeObject<PatientTransactionResponse>(jsonContent);

                if (result == null)
                {
                    await ShowErrorAlert("Invalid Response", "Received invalid data from server.");
                    return;
                }

                _currentResponse = result;

                await Device.InvokeOnMainThreadAsync(() =>
                {
                    if (result.Code == "00" && result.Transactions != null && result.Transactions.Any())
                    {
                        DisplayPatientInformation(result);
                    }
                    else
                    {
                        ShowNoDataFoundMessage();
                    }
                });
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"JSON deserialization error: {ex.Message}");
                await ShowErrorAlert("Data Processing Error", "Failed to process the response from server.");
            }
        }

        private void DisplayPatientInformation(PatientTransactionResponse result)
        {
            try
            {
                // Header Information
                PatientNameLabel.Text = result.PatientName ?? "N/A";
                PatientIdLabel.Text = $"ID: {result.PatientNo ?? "N/A"}";
                TotalTransactionsLabel.Text = result.TotalTransactions.ToString();
                TotalAmountLabel.Text = $"₦{result.TotalAmount:N2}";
                GeneratedDate.Text = $"Generated: {DateTime.Now:dd MMM yyyy hh:mm tt}";

                // Clear previous transactions
                TransactionsStack.Children.Clear();

                // Add transactions
                foreach (var transaction in result.Transactions.OrderByDescending(t => t.DateList))
                {
                    var transactionView = CreateTransactionView(transaction);
                    TransactionsStack.Children.Add(transactionView);
                }

                // Show the receipt with animation
                ReceiptContainer.IsVisible = true;
                ReceiptContainer.FadeTo(1, 300);

                // Show share button
                ShareButton.IsVisible = true;
                ShareButton.FadeTo(1, 300);

                UserDialogs.Instance.Toast("Patient transactions loaded successfully!", TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error displaying patient information: {ex.Message}");
            }
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

            // Service Name Header
            var serviceHeader = new Frame
            {
                BackgroundColor = Color.FromHex("#004225"),
                CornerRadius = 8,
                Padding = new Thickness(12, 8),
                HasShadow = false
            };

            serviceHeader.Content = new Label
            {
                Text = transaction.ServiceTypeName,
                TextColor = Color.White,
                FontAttributes = FontAttributes.Bold,
                FontSize = 15,
                HorizontalTextAlignment = TextAlignment.Center
            };

            mainStack.Children.Add(serviceHeader);

            // Amount and Status
            var amountStatusGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                ColumnSpacing = 10
            };

            var amountFrame = new Frame
            {
                BackgroundColor = Color.FromHex("#FFF3E0"),
                CornerRadius = 8,
                Padding = new Thickness(10, 8),
                HasShadow = false
            };

            var amountStack = new StackLayout { Spacing = 2 };
            amountStack.Children.Add(new Label
            {
                Text = "Amount",
                FontSize = 11,
                TextColor = Color.FromHex("#666666"),
                HorizontalTextAlignment = TextAlignment.Center
            });
            amountStack.Children.Add(new Label
            {
                Text = $"₦{transaction.Amount:N2}",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromHex("#004225"),
                HorizontalTextAlignment = TextAlignment.Center
            });

            amountFrame.Content = amountStack;
            Grid.SetColumn(amountFrame, 0);
            amountStatusGrid.Children.Add(amountFrame);

            var statusFrame = new Frame
            {
                BackgroundColor = GetStatusColor(transaction.Status),
                CornerRadius = 8,
                Padding = new Thickness(10, 8),
                HasShadow = false
            };

            var statusStack = new StackLayout { Spacing = 2 };
            statusStack.Children.Add(new Label
            {
                Text = "Status",
                FontSize = 11,
                TextColor = Color.White,
                HorizontalTextAlignment = TextAlignment.Center
            });
            statusStack.Children.Add(new Label
            {
                Text = GetStatusText(transaction.Status),
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.White,
                HorizontalTextAlignment = TextAlignment.Center
            });

            statusFrame.Content = statusStack;
            Grid.SetColumn(statusFrame, 1);
            amountStatusGrid.Children.Add(statusFrame);

            mainStack.Children.Add(amountStatusGrid);

            // Additional Details
            var detailsStack = new StackLayout { Spacing = 5, Margin = new Thickness(0, 5, 0, 0) };

            detailsStack.Children.Add(CreateDetailRow("Transaction ID:", transaction.TransactionId));
            detailsStack.Children.Add(CreateDetailRow("Date:", FormatDate(transaction.DateList)));
            detailsStack.Children.Add(CreateDetailRow("Payer:", transaction.Payer));
            detailsStack.Children.Add(CreateDetailRow("Revenue Head:", transaction.RevenueHead));

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
                    new ColumnDefinition { Width = new GridLength(120) },
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
            grid.Children.Add(labelView);

            var valueView = new Label
            {
                Text = value ?? "N/A",
                FontSize = 12,
                TextColor = Color.FromHex("#333333"),
                LineBreakMode = LineBreakMode.TailTruncation
            };
            Grid.SetColumn(valueView, 1);
            grid.Children.Add(valueView);

            return grid;
        }

        private Color GetStatusColor(string status)
        {
            if (status?.ToLower().Contains("successful") == true ||
                status?.ToLower().Contains("approved") == true)
            {
                return Color.FromHex("#28A745");
            }
            else if (status?.ToLower().Contains("pending") == true)
            {
                return Color.FromHex("#FFC107");
            }
            else
            {
                return Color.FromHex("#DC3545");
            }
        }

        private string GetStatusText(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "Unknown";

            if (status.ToLower().Contains("successful"))
                return "✓ Successful";
            if (status.ToLower().Contains("pending"))
                return "⏳ Pending";
            if (status.ToLower().Contains("failed"))
                return "✗ Failed";

            return status;
        }

        private string FormatDate(string dateString)
        {
            if (DateTime.TryParse(dateString, out DateTime date))
            {
                return date.ToString("dd MMM yyyy, hh:mm tt");
            }
            return dateString;
        }

        private async void ShowNoDataFoundMessage()
        {
            ClearResults();
            await ShowInfoAlert("No Transactions Found",
                "No transaction records were found for the entered Patient ID. Please verify the ID and try again.");
        }

        private async void ShareButton_Clicked(object sender, EventArgs e)
        {
            try
            {
                var button = sender as Button;
                if (button != null) button.IsEnabled = false;

                UserDialogs.Instance.ShowLoading("Preparing receipt for sharing...");

                // Capture screenshot using Xamarin.Essentials Screenshot API
                var screenshot = await Screenshot.CaptureAsync();

                if (screenshot != null)
                {
                    var stream = await screenshot.OpenReadAsync();

                    // Save to temporary file
                    var fileName = $"PatientReceipt_{_currentPatientId}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);

                    using (var fileStream = File.Create(filePath))
                    {
                        await stream.CopyToAsync(fileStream);
                    }

                    // Share the file
                    await Share.RequestAsync(new ShareFileRequest
                    {
                        Title = "Patient Transaction Receipt",
                        File = new ShareFile(filePath)
                    });

                    UserDialogs.Instance.Toast("Receipt shared successfully!", TimeSpan.FromSeconds(2));
                }
                else
                {
                    await ShowErrorAlert("Error", "Failed to capture receipt screenshot.");
                }
            }
            catch (FeatureNotSupportedException)
            {
                await ShowErrorAlert("Not Supported", "Screenshot feature is not supported on this device.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Share error: {ex.Message}");
                await ShowErrorAlert("Error", "Failed to share receipt. Please try again.");
            }
            finally
            {
                UserDialogs.Instance.HideLoading();
                var button = sender as Button;
                if (button != null) button.IsEnabled = true;
            }
        }

        private async Task HandleHttpError(HttpResponseMessage response)
        {
            var statusCode = (int)response.StatusCode;
            string errorMessage;

            switch (statusCode)
            {
                case 400:
                    errorMessage = "Invalid request. Please check the Patient ID format.";
                    break;
                case 401:
                    errorMessage = "Authentication failed. Please contact support.";
                    break;
                case 403:
                    errorMessage = "Access denied. Please contact support.";
                    break;
                case 404:
                    errorMessage = "Patient records not found or service unavailable.";
                    break;
                case 429:
                    errorMessage = "Too many requests. Please wait a moment and try again.";
                    break;
                case 500:
                    errorMessage = "Server error occurred. Please try again later.";
                    break;
                default:
                    errorMessage = $"Request failed with status code: {statusCode}";
                    break;
            }

            await ShowErrorAlert("Service Error", errorMessage);
            Debug.WriteLine($"HTTP Error: {response.StatusCode} - {response.ReasonPhrase}");
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

        private async void OnBackNavClicked(object sender, EventArgs e)
        {
            try { await Navigation.PopAsync(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] Back navigation error: {ex}");
            }
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

        [JsonProperty("totalTransactions")]
        public int TotalTransactions { get; set; }

        [JsonProperty("totalAmount")]
        public decimal TotalAmount { get; set; }

        [JsonProperty("transactions")]
        public List<Transaction> Transactions { get; set; }
    }

    public class Transaction
    {
        [JsonProperty("datelIst")]
        public string DateList { get; set; }

        [JsonProperty("transactionId")]
        public string TransactionId { get; set; }

        [JsonProperty("serviceTypeName")]
        public string ServiceTypeName { get; set; }

        [JsonProperty("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("payer")]
        public string Payer { get; set; }

        [JsonProperty("revenueHead")]
        public string RevenueHead { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }
    }
}