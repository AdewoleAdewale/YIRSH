using Acr.UserDialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRSHospital.Services;

namespace YIRSHospital.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ProcessPatientBill : ContentPage
    {
        private const int MaxRecentSearches = 6;

        // Lightweight display wrapper so the CollectionView can show/hide
        // optional fields (Notes, Status, RaisedBy) without needing a value converter.
        private class PendingServiceDisplay
        {
            public string ServiceName { get; set; }
            public decimal Amount { get; set; }
            public string StatusText { get; set; }
            public bool HasStatus { get; set; }
            public string NotesText { get; set; }
            public bool HasNotes { get; set; }
            public string RaisedByText { get; set; }
            public bool HasRaisedBy { get; set; }
        }

        private readonly ObservableCollection<string> _recentSearches = new ObservableCollection<string>();

        private PatientBillResponse _currentBill;
        private ProcessBillResponse _lastResult;
        private string _selectedPaymentMethod = "Cash";
        private bool _pinVisible = false;

        public ProcessPatientBill()
        {
            InitializeComponent();
            RecentSearchesView.ItemsSource = _recentSearches;
            ShowIdleState();
        }

        // ─────────────────────────────────────────────────────────
        //  FETCH BILL
        // ─────────────────────────────────────────────────────────

        private async void OnFetchBillClicked(object sender, EventArgs e)
        {
            string patientNo = PatientSearchEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(patientNo))
            {
                await DisplayAlert("Validation", "Enter patient number.", "OK");
                return;
            }

            await RunFetch(patientNo);
        }

        private async void OnRecentSearchSelected(object sender, SelectionChangedEventArgs e)
        {
            var picked = e.CurrentSelection?.FirstOrDefault() as string;
            RecentSearchesView.SelectedItem = null;
            if (string.IsNullOrWhiteSpace(picked)) return;

            PatientSearchEntry.Text = picked;
            await RunFetch(picked);
        }

        private async void OnRefreshRequested(object sender, EventArgs e)
        {
            string patientNo = PatientSearchEntry.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(patientNo))
                await RunFetch(patientNo, isRefresh: true);

            PageRefreshView.IsRefreshing = false;
        }

        private async Task RunFetch(string patientNo, bool isRefresh = false)
        {
            if (!isRefresh)
                UserDialogs.Instance.ShowLoading("Fetching pending bill...");

            try
            {
                // Uses active hospital code from HospitalContext
                var result = await HospitalApiService.GetPatientBillAsync(patientNo, HospitalContext.Code);

                if (result.Success && result.Data != null && result.Data.Code == "00")
                {
                    _currentBill = result.Data;
                    RememberSearch(patientNo);
                    RenderBill(_currentBill);
                }
                else
                {
                    _currentBill = null;
                    ShowNotFoundState(result.Data?.Message ?? "No pending bill found.");
                }
            }
            catch (Exception ex)
            {
                _currentBill = null;
                await DisplayAlert("Error", $"Something went wrong while fetching the bill.\n{ex.Message}", "OK");
                ShowIdleState();
            }
            finally
            {
                if (!isRefresh)
                    UserDialogs.Instance.HideLoading();
            }
        }

        private void RenderBill(PatientBillResponse bill)
        {
            PatientNameLabel.Text = string.IsNullOrWhiteSpace(bill.PatientName) ? "—" : bill.PatientName;
            GenderLabel.Text = string.IsNullOrWhiteSpace(bill.Gender) ? "—" : bill.Gender;
            DepartmentLabel.Text = string.IsNullOrWhiteSpace(bill.Department) ? "—" : bill.Department;
            BillGroupIdLabel.Text = string.IsNullOrWhiteSpace(bill.BillGroupId) ? "—" : bill.BillGroupId;
            GrandTotalLabel.Text = $"₦{bill.GrandTotal:N2}";

            if (!string.IsNullOrWhiteSpace(bill.PhoneNumber))
            {
                PhoneRow.IsVisible = true;
                PhoneNumberLabel.Text = bill.PhoneNumber;
            }
            else
            {
                PhoneRow.IsVisible = false;
            }

            var services = bill.Services ?? new List<PendingBillServiceItem>();
            ServiceCountLabel.Text = services.Count == 1 ? "1 item" : $"{services.Count} items";

            var displayItems = services.Select(s => new PendingServiceDisplay
            {
                ServiceName = s.ServiceName,
                Amount = s.Amount,
                StatusText = s.Status,
                HasStatus = !string.IsNullOrWhiteSpace(s.Status),
                NotesText = string.IsNullOrWhiteSpace(s.Notes) ? null : $"Note: {s.Notes}",
                HasNotes = !string.IsNullOrWhiteSpace(s.Notes),
                RaisedByText = string.IsNullOrWhiteSpace(s.RaisedByName) ? null : $"Raised by {s.RaisedByName}",
                HasRaisedBy = !string.IsNullOrWhiteSpace(s.RaisedByName)
            }).ToList();

            PendingServicesCollection.ItemsSource = displayItems;

            IdleStateCard.IsVisible = false;
            NotFoundCard.IsVisible = false;
            ResultCard.IsVisible = false;
            BillDetailsCard.IsVisible = true;
        }

        private void ShowIdleState()
        {
            BillDetailsCard.IsVisible = false;
            NotFoundCard.IsVisible = false;
            ResultCard.IsVisible = false;
            IdleStateCard.IsVisible = true;
        }

        private void ShowNotFoundState(string message)
        {
            BillDetailsCard.IsVisible = false;
            IdleStateCard.IsVisible = false;
            ResultCard.IsVisible = false;
            NotFoundMessageLabel.Text = message;
            NotFoundCard.IsVisible = true;
        }

        private void RememberSearch(string patientNo)
        {
            var existing = _recentSearches.FirstOrDefault(s => string.Equals(s, patientNo, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                _recentSearches.Remove(existing);

            _recentSearches.Insert(0, patientNo);

            while (_recentSearches.Count > MaxRecentSearches)
                _recentSearches.RemoveAt(_recentSearches.Count - 1);

            RecentSearchesView.IsVisible = _recentSearches.Any();
        }

        private async void OnCallPatientTapped(object sender, EventArgs e)
        {
            if (_currentBill == null || string.IsNullOrWhiteSpace(_currentBill.PhoneNumber))
                return;

            try
            {
                PhoneDialer.Open(_currentBill.PhoneNumber);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Unable to Call", $"Could not open the dialer.\n{ex.Message}", "OK");
            }
        }

        // ─────────────────────────────────────────────────────────
        //  PAYMENT METHOD
        // ─────────────────────────────────────────────────────────

        private void OnCashSelected(object sender, EventArgs e)
        {
            _selectedPaymentMethod = "Cash";
            SetMethodButtonState(CashBtn, TransferBtn, CardBtn);
            ReferenceSection.IsVisible = false;
        }

        private void OnTransferSelected(object sender, EventArgs e)
        {
            _selectedPaymentMethod = "Transfer";
            SetMethodButtonState(TransferBtn, CashBtn, CardBtn);
            ReferenceSection.IsVisible = true;
        }

        private void OnCardSelected(object sender, EventArgs e)
        {
            _selectedPaymentMethod = "Card";
            SetMethodButtonState(CardBtn, CashBtn, TransferBtn);
            ReferenceSection.IsVisible = true;
        }

        private void SetMethodButtonState(Button active, Button inactive1, Button inactive2)
        {
            active.BackgroundColor = (Color)Resources["Primary"];
            active.TextColor = Color.White;

            var inactiveColor = (Color)Resources["TextPrimary"];

            inactive1.BackgroundColor = Color.FromHex("#E2E8F0");
            inactive1.TextColor = inactiveColor;

            inactive2.BackgroundColor = Color.FromHex("#E2E8F0");
            inactive2.TextColor = inactiveColor;
        }

        private void OnTogglePinVisibilityClicked(object sender, EventArgs e)
        {
            _pinVisible = !_pinVisible;
            PinEntry.IsPassword = !_pinVisible;
            TogglePinVisibilityButton.Text = _pinVisible ? "🙈" : "👁";
        }

        // ─────────────────────────────────────────────────────────
        //  PROCESS PAYMENT
        // ─────────────────────────────────────────────────────────

        private async void OnProcessPaymentClicked(object sender, EventArgs e)
        {
            if (_currentBill == null || _currentBill.Services == null || !_currentBill.Services.Any())
            {
                await DisplayAlert("Error", "No active pending bill loaded.", "OK");
                return;
            }

            string pin = PinEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4)
            {
                await DisplayAlert("Validation", "Enter your 4-digit PIN.", "OK");
                return;
            }

            string refCode = ReferenceEntry.Text?.Trim();
            if (_selectedPaymentMethod != "Cash" && string.IsNullOrWhiteSpace(refCode))
            {
                await DisplayAlert("Validation", "Payment Reference is required for Transfer or Card.", "OK");
                return;
            }

            bool confirmed = await DisplayAlert(
                "Confirm Payment",
                $"Process a {_selectedPaymentMethod} payment of ₦{_currentBill.GrandTotal:N2} for {_currentBill.PatientName}?",
                "Confirm", "Cancel");

            if (!confirmed) return;

            UserDialogs.Instance.ShowLoading("Processing bill payment...");

            var payload = new ProcessBillRequest
            {
                HospitalCode = HospitalContext.Code,
                HospitalNo = _currentBill.PatientNo,
                Department = _currentBill.Department,
                Email = LoginPage.ValidUserMail,
                Pin = pin,
                PaymentMethod = _selectedPaymentMethod,
                PaymentReference = _selectedPaymentMethod == "Cash" ? null : refCode,
                // Match services exactly per API spec: DRF pharmacy requires amount > 0, standard services 0
                Services = _currentBill.Services.Select(s => new ProcessBillServiceItem
                {
                    ServiceName = s.ServiceName,
                    Quantity = 1,
                    Amount = s.ServiceName.IndexOf("DRF", StringComparison.OrdinalIgnoreCase) >= 0 ? s.Amount : 0
                }).ToList()
            };

            try
            {
                var result = await HospitalApiService.ProcessPatientBillAsync(payload);

                if (result.Success && result.Data?.Code == "00")
                {
                    _lastResult = result.Data;
                    ShowResultCard(result.Data);
                }
                else
                {
                    await DisplayAlert("Payment Failed", result.Data?.Message ?? result.ErrorMessage, "OK");
                }
            }
            finally
            {
                UserDialogs.Instance.HideLoading();
            }
        }

        private void ShowResultCard(ProcessBillResponse data)
        {
            TransactionNoLabel.Text = string.IsNullOrWhiteSpace(data.TransactionNo) ? "N/A" : data.TransactionNo;
            DebitRefResultLabel.Text = string.IsNullOrWhiteSpace(data.DebitRef) ? "N/A" : data.DebitRef;
            ResultTotalAmountLabel.Text = $"₦{data.TotalAmount:N2}";
            ResultSubtitleLabel.Text = $"{data.PatientName} — {data.Department}";

            BillDetailsCard.IsVisible = false;
            IdleStateCard.IsVisible = false;
            NotFoundCard.IsVisible = false;
            ResultCard.IsVisible = true;
        }

        private async void OnCopyTransactionNoClicked(object sender, EventArgs e)
        {
            if (_lastResult == null || string.IsNullOrWhiteSpace(_lastResult.TransactionNo)) return;
            await Clipboard.SetTextAsync(_lastResult.TransactionNo);
            UserDialogs.Instance.Toast("Transaction number copied");
        }

        private async void OnCopyDebitRefClicked(object sender, EventArgs e)
        {
            if (_lastResult == null || string.IsNullOrWhiteSpace(_lastResult.DebitRef)) return;
            await Clipboard.SetTextAsync(_lastResult.DebitRef);
            UserDialogs.Instance.Toast("Debit reference copied");
        }

        private async void OnShareResultClicked(object sender, EventArgs e)
        {
            if (_lastResult == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("PAYMENT RECEIPT");
            sb.AppendLine($"Patient: {_lastResult.PatientName} ({_lastResult.PatientNo})");
            sb.AppendLine($"Department: {_lastResult.Department}");
            sb.AppendLine($"Transaction No: {_lastResult.TransactionNo}");
            sb.AppendLine($"Payment Method: {_lastResult.PaymentMethod}");
            if (!string.IsNullOrWhiteSpace(_lastResult.PaymentReference))
                sb.AppendLine($"Payment Reference: {_lastResult.PaymentReference}");
            sb.AppendLine($"Debit Ref: {_lastResult.DebitRef}");
            foreach (var b in _lastResult.Breakdowns ?? new List<BillBreakdownItem>())
                sb.AppendLine($" • {b.ServiceName} x{b.Quantity}: ₦{b.SubTotal:N2}");
            sb.AppendLine($"Total Amount: ₦{_lastResult.TotalAmount:N2}");

            await Share.RequestAsync(new ShareTextRequest
            {
                Text = sb.ToString(),
                Title = "Payment Receipt"
            });
        }

        private async void OnDoneClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}
