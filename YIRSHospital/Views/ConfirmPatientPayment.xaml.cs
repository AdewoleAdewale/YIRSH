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
    public partial class ConfirmPatientPayment : ContentPage
    {
        public ConfirmPatientPayment()
        {
            InitializeComponent();
        }

        private async void OnConfirmPaymentClicked(object sender, EventArgs e)
        {
            string patientNo = SearchPatientEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(patientNo)) return;

            UserDialogs.Instance.ShowLoading("Confirming...");
            var result = await HospitalApiService.ConfirmPatientPaymentAsync(patientNo);
            UserDialogs.Instance.HideLoading();

            if (result.Success && result.Data?.Code == "00")
            {
                var data = result.Data;

                // UI Enhancements based on 24-hour validity
                bool isValidToday = data.IsRecent && data.PaymentStatus == "PAID TODAY";

                StatusCard.BackgroundColor = isValidToday ? Color.FromHex("#F0FDF4") : Color.FromHex("#FEF2F2");
                StatusCard.BorderColor = isValidToday ? Color.FromHex("#BBF7D0") : Color.FromHex("#FECACA");
                StatusCard.BorderThickness = 1;

                PaymentStatusLabel.Text = data.PaymentStatus;
                PaymentStatusLabel.TextColor = isValidToday ? Color.FromHex("#166534") : Color.FromHex("#991B1B");

                PatientNameLabel.Text = data.PatientName;
                ServiceNameLabel.Text = data.ServiceName;
                AmountPaidLabel.Text = $"₦{data.Amount:N2}";

                if (DateTime.TryParse(data.Date, out DateTime parsedDate))
                    PaymentDateLabel.Text = parsedDate.ToString("dd MMM yyyy, hh:mm tt");
                else
                    PaymentDateLabel.Text = data.Date;

                StatusCard.IsVisible = true;
            }
            else
            {
                StatusCard.IsVisible = false;
                await DisplayAlert("Error", result.Data?.Message ?? "No payment found for this patient in your department.", "OK");
            }
        }
    }
}