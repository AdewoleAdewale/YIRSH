using Acr.UserDialogs;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Xamarin.Forms;
using YIRSHospital.Models;
using YIRSHospital.Services;

namespace YIRSHospital.Views
{
    public partial class RaisePatientBill : ContentPage
    {
        private ObservableCollection<DepartmentServiceItem> _availableServices = new ObservableCollection<DepartmentServiceItem>();

        public RaisePatientBill()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Load departments when page opens
            if (DepartmentPicker.ItemsSource == null)
            {
                await LoadDepartmentsAsync();
            }
        }

        private async Task LoadDepartmentsAsync()
        {
            UserDialogs.Instance.ShowLoading("Loading departments...");
            var result = await HospitalApiService.GetDepartmentsAsync(HospitalContext.Code);
            UserDialogs.Instance.HideLoading();

            if (result.Success && result.Data != null)
            {
                DepartmentPicker.ItemsSource = new ObservableCollection<HospitalDepartment>(result.Data);
            }
            else
            {
                await DisplayAlert("Error", result.ErrorMessage ?? "Could not load departments.", "OK");
            }
        }

        private async void OnDepartmentSelectedIndexChanged(object sender, EventArgs e)
        {
            if (DepartmentPicker.SelectedItem is HospitalDepartment selectedDept)
            {
                await LoadServicesAsync(selectedDept.name);
            }
        }

        private async Task LoadServicesAsync(string departmentName)
        {
            UserDialogs.Instance.ShowLoading("Loading services...");
            var result = await HospitalApiService.GetDepartmentServicesAsync(HospitalContext.Code, departmentName);
            UserDialogs.Instance.HideLoading();

            _availableServices.Clear();
            if (result.Success && result.Data?.Services != null)
            {
                foreach (var svc in result.Data.Services)
                {
                    _availableServices.Add(svc);
                }
            }

            ServicesList.ItemsSource = _availableServices;
            UpdateTotalAmount();
        }

        private void OnServiceCheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            UpdateTotalAmount();
        }

        private void UpdateTotalAmount()
        {
            decimal total = _availableServices.Where(s => s.IsSelected).Sum(s => s.Amount);
            TotalAmountLabel.Text = $"₦{total:N2}";
        }

        private async void OnRaiseBillClicked(object sender, EventArgs e)
        {
            var patientNo = PatientNoEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(patientNo))
            {
                await DisplayAlert("Validation", "Patient Number is required.", "OK");
                return;
            }

            if (!(DepartmentPicker.SelectedItem is HospitalDepartment selectedDept))
            {
                await DisplayAlert("Validation", "Please select a department.", "OK");
                return;
            }

            var selectedServices = _availableServices.Where(s => s.IsSelected).Select(s => new RaiseServicePayload
            {
                ServiceName = s.ServiceName,
                Amount = s.Amount
            }).ToList();

            if (!selectedServices.Any())
            {
                await DisplayAlert("Validation", "Select at least one service.", "OK");
                return;
            }

            UserDialogs.Instance.ShowLoading("Raising bill...");
            var payload = new RaisePatientBillRequest
            {
                PatientNo = patientNo,
                HospitalCode = HospitalContext.Code,
                Department = selectedDept.name,
                Email = LoginPage.ValidUserMail,
                Notes = NotesEntry.Text?.Trim(),
                Services = selectedServices
            };

            var result = await HospitalApiService.RaisePatientBillAsync(payload);
            UserDialogs.Instance.HideLoading();

            if (result.Success && result.Data?.Code == "00")
            {
                await DisplayAlert("Success", $"Bill raised for {result.Data.PatientName}. Total: ₦{result.Data.GrandTotal:N2}", "OK");
                await Navigation.PopAsync();
            }
            else
            {
                await DisplayAlert("Error", result.Data?.Message ?? result.ErrorMessage, "OK");
            }
        }
    }
}