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
    public partial class RaisePatientBill : ContentPage
    {
        // Master list of every service returned by the API for this department.
        private ObservableCollection<DepartmentServiceItem> _availableServices = new ObservableCollection<DepartmentServiceItem>();

        // The last successfully raised bill, kept around for Share/Copy actions.
        private RaiseBillResponse _lastResult;

        public RaisePatientBill()
        {
            InitializeComponent();
            HeaderSubtitleLabel.Text = $"Raise service bills for patients — {SessionService.CurrentDepartment}";
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (!_availableServices.Any())
            {
                await LoadDepartmentServices();
            }
        }

        private async void OnRefreshRequested(object sender, EventArgs e)
        {
            await LoadDepartmentServices();
            PageRefreshView.IsRefreshing = false;
        }

        private async Task LoadDepartmentServices()
        {
            UserDialogs.Instance.ShowLoading("Loading services...");

            var result = await HospitalApiService.GetDepartmentServicesAsync(HospitalContext.Code, SessionService.CurrentDepartment);

            UserDialogs.Instance.HideLoading();

            if (result.Success && result.Data?.Code == "00" && result.Data.Services != null)
            {
                _availableServices = new ObservableCollection<DepartmentServiceItem>(result.Data.Services);
                ApplyServiceFilter(ServiceFilterEntry?.Text);
                RecalculateSelection();
            }
            else
            {
                await DisplayAlert("Error", result.Data?.Message ?? "Could not load department services.", "OK");
            }
        }

        // ─────────────────────────────────────────────────────────
        //  FILTER / SELECT ALL / CLEAR
        // ─────────────────────────────────────────────────────────

        private void OnServiceFilterTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyServiceFilter(e.NewTextValue);
        }

        private void ApplyServiceFilter(string query)
        {
            IEnumerable<DepartmentServiceItem> filtered = _availableServices;

            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = _availableServices.Where(s =>
                    !string.IsNullOrWhiteSpace(s.ServiceName) &&
                    s.ServiceName.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var filteredList = filtered.ToList();
            ServicesList.ItemsSource = filteredList;
            NoServicesLabel.IsVisible = !filteredList.Any();
        }

        private void OnSelectAllClicked(object sender, EventArgs e)
        {
            foreach (var service in ServicesList.ItemsSource?.Cast<DepartmentServiceItem>() ?? Enumerable.Empty<DepartmentServiceItem>())
                service.IsSelected = true;

            // CheckBox binding doesn't auto-refresh visuals for items already rendered
            // when toggled from code, so force the CollectionView to redraw.
            RefreshServicesList();
            RecalculateSelection();
        }

        private void OnClearAllClicked(object sender, EventArgs e)
        {
            foreach (var service in _availableServices)
                service.IsSelected = false;

            RefreshServicesList();
            RecalculateSelection();
        }

        private void RefreshServicesList()
        {
            var current = ServicesList.ItemsSource;
            ServicesList.ItemsSource = null;
            ServicesList.ItemsSource = current;
        }

        private void OnServiceCheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            RecalculateSelection();
        }

        private void RecalculateSelection()
        {
            var selected = _availableServices.Where(s => s.IsSelected).ToList();
            decimal total = selected.Sum(s => s.Amount);
            TotalAmountLabel.Text = $"₦{total:N2}";
            SelectedCountLabel.Text = selected.Count == 1 ? "1 selected" : $"{selected.Count} selected";
        }

        // ─────────────────────────────────────────────────────────
        //  RAISE BILL
        // ─────────────────────────────────────────────────────────

        private async void OnRaiseBillClicked(object sender, EventArgs e)
        {
            string patientNo = PatientNoEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(patientNo))
            {
                await DisplayAlert("Validation", "Patient Number is required.", "OK");
                return;
            }

            var selectedServices = _availableServices
                .Where(s => s.IsSelected)
                .Select(s => new RaiseBillServiceItem
                {
                    ServiceName = s.ServiceName,
                    Amount = s.Amount
                }).ToList();

            if (!selectedServices.Any())
            {
                await DisplayAlert("Validation", "Select at least one service.", "OK");
                return;
            }

            decimal total = selectedServices.Sum(s => s.Amount);
            bool confirmed = await DisplayAlert(
                "Confirm Bill",
                $"Raise a bill for patient {patientNo} covering {selectedServices.Count} service(s), totalling ₦{total:N2}?",
                "Raise Bill", "Cancel");

            if (!confirmed) return;

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
                _lastResult = result.Data;
                ShowSuccessCard(result.Data);
            }
            else
            {
                await DisplayAlert("Error", result.Data?.Message ?? result.ErrorMessage, "OK");
            }
        }

        private void ShowSuccessCard(RaiseBillResponse data)
        {
            BillGroupIdResultLabel.Text = string.IsNullOrWhiteSpace(data.BillGroupId) ? "N/A" : data.BillGroupId;
            ResultPatientNameLabel.Text = data.PatientName;
            ResultServicesCountLabel.Text = data.TotalServices > 0
                ? data.TotalServices.ToString()
                : (data.Services?.Count ?? 0).ToString();
            ResultGrandTotalLabel.Text = $"₦{data.GrandTotal:N2}";
            SuccessSubtitleLabel.Text = string.IsNullOrWhiteSpace(data.RaisedBy)
                ? "Ready for cashier payment."
                : $"Raised by {data.RaisedBy}";

            FormSection.IsVisible = false;
            SuccessCard.IsVisible = true;
        }

        private void OnRaiseAnotherClicked(object sender, EventArgs e)
        {
            _lastResult = null;
            PatientNoEntry.Text = string.Empty;
            NotesEntry.Text = string.Empty;
            ServiceFilterEntry.Text = string.Empty;

            foreach (var service in _availableServices)
                service.IsSelected = false;

            ApplyServiceFilter(null);
            RecalculateSelection();

            SuccessCard.IsVisible = false;
            FormSection.IsVisible = true;
        }

        // ─────────────────────────────────────────────────────────
        //  QUICK ACTIONS ON THE RESULT CARD
        // ─────────────────────────────────────────────────────────

        private async void OnCopyBillGroupIdClicked(object sender, EventArgs e)
        {
            if (_lastResult == null || string.IsNullOrWhiteSpace(_lastResult.BillGroupId))
                return;

            await Clipboard.SetTextAsync(_lastResult.BillGroupId);
            UserDialogs.Instance.Toast("Bill Group ID copied");
        }

        private async void OnShareResultClicked(object sender, EventArgs e)
        {
            if (_lastResult == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("BILL RAISED");
            sb.AppendLine($"Patient: {_lastResult.PatientName} ({_lastResult.PatientNo})");
            sb.AppendLine($"Department: {_lastResult.Department}");
            sb.AppendLine($"Bill Group ID: {_lastResult.BillGroupId}");
            foreach (var svc in _lastResult.Services ?? new System.Collections.Generic.List<RaisedServiceItem>())
                sb.AppendLine($" • {svc.ServiceName}: ₦{svc.Amount:N2}");
            sb.AppendLine($"Grand Total: ₦{_lastResult.GrandTotal:N2}");

            await Share.RequestAsync(new ShareTextRequest
            {
                Text = sb.ToString(),
                Title = "Bill Raised"
            });
        }
    }
}
