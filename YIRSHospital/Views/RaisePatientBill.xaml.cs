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
    public partial class RaisePatientBill : ContentPage
    {
        public RaisePatientBill()
        {
            InitializeComponent();
        }


        private async void OnRaiseBillClicked(object sender, EventArgs e)
        {
            string patientNo = PatientNoEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(patientNo))
            {
                await DisplayAlert("Validation", "Patient Number is required.", "OK");
                return;
            }

            var selectedServices = _availableServices.Where(s => s.IsSelected).Select(s => new ServicePayload
            {
                ServiceName = s.serviceName,
                Amount = s.amount
            }).ToList();

            if (!selectedServices.Any())
            {
                await DisplayAlert("Validation", "Select at least one service.", "OK");
                return;
            }

            UserDialogs.Instance.ShowLoading("Raising bill...");

            var payload = new RaiseBillRequest
            {
                PatientNo = patientNo,
                HospitalCode = HospitalContext.Code,
                Department = SessionService.CurrentDepartment,
                Email = LoginPage.ValidUserMail,
                Notes = NotesEntry.Text?.Trim(),
                Services = selectedServices
            };

            var result = await HospitalApiService.RaisePatientBillAsync(payload);
            UserDialogs.Instance.HideLoading();

            if (result.Success && result.Data?.Code == "00")
            {
                await DisplayAlert("Success", $"Bill raised successfully for {result.Data.PatientName}. Total: ₦{result.Data.GrandTotal:N2}", "OK");
                await Navigation.PopAsync();
            }
            else
            {
                await DisplayAlert("Error", result.Data?.Message ?? result.ErrorMessage, "OK");
            }
        }
    }
}