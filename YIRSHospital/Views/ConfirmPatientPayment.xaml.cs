using Acr.UserDialogs;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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
    public partial class ConfirmPatientPayment : ContentPage
    {
        private const int MaxRecentSearches = 6;

        private readonly ObservableCollection<string> _recentSearches = new ObservableCollection<string>();

        // Holds the last successfully confirmed record, so Share/Print/Copy actions
        // and pull-to-refresh don't need to re-parse the UI.
        private ConfirmPaymentResponse _currentResult;

        public ConfirmPatientPayment()
        {
            InitializeComponent();
            RecentSearchesView.ItemsSource = _recentSearches;
            ShowIdleState();
        }

        // ─────────────────────────────────────────────────────────
        //  SEARCH
        // ─────────────────────────────────────────────────────────

        private async void OnConfirmPaymentClicked(object sender, EventArgs e)
        {
            string patientNo = SearchPatientEntry.Text?.Trim();

            if (string.IsNullOrWhiteSpace(patientNo))
            {
                await DisplayAlert("Validation", "Please enter a patient number to search.", "OK");
                return;
            }

            await RunConfirmSearch(patientNo);
        }

        private async void OnRecentSearchSelected(object sender, Xamarin.Forms.SelectionChangedEventArgs e)
        {
            var picked = e.CurrentSelection?.FirstOrDefault() as string;
            RecentSearchesView.SelectedItem = null; // reset visual selection
            if (string.IsNullOrWhiteSpace(picked)) return;

            SearchPatientEntry.Text = picked;
            await RunConfirmSearch(picked);
        }

        private async void OnRefreshRequested(object sender, EventArgs e)
        {
            string patientNo = SearchPatientEntry.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(patientNo))
                await RunConfirmSearch(patientNo, isRefresh: true);

            PageRefreshView.IsRefreshing = false;
        }

        private void OnNewSearchClicked(object sender, EventArgs e)
        {
            SearchPatientEntry.Text = string.Empty;
            _currentResult = null;
            ShowIdleState();
            SearchPatientEntry.Focus();
        }

        private async Task RunConfirmSearch(string patientNo, bool isRefresh = false)
        {
            if (!isRefresh)
                UserDialogs.Instance.ShowLoading("Confirming...");

            try
            {
                var result = await HospitalApiService.ConfirmPatientPaymentAsync(patientNo);

                if (result.Success && result.Data != null)
                {
                    _currentResult = result.Data;
                    RememberSearch(patientNo);
                    RenderResult(result.Data);
                }
                else
                {
                    _currentResult = null;
                    ShowNotFoundState(result.ErrorMessage
                        ?? "No payment found for this patient in your department.");
                }
            }
            catch (Exception ex)
            {
                _currentResult = null;
                await DisplayAlert("Error", $"Something went wrong while confirming payment.\n{ex.Message}", "OK");
                ShowIdleState();
            }
            finally
            {
                if (!isRefresh)
                    UserDialogs.Instance.HideLoading();
            }
        }

        // ─────────────────────────────────────────────────────────
        //  RENDERING
        // ─────────────────────────────────────────────────────────

        private void RenderResult(ConfirmPaymentResponse data)
        {
            bool isValidToday = data.IsRecent && data.PaymentStatus == "PAID TODAY";

            // Icon + pill colours
            StatusIconBadge.BackgroundColor = isValidToday ? Color.FromHex("#D1FAE5") : Color.FromHex("#FEE2E2");
            StatusIconLabel.Text = isValidToday ? "✅" : "⚠️";

            StatusPill.BackgroundColor = isValidToday ? Color.FromHex("#F0FDF4") : Color.FromHex("#FEF2F2");
            PaymentStatusLabel.Text = data.PaymentStatus;
            PaymentStatusLabel.TextColor = isValidToday ? Color.FromHex("#166534") : Color.FromHex("#991B1B");

            RecencyLabel.Text = isValidToday
                ? "Verified — this payment was made today"
                : "Note — this payment was not made today. Double-check before proceeding.";

            // Patient information
            PatientNameLabel.Text = string.IsNullOrWhiteSpace(data.PatientName) ? "—" : data.PatientName;
            PatientNoLabel.Text = string.IsNullOrWhiteSpace(data.PatientNo) ? "—" : data.PatientNo;
            GenderLabel.Text = string.IsNullOrWhiteSpace(data.Gender) ? "—" : data.Gender;
            DepartmentLabel.Text = string.IsNullOrWhiteSpace(data.Department) ? "—" : data.Department;

            if (!string.IsNullOrWhiteSpace(data.PhoneNumber))
            {
                PhoneRow.IsVisible = true;
                CallPatientButton.IsVisible = true;
                PhoneNumberLabel.Text = data.PhoneNumber;
            }
            else
            {
                PhoneRow.IsVisible = false;
                CallPatientButton.IsVisible = false;
            }

            // Payment details
            ServiceNameLabel.Text = string.IsNullOrWhiteSpace(data.ServiceName) ? "—" : data.ServiceName;
            AmountPaidLabel.Text = $"₦{data.Amount:N2}";
            PaymentMethodLabel.Text = string.IsNullOrWhiteSpace(data.PaymentMethod) ? "—" : data.PaymentMethod;
            CashedByLabel.Text = string.IsNullOrWhiteSpace(data.CashedBy) ? "—" : data.CashedBy;

            if (DateTime.TryParse(data.Date, out DateTime parsedDate))
                PaymentDateLabel.Text = parsedDate.ToString("dd MMM yyyy, hh:mm tt");
            else
                PaymentDateLabel.Text = string.IsNullOrWhiteSpace(data.Date) ? "—" : data.Date;

            // Transaction reference
            TransactionIdLabel.Text = string.IsNullOrWhiteSpace(data.TransactionId) ? "N/A" : data.TransactionId;
            DebitRefLabel.Text = string.IsNullOrWhiteSpace(data.DebitRef) ? "N/A" : data.DebitRef;
            RevenueHeadLabel.Text = string.IsNullOrWhiteSpace(data.RevenueHead) ? "—" : data.RevenueHead;

            IdleStateCard.IsVisible = false;
            NotFoundCard.IsVisible = false;
            StatusCard.IsVisible = true;
        }

        private void ShowIdleState()
        {
            StatusCard.IsVisible = false;
            NotFoundCard.IsVisible = false;
            IdleStateCard.IsVisible = true;
        }

        private void ShowNotFoundState(string message)
        {
            StatusCard.IsVisible = false;
            IdleStateCard.IsVisible = false;
            NotFoundMessageLabel.Text = message;
            NotFoundCard.IsVisible = true;
        }

        // ─────────────────────────────────────────────────────────
        //  RECENT SEARCHES
        // ─────────────────────────────────────────────────────────

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

        // ─────────────────────────────────────────────────────────
        //  QUICK ACTIONS
        // ─────────────────────────────────────────────────────────

        private async void OnCallPatientTapped(object sender, EventArgs e)
        {
            if (_currentResult == null || string.IsNullOrWhiteSpace(_currentResult.PhoneNumber))
                return;

            try
            {
                PhoneDialer.Open(_currentResult.PhoneNumber);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Unable to Call", $"Could not open the dialer.\n{ex.Message}", "OK");
            }
        }

        private async void OnCopyTransactionIdClicked(object sender, EventArgs e)
        {
            if (_currentResult == null || string.IsNullOrWhiteSpace(_currentResult.TransactionId))
            {
                await DisplayAlert("Nothing to Copy", "No transaction ID is available for this record.", "OK");
                return;
            }

            await Clipboard.SetTextAsync(_currentResult.TransactionId);
            UserDialogs.Instance.Toast("Transaction ID copied");
        }

        private async void OnCopyDebitRefClicked(object sender, EventArgs e)
        {
            if (_currentResult == null || string.IsNullOrWhiteSpace(_currentResult.DebitRef))
            {
                await DisplayAlert("Nothing to Copy", "No debit reference is available for this record.", "OK");
                return;
            }

            await Clipboard.SetTextAsync(_currentResult.DebitRef);
            UserDialogs.Instance.Toast("Debit reference copied");
        }

        private async void OnShareClicked(object sender, EventArgs e)
        {
            if (_currentResult == null)
            {
                await DisplayAlert("Nothing to Share", "Confirm a payment first.", "OK");
                return;
            }

            var summary = BuildShareSummary(_currentResult);

            await Share.RequestAsync(new ShareTextRequest
            {
                Text = summary,
                Title = "Payment Confirmation"
            });
        }

        private static string BuildShareSummary(ConfirmPaymentResponse data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PAYMENT CONFIRMATION");
            sb.AppendLine($"Status: {data.PaymentStatus}");
            sb.AppendLine($"Patient: {data.PatientName} ({data.PatientNo})");
            sb.AppendLine($"Department: {data.Department}");
            sb.AppendLine($"Service: {data.ServiceName}");
            sb.AppendLine($"Amount: ₦{data.Amount:N2}");
            sb.AppendLine($"Payment Method: {data.PaymentMethod}");
            sb.AppendLine($"Date: {data.Date}");
            sb.AppendLine($"Transaction ID: {data.TransactionId}");
            if (!string.IsNullOrWhiteSpace(data.DebitRef))
                sb.AppendLine($"Debit Ref: {data.DebitRef}");
            return sb.ToString();
        }

        // NOTE: Reuses the same receipt-printing pipeline (BluetoothPrinterService /
        // ReceiptData / HospitalBranding) already wired up in UnifiedPatientWorkflow.
        // Confirm this matches the exact signatures in your project — those classes
        // weren't part of the files shared for this page, so verify on build.
        private async void OnPrintSlipClicked(object sender, EventArgs e)
        {
            if (_currentResult == null)
            {
                await DisplayAlert("Nothing to Print", "Confirm a payment first.", "OK");
                return;
            }

            if (!HospitalContext.IsSelected)
            {
                await DisplayAlert("Error", "No hospital is selected, so the slip cannot be branded. Please log in again.", "OK");
                return;
            }

            try
            {
                UserDialogs.Instance.ShowLoading("Printing verification slip…");

                var branding = HospitalBranding.Current;
                var data = _currentResult;

                var items = new List<ReceiptItem>
                {
                    new ReceiptItem { Description = "Patient Name", SubText = data.PatientName },
                    new ReceiptItem { Description = "Patient No", SubText = data.PatientNo },
                    new ReceiptItem { Description = "Department", SubText = data.Department },
                    new ReceiptItem { Description = "Service", SubText = data.ServiceName, Amount = data.Amount },
                    new ReceiptItem { Description = "Payment Method", SubText = data.PaymentMethod },
                    new ReceiptItem { Description = "Cashed By", SubText = data.CashedBy },
                };

                var receipt = new ReceiptData
                {
                    StoreName = branding.StoreName,
                    StorePhone = branding.Phone,
                    ReceiptBannerText = "PAYMENT VERIFICATION SLIP",
                    ReceiptNumber = string.IsNullOrWhiteSpace(data.TransactionId) ? "N/A" : data.TransactionId,
                    AgentName = LoginPage.Name,
                    CollectionPoint = data.Department,
                    PrintDate = DateTime.Now,
                    Items = items,
                    TotalAmount = data.Amount,
                    FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY"
                };

                using (var printer = new BluetoothPrinterService(use80mm: false))
                {
                    await printer.PrintReceiptAsync(receipt, branding.ResolveLogoAsset(), branding.WatermarkText);
                }

                UserDialogs.Instance.Toast("Verification slip printed");
            }
            catch (PrinterException pex)
            {
                await DisplayAlert("Printer Error", pex.Message, "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Print Failed", ex.Message, "OK");
            }
            finally
            {
                UserDialogs.Instance.HideLoading();
            }
        }
    }
}