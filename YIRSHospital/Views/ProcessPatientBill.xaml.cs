using Acr.UserDialogs;
using Android.Accounts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRSHospital.Models;
using YIRSHospital.Services;
using static Android.Content.ClipData;
using static Android.InputMethodServices.Keyboard;
using static System.Net.Mime.MediaTypeNames;

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

           // DRF lines that came back from the API with a zero amount need the
           // cashier to key the amount in; everything else shows the fixed amount.
           public bool NeedsAmountInput { get; set; }
           public bool ShowAmount { get { return !NeedsAmountInput; } }
            public string EnteredAmountText { get; set; }
        }

        /// <summary>
        /// Only these departments bill DRF with a custom amount; everywhere else the
        /// amount must go up as 0. Matching on "contains DRF" alone would catch any
        /// service name with those three letters in it.
        /// </summary>
        private static readonly string[] DrfDepartments =
        {
            "PHAMARCY", "GOPD PHARMACY", "MAIN PHARMACY", "A & E PHARMACY", "O AND G PHARMACY"
        };

        private readonly ObservableCollection<string> _recentSearches = new ObservableCollection<string>();

        private PatientBillResponse _currentBill;
        private ProcessBillResponse _lastResult;
        private List<PendingServiceDisplay> _displayItems = new List<PendingServiceDisplay>();
        private string _selectedPaymentMethod = "Cash";
        private bool _pinVisible = false;

        public ProcessPatientBill()
        {
            InitializeComponent();
            RecentSearchesView.ItemsSource = _recentSearches;
            ShowIdleState();
        }


        private static bool IsDrfService(string serviceName)
        {
          return (serviceName ?? string.Empty).IndexOf("DRF", StringComparison.OrdinalIgnoreCase) >= 0;
        }

       private static bool TryParseAmount(string text, out decimal amount)
        {
          amount = 0;
           if (string.IsNullOrWhiteSpace(text)) return false;
           return decimal.TryParse(text.Replace(",", string.Empty).Trim(),
          NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0;
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

                if (result.Success && result.Data != null)
                {
                    _currentBill = result.Data;
                    RememberSearch(patientNo);
                    RenderBill(_currentBill);
                }
                else
                {
                    // ErrorMessage now carries the mapped response code as well as
                    // network failures — showing "No pending bill found" for a dropped
                    // connection was sending agents to look for a bill that exists.
                    _currentBill = null;
                    ShowNotFoundState(result.ErrorMessage ?? "No pending bill found.");
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
                HasRaisedBy = !string.IsNullOrWhiteSpace(s.RaisedByName),
                NeedsAmountInput = IsDrfService(s.ServiceName) && s.Amount <= 0
            }).ToList();

            _displayItems = displayItems;
            PendingServicesCollection.HeightRequest = displayItems.Any(d => d.NeedsAmountInput) ? 300 : 180;
            PendingServicesCollection.ItemsSource = displayItems;
            IdleStateCard.IsVisible = false;
            NotFoundCard.IsVisible = false;
            ResultCard.IsVisible = false;
            BillDetailsCard.IsVisible = true;
            IdleStateCard.IsVisible = false;
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

        private void OnDrfAmountChanged(object sender, TextChangedEventArgs e)
        {
            var item = (sender as Entry)?.BindingContext as PendingServiceDisplay;
           if (item == null) return;

            item.EnteredAmountText = e.NewTextValue;
           UpdateGrandTotalLabel();
       }

       /// <summary>Bill total plus any DRF amounts the cashier has keyed in so far.</summary>
       private decimal GetEffectiveTotal()
        {
           if (_currentBill == null) return 0;

           decimal total = _currentBill.GrandTotal;
           foreach (var item in _displayItems.Where(d => d.NeedsAmountInput))
            {
                decimal entered;
               if (TryParseAmount(item.EnteredAmountText, out entered))
                   total += entered;
           }
           return total;
       }

       private void UpdateGrandTotalLabel()
        {
    GrandTotalLabel.Text = $"₦{GetEffectiveTotal():N2}";
            }
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
                await DisplayAlert(
                    "Validation",
                    "Payment Reference is required for Transfer or Card.",
                    "OK");

                return;
            }

            foreach (var item in _displayItems.Where(d => d.NeedsAmountInput))
            {
                decimal enteredAmount;
                if (!TryParseAmount(item.EnteredAmountText, out enteredAmount))
                {
                    await DisplayAlert(
                        "Validation",
                        $"Enter an amount greater than zero for {item.ServiceName}.",
                        "OK");

                    return;
                }
            }

            bool confirmed = await DisplayAlert(
                "Confirm Payment",
                $"Process a {_selectedPaymentMethod} payment of " +
                $"₦{GetEffectiveTotal():N2} for {_currentBill.PatientName}?",
                "Confirm",
                "Cancel");

            if (!confirmed)
                return;

            UserDialogs.Instance.ShowLoading("Processing bill payment...");

            bool isDrfDepartment = DrfDepartments.Contains(
                (_currentBill.Department ?? string.Empty).Trim(),
                StringComparer.OrdinalIgnoreCase);

            var payload = new ProcessBillRequest
            {
                HospitalCode = HospitalContext.Code,
                HospitalNo = _currentBill.PatientNo,
                Department = _currentBill.Department,
                Email = LoginPage.ValidUserMail,
                Pin = pin,
                PaymentMethod = _selectedPaymentMethod,
                PaymentReference = refCode,
                Services = _currentBill.Services.Select((service, index) =>
                {
                    decimal amount = 0;

                    if (isDrfDepartment && IsDrfService(service.ServiceName))
                    {
                        amount = service.Amount;

                        var row = index < _displayItems.Count
                            ? _displayItems[index]
                            : null;

                        decimal enteredAmount;
                        if (amount <= 0 &&
                            row != null &&
                            TryParseAmount(row.EnteredAmountText, out enteredAmount))
                        {
                            amount = enteredAmount;
                        }
                    }

                    return new ProcessBillServiceItem
                    {
                        ServiceName = service.ServiceName,
                        Quantity = 1,
                        Amount = amount
                    };
                }).ToList()
            };

            try
            {
                var result = await HospitalApiService.ProcessPatientBillAsync(payload);

                if (result.Success && result.Data != null)
                {
                    _lastResult = result.Data;
                    ShowResultCard(result.Data);
                    return;
                }

                var code = result.Data?.Code;

                if (HospitalResponseCodes.RequiresRefetch(code))
                {
                    bool refetch = await DisplayAlert(
                        "Bill Out of Date",
                        result.ErrorMessage,
                        "Fetch Again",
                        "Cancel");

                    if (refetch)
                        await RunFetch(_currentBill.PatientNo);

                    return;
                }

                if (HospitalResponseCodes.IsTransient(code))
                {
                    bool retry = await DisplayAlert(
                        "Wallet Unavailable",
                        result.ErrorMessage,
                        "Retry",
                        "Cancel");

                    if (retry)
                        OnProcessPaymentClicked(sender, e);

                    return;
                }

                await DisplayAlert(
                    "Payment Failed",
                    result.ErrorMessage ?? "The payment could not be completed.",
                    "OK");
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

        private async void OnPrintReceiptClicked(object sender, EventArgs e)
        {
            if (_lastResult == null) return;

            try
            {
                UserDialogs.Instance.ShowLoading("Printing receipt...");

                // Map the bill breakdown to the printer's expected item format
                var receiptItems = _lastResult.Breakdowns?.Select(b => new ReceiptItem
                {
                    Description = b.ServiceName,
                    Amount = b.Amount, // Maps the individual service amount
                }).ToList() ?? new List<ReceiptItem>();

                // Build the receipt payload with Barcode and QR Code triggers
                var receiptData = new ReceiptData
                {
                    StoreName = HospitalContext.Label ?? "YOBE STATE SPECIALIST HOSPITAL",
                    ReceiptBannerText = "OFFICIAL  RECEIPT",
                    ReceiptNumber = _lastResult.TransactionNo, // Triggers the global native 1D Barcode at the bottom
                    BarcodeLabel = $"https://yobe.osoftpay.net/Singlecollections/Verify?TransactId={_lastResult.TransactionNo}", // Triggers the native QR Code verification link
                    AgentName = LoginPage.Name,
                    CollectionPoint = LoginPage.CollectionPoint ?? _lastResult.Department,
                    PatientName = PatientNameLabel.Text ?? _lastResult.PatientName, // Binds patient name
                    PatientNo = _lastResult.PatientNo,    // Binds patient number
                    PrintDate = DateTime.Now,
                    Items = receiptItems,
                    TotalAmount = _lastResult.TotalAmount,
                    FooterLine2 = "POWERED BY OSOFTPAY"
                };

                using (var printerService = new BluetoothPrinterService(use80mm: false))
                {
                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30)))
                    {
                        // Print with Logo, Watermark Text, and Watermark Logo configured underneath the receipt
                        await printerService.PrintReceiptAsync(
                            receiptData,
                            logoAssetName: "Logo.png",
                            watermarkMode: WatermarkMode.Both,
                            watermarkText: "PAID",
                            watermarkLogoAssetName: "Logo.png",
                            cancellationToken: cts.Token
                        );
                    }
                }

                UserDialogs.Instance.HideLoading();
                UserDialogs.Instance.Toast("Receipt printed successfully.");
            }
            catch (Exception ex)
            {
                UserDialogs.Instance.HideLoading();
                await DisplayAlert("Printer Error", $"Could not print receipt: {ex.Message}", "OK");
            }
        }
        private async void OnDoneClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}