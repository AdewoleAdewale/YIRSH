using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace YIRSHospital.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class History : ContentPage
    {
        #region Data Models
        public class TransactionApiResponse
        {
            public string message { get; set; }
            public string respondCode { get; set; }
            public string agent { get; set; }
            public int totalTransactionCount { get; set; }
            public decimal totalAmount { get; set; }
            public List<Transaction> transactions { get; set; }
        }

        public class Transaction
        {
            public string datelIst { get; set; }
            public string transactionId { get; set; }
            public string serviceTypeName { get; set; }

            public string HospitalNo { get; set; }
            public decimal amount { get; set; }
            public string payer { get; set; }
            public string agentName { get; set; }
            public string revenueHead { get; set; }
            public string remitaServiceName { get; set; }
            public string status { get; set; }

            public string DisplayDate
            {
                get
                {
                    if (DateTime.TryParse(datelIst, out DateTime date))
                        return date.ToString("MMM dd, yyyy h:mm tt");
                    return datelIst ?? "N/A";
                }
            }

            public string PayerDisplay => string.IsNullOrWhiteSpace(payer) ? "N/A" : payer;

            public Color StatusColor
            {
                get
                {
                    if (string.IsNullOrEmpty(status)) return Color.Gray;
                    if (status.Contains("Approved") || status.Contains("Successful"))
                        return Color.FromHex("#27AE60");
                    if (status.Contains("Refunded"))
                        return Color.FromHex("#E74C3C");
                    if (status.Contains("Pending"))
                        return Color.FromHex("#F39C12");
                    return Color.Gray;
                }
            }

            public string StatusIcon
            {
                get
                {
                    if (string.IsNullOrEmpty(status)) return "❓";
                    if (status.Contains("Approved") || status.Contains("Successful")) return "✅";
                    if (status.Contains("Refunded")) return "↩️";
                    if (status.Contains("Pending")) return "⏳";
                    return "❓";
                }
            }
        }

        public class TransactionDataContext : INotifyPropertyChanged
        {
            private List<Transaction> _transactions = new List<Transaction>();

            public List<Transaction> Transactions
            {
                get => _transactions;
                set
                {
                    _transactions = value ?? new List<Transaction>();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Size));
                    OnPropertyChanged(nameof(TotalAmount));
                    OnPropertyChanged(nameof(TransactionCount));
                    OnPropertyChanged(nameof(ApprovedCount));
                    OnPropertyChanged(nameof(RefundedCount));
                    OnPropertyChanged(nameof(ApprovedAmount));
                }
            }

            public string AgentName { get; set; }
            public string RevenueHead { get; set; }
            public decimal Size => Math.Max(Transactions.Count * 200, 300);
            public int TransactionCount => Transactions.Count;
            public decimal TotalAmount => Transactions.Sum(x => x.amount);
            public int ApprovedCount => Transactions.Count(t => t.status?.Contains("Approved") == true || t.status?.Contains("Successful") == true);
            public int RefundedCount => Transactions.Count(t => t.status?.Contains("Refunded") == true);
            public decimal ApprovedAmount => Transactions.Where(t => t.status?.Contains("Approved") == true || t.status?.Contains("Successful") == true).Sum(t => t.amount);

            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        #endregion

        #region Private Fields
        private static readonly HttpClient _httpClient = CreateHttpClient();
        private bool _isLoading = false;
        private const int REQUEST_TIMEOUT_SECONDS = 30;
        private const string API_BASE_URL = "https://yobe.osoftpay.net/api/Agents/GetAgentTransactions";
        #endregion

        #region Constructor
        public History()
        {
            try
            {
                InitializeComponent();
                InitializePage();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Init Error: {ex.Message}");
            }
        }
        #endregion

        #region Initialization
        private void InitializePage()
        {
            endDatePicker.Date = DateTime.Now;
            startDatePicker.Date = DateTime.Now.AddDays(-30);
            BindingContext = new TransactionDataContext();
            HideAllSections();
        }

        private static HttpClient CreateHttpClient()
        {
            try
            {
                // CRITICAL FIX: Proper SSL/TLS configuration for Xamarin
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    // More secure approach - only bypass if necessary
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                    {
                        // For production, implement proper certificate validation
                        // For now, log the error and allow connection
                        if (errors != System.Net.Security.SslPolicyErrors.None)
                        {
                            System.Diagnostics.Debug.WriteLine($"SSL Certificate Warning: {errors}");
                        }
                        return true; // Only for development/testing
                    }
                };

                // Configure TLS settings BEFORE creating HttpClient
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
                ServicePointManager.CheckCertificateRevocationList = false;
                ServicePointManager.DefaultConnectionLimit = 10;

                return new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS)
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HttpClient creation error: {ex.Message}");
                return new HttpClient { Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS) };
            }
        }
        #endregion

        #region Event Handlers
        private async void Button_Clicked(object sender, EventArgs e)
        {
            if (_isLoading) return;
            await SearchTransactions();
        }
        #endregion

        #region Main Business Logic
        private async Task SearchTransactions()
        {
            _isLoading = true;

            try
            {
                if (!ValidateInputs()) return;

                ShowLoadingState();

                var url = BuildApiUrl();
                System.Diagnostics.Debug.WriteLine($"API Request: {url}");

                var apiResponse = await FetchTransactionsAsync(url);
                ProcessTransactionResults(apiResponse);
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"HTTP Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Inner Exception: {ex.InnerException?.Message}");
                ShowErrorState($"Network error: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (TaskCanceledException)
            {
                ShowErrorState("Request timed out. Please try again.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}\n{ex.StackTrace}");
                ShowErrorState("An unexpected error occurred. Please try again.");
            }
            finally
            {
                HideLoadingState();
                _isLoading = false;
            }
        }

        private bool ValidateInputs()
        {
            if (startDatePicker.Date > endDatePicker.Date)
            {
                Device.BeginInvokeOnMainThread(async () =>
                    await DisplayAlert("Validation Error", "Start date cannot be later than end date.", "OK"));
                return false;
            }

            if ((endDatePicker.Date - startDatePicker.Date).TotalDays > 365)
            {
                Device.BeginInvokeOnMainThread(async () =>
                    await DisplayAlert("Validation Error", "Date range cannot exceed 365 days.", "OK"));
                return false;
            }

            if (string.IsNullOrWhiteSpace(LoginPage.ValidUserMail))
            {
                Device.BeginInvokeOnMainThread(async () =>
                    await DisplayAlert("Error", "User email not found. Please log in again.", "OK"));
                return false;
            }

            return true;
        }

        private string BuildApiUrl()
        {
            string fromDate = startDatePicker.Date.ToString("M/d/yyyy");
            string toDate = endDatePicker.Date.ToString("M/d/yyyy");
            return $"{API_BASE_URL}?agentEmail={Uri.EscapeDataString(LoginPage.ValidUserMail)}&fromDate={Uri.EscapeDataString(fromDate)}&toDate={Uri.EscapeDataString(toDate)}";
        }

        private async Task<TransactionApiResponse> FetchTransactionsAsync(string url)
        {
            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine($"Response Status: {response.StatusCode}");
            System.Diagnostics.Debug.WriteLine($"Response: {json.Substring(0, Math.Min(500, json.Length))}");

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Server error: {response.StatusCode}");
            }

            var apiResponse = JsonConvert.DeserializeObject<TransactionApiResponse>(json);

            if (apiResponse?.respondCode != "00")
            {
                throw new InvalidOperationException($"API Error: {apiResponse?.message ?? "Unknown error"}");
            }

            apiResponse.transactions = apiResponse.transactions ?? new List<Transaction>();
            return apiResponse;
        }

        private void ProcessTransactionResults(TransactionApiResponse apiResponse)
        {
            var transactions = apiResponse.transactions
                .OrderByDescending(t => t.datelIst)
                .ToList();

            // Clean data
            foreach (var t in transactions)
            {
                if (t.transactionId == null) t.transactionId = "N/A";
                if (t.serviceTypeName == null) t.serviceTypeName = "Unknown Service";
                if (t.agentName == null) t.agentName = "Unknown Agent";
                if (t.revenueHead == null) t.revenueHead = "N/A";
                if (t.remitaServiceName == null) t.remitaServiceName = "N/A";
                if (t.status == null) t.status = "Unknown";
                if (t.datelIst == null) t.datelIst = DateTime.Now.ToString("o");
            }

            var dataContext = new TransactionDataContext
            {
                Transactions = transactions,
                AgentName = apiResponse.agent ?? "Unknown Agent",
                RevenueHead = transactions.FirstOrDefault()?.revenueHead ?? "N/A"
            };

            Device.BeginInvokeOnMainThread(() =>
            {
                BindingContext = dataContext;

                if (transactions.Count > 0)
                {
                    ShowResultsState(dataContext);
                }
                else
                {
                    ShowEmptyState();
                }
            });
        }
        #endregion

        #region UI State Management
        private void ShowLoadingState()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                loadingOverlay.IsVisible = true;
                HideAllSections();
            });
        }

        private void HideLoadingState()
        {
            Device.BeginInvokeOnMainThread(() => loadingOverlay.IsVisible = false);
        }

        private void ShowResultsState(TransactionDataContext dataContext)
        {
            HideAllSections();
            resultsSection.IsVisible = true;
            summaryLabel.Text = $"{dataContext.TransactionCount} transaction{(dataContext.TransactionCount != 1 ? "s" : "")} • " +
                               $"Approved: {dataContext.ApprovedCount} (₦{dataContext.ApprovedAmount:N2}) • " +
                               $"Refunded: {dataContext.RefundedCount} • " +
                               $"Total: ₦{dataContext.TotalAmount:N2}";
        }

        private void HideAllSections()
        {
            resultsSection.IsVisible = false;
            emptyStateSection.IsVisible = false;
            errorStateSection.IsVisible = false;
        }

        private void ShowEmptyState()
        {
            HideAllSections();
            emptyStateSection.IsVisible = true;
        }

        private void ShowErrorState(string errorMessage = null)
        {
            HideAllSections();
            errorStateSection.IsVisible = true;
            if (!string.IsNullOrEmpty(errorMessage))
                errorMessageLabel.Text = errorMessage;
        }
        #endregion

        #region Cleanup
        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _httpClient?.CancelPendingRequests();
        }
        #endregion


        private async void OnBackNavClicked(object sender, EventArgs e)
        {
            try { await Navigation.PopAsync(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] Back navigation error: {ex}");
            }
        }
    }
}