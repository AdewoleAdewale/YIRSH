using Acr.UserDialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRSHospital.Services;

namespace YIRSHospital.Views.Staff
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class VerifyTransaction : ContentPage
    {
        public VerifyTransaction()
        {
            InitializeComponent();
        }

        private async void OnVerifyClicked(object sender, EventArgs e)
        {
            string txId = TransactionIdEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(txId))
            {
                await DisplayAlert("Notice", "Please enter a transaction ID.", "OK");
                return;
            }

            UserDialogs.Instance.ShowLoading("Verifying...");
            var result = await HospitalApiService.VerifyTransactionAsync(txId);
            UserDialogs.Instance.HideLoading();

            if (result.Success && result.Data?.code == "00")
            {
                var data = result.Data;
                ResultsCard.IsVisible = true;

                // Adjust header color based on status
                bool isSuccess = data.status.Contains("Approved") || data.paymentStatus.Contains("PAID");
                StatusHeader.BackgroundColor = isSuccess ? Color.FromHex("#10B981") : Color.FromHex("#EF4444");

                PaymentStatusLabel.Text = data.paymentStatus?.ToUpper() ?? "UNKNOWN";
                TransactionDateLabel.Text = data.date;
                PatientNameLabel.Text = data.patientName;
                PatientIdLabel.Text = data.patientNo;
                PaymentMethodLabel.Text = data.paymentMethod;
                TotalAmountLabel.Text = $"₦{data.totalAmount:N2}";

                ServicesListContainer.Children.Clear();
                if (data.services != null)
                {
                    foreach (var svc in data.services)
                    {
                        var serviceLayout = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } } };
                        serviceLayout.Children.Add(new Label { Text = svc.serviceTypeName, FontSize = 13, TextColor = Color.FromHex("#1A202C") }, 0, 0);
                        serviceLayout.Children.Add(new Label { Text = $"₦{svc.amount:N2}", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#1A202C") }, 1, 0);
                        ServicesListContainer.Children.Add(serviceLayout);
                    }
                }
            }
            else
            {
                ResultsCard.IsVisible = false;
                await DisplayAlert("Verification Failed", result.ErrorMessage ?? "Transaction not found.", "OK");
            }
        }
    }
}