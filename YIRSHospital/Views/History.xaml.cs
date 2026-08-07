using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRSHospital.Services;

namespace YIRSHospital.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class History : ContentPage
    {
        #region Data Models

        /// <summary>
        /// View model for one row of AllHospitalPaymentHistory.
        ///
        /// The endpoint returns only five fields (department, serviceName,
        /// transactionId, amount, dateRecorded). The property names below are kept
        /// as they were so History.xaml binds without edits — but three of them are
        /// now fed from a different source, which is called out on each one.
        /// </summary>
        public class Transaction
        {
            public string datelIst { get; set; }
            public string transactionId { get; set; }

            /// <summary>API: serviceName.</summary>
            public string serviceTypeName { get; set; }

            /// <summary>Not returned by this endpoint — see notes.</summary>
            public string HospitalNo { get; set; }

            public decimal amount { get; set; }

            /// <summary>Not returned by this endpoint — see notes.</summary>
            public string payer { get; set; }

            /// <summary>The agent who ran the search, not the one who took payment.</summary>
            public string agentName { get; set; }

            /// <summary>Now carries the hospital name, not a revenue head.</summary>
            public string revenueHead { get; set; }

            /// <summary>Now carries the department.</summary>
            public string remitaServiceName { get; set; }

            /// <summary>
            /// Synthesised. This endpoint only records completed payments, so every
            /// row is a success; there is no status field to read.
            /// </summary>
            public string status { get; set; }

            [Newtonsoft.Json.JsonIgnore]
            public DateTime? RecordedAt { get; set; }

            public string DisplayDate
            {
                get
                {
                    if (RecordedAt.HasValue)
                        return RecordedAt.Value.ToString("MMM dd, yyyy h:mm tt");

                    return string.IsNullOrWhiteSpace(datelIst) ? "N/A" : datelIst;
                }
            }

            public string PayerDisplay
            {
                get { return string.IsNullOrWhiteSpace(payer) ? "N/A" : payer; }
            }

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

            /// <summary>Maps one API row onto the shape the ListView expects.</summary>
            public static Transaction FromApi(HospitalPaymentHistoryItem item, string agentName)
            {
                var recordedAt = item.RecordedAt;

                return new Transaction
                {
                    transactionId = string.IsNullOrWhiteSpace(item.transactionId) ? "N/A" : item.transactionId,
                    serviceTypeName = string.IsNullOrWhiteSpace(item.serviceName) ? "Unknown Service" : item.serviceName,
                    remitaServiceName = string.IsNullOrWhiteSpace(item.department) ? "N/A" : item.department,
                    revenueHead = HospitalContext.Label,
                    agentName = string.IsNullOrWhiteSpace(agentName) ? "N/A" : agentName,
                    amount = item.AmountValue,
                    RecordedAt = recordedAt,
                    datelIst = recordedAt.HasValue ? recordedAt.Value.ToString("o") : item.dateRecorded,
                    HospitalNo = "—",
                    payer = null,
                    status = "Successful"
                };
            }
        }

        public class TransactionDataContext : INotifyPropertyChanged
        {
            private List<Transaction> _transactions = new List<Transaction>();

            public List<Transaction> Transactions
            {
                get { return _transactions; }
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
            public string HospitalName { get; set; }

            public decimal Size { get { return Math.Max(Transactions.Count * 200, 300); } }
            public int TransactionCount { get { return Transactions.Count; } }
            public decimal TotalAmount { get { return Transactions.Sum(x => x.amount); } }

            public int ApprovedCount
            {
                get
                {
                    return Transactions.Count(t => t.status?.Contains("Approved") == true
                                                || t.status?.Contains("Successful") == true);
                }
            }

            public int RefundedCount
            {
                get { return Transactions.Count(t => t.status?.Contains("Refunded") == true); }
            }

            public decimal ApprovedAmount
            {
                get
                {
                    return Transactions
                        .Where(t => t.status?.Contains("Approved") == true
                                 || t.status?.Contains("Successful") == true)
                        .Sum(t => t.amount);
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        #endregion

        #region Private Fields
        private bool _isLoading;
        private CancellationTokenSource _cts;
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

            BindingContext = new TransactionDataContext
            {
                HospitalName = HospitalContext.Label
            };

            HideAllSections();
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

                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();

                var result = await HospitalApiService.GetPaymentHistoryAsync(
                    LoginPage.ValidUserMail,
                    startDatePicker.Date,
                    endDatePicker.Date,
                    HospitalContext.Code,
                    _cts.Token);

                if (!result.Success)
                {
                    ShowErrorState(result.ErrorMessage ?? "Could not load payment history.");
                    return;
                }

                ProcessTransactionResults(result.Data);
            }
            catch (OperationCanceledException)
            {
                // A newer search superseded this one — nothing to report.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] {ex}");
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
            if (!HospitalContext.IsSelected)
            {
                Device.BeginInvokeOnMainThread(async () =>
                    await DisplayAlert("Error", "No hospital selected. Please log in again.", "OK"));
                return false;
            }

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

        private void ProcessTransactionResults(List<HospitalPaymentHistoryItem> items)
        {
            var source = items ?? new List<HospitalPaymentHistoryItem>();

            var transactions = source
                .Select(i => Transaction.FromApi(i, LoginPage.Name))
                .OrderByDescending(t => t.RecordedAt ?? DateTime.MinValue)
                .ThenByDescending(t => t.transactionId)
                .ToList();

            var dataContext = new TransactionDataContext
            {
                Transactions = transactions,
                AgentName = LoginPage.Name ?? "Unknown Agent",
                HospitalName = HospitalContext.Label,
                RevenueHead = HospitalContext.Label
            };

            Device.BeginInvokeOnMainThread(() =>
            {
                BindingContext = dataContext;

                if (transactions.Count > 0)
                    ShowResultsState(dataContext);
                else
                    ShowEmptyState();
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

            summaryLabel.Text =
                $"{dataContext.HospitalName} • " +
                $"{dataContext.TransactionCount} transaction{(dataContext.TransactionCount != 1 ? "s" : "")} • " +
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
            Device.BeginInvokeOnMainThread(() =>
            {
                HideAllSections();
                emptyStateSection.IsVisible = true;
            });
        }

        private void ShowErrorState(string errorMessage = null)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                HideAllSections();
                errorStateSection.IsVisible = true;
                if (!string.IsNullOrEmpty(errorMessage))
                    errorMessageLabel.Text = errorMessage;
            });
        }
        #endregion

        #region Cleanup
        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] Cleanup error: {ex.Message}");
            }
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