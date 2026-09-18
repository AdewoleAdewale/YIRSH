using Acr.UserDialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRSHospital.Services;

namespace YIRSHospital.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ProcessPatientBill : ContentPage
    {
        private PatientBillResponse _currentBill;
        private string _selectedPaymentMethod = "Cash";
        public ProcessPatientBill()
        {
            InitializeComponent();
        }
        private async void OnFetchBillClicked(object sender, EventArgs e)
        {
            string patientNo = PatientSearchEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(patientNo))
            {
                await DisplayAlert("Validation", "Enter patient number.", "OK");
                return;
            }

            UserDialogs.Instance.ShowLoading("Fetching pending bill...");
            // Uses active hospital code from HospitalContext
            var result = await HospitalApiService.GetPatientBillAsync(patientNo, HospitalContext.Code);
            UserDialogs.Instance.HideLoading();

            if (result.Success && result.Data != null && result.Data.Code == "00")
            {
                _currentBill = result.Data;
                PatientNameLabel.Text = _currentBill.PatientName;
                DepartmentLabel.Text = _currentBill.Department;
                BillGroupIdLabel.Text = _currentBill.BillGroupId;
                GrandTotalLabel.Text = $"₦{_currentBill.GrandTotal:N2}";

                PendingServicesCollection.ItemsSource = _currentBill.Services;
                BillDetailsCard.IsVisible = true;
            }
            else
            {
                BillDetailsCard.IsVisible = false;
                await DisplayAlert("Notice", result.Data?.Message ?? "No pending bill found.", "OK");
            }
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
            active.BackgroundColor = (Color)Application.Current.Resources["Primary"];
            active.TextColor = Color.White;

            inactive1.BackgroundColor = Color.FromHex("#E2E8F0");
            inactive1.TextColor = (Color)Application.Current.Resources["TextPrimary"];

            inactive2.BackgroundColor = Color.FromHex("#E2E8F0");
            inactive2.TextColor = (Color)Application.Current.Resources["TextPrimary"];
        }

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

            var result = await HospitalApiService.ProcessPatientBillAsync(payload);
            UserDialogs.Instance.HideLoading();

            if (result.Success && result.Data?.Code == "00")
            {
                await DisplayAlert("Payment Successful",
                    $"Txn No: {result.Data.TransactionNo}\nTotal: ₦{result.Data.TotalAmount:N2}\nDebit Ref: {result.Data.DebitRef}",
                    "OK");
                await Navigation.PopAsync();
            }
            else
            {
                await DisplayAlert("Payment Failed", result.Data?.Message ?? result.ErrorMessage, "OK");
            }
        }
    }
}