using Acr.UserDialogs;
using System;
using System.Collections.Generic;
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
        // Master list (everything returned by the API) and the filtered list bound to the CollectionView.
        private readonly List<DepartmentServiceItem> _allServices = new List<DepartmentServiceItem>();
        private readonly ObservableCollection<DepartmentServiceItem> _visibleServices = new ObservableCollection<DepartmentServiceItem>();
        private ObservableCollection<DepartmentServiceItem> _availableServices = new ObservableCollection<DepartmentServiceItem>();

        private bool _suppressCheckEvents;
        private TaskCompletionSource<bool> _sheetResult;
        private bool _popOnSheetClose;

        public RaisePatientBill()
        {
            InitializeComponent();
            ServicesList.ItemsSource = _visibleServices;
            HospitalCodeLabel.Text = string.IsNullOrWhiteSpace(HospitalContext.Code) ? "—" : HospitalContext.Code;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (DepartmentPicker.ItemsSource == null)
            {
                await LoadDepartmentsAsync();
            }
        }

        protected override bool OnBackButtonPressed()
        {
            if (SheetOverlay.IsVisible)
            {
                Device.BeginInvokeOnMainThread(async () => await HideSheetAsync(false));
                return true;
            }
            return base.OnBackButtonPressed();
        }

        // ---------------- Data loading ----------------

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


        private async Task LoadServicesAsync(string departmentName)
        {
            UserDialogs.Instance.ShowLoading("Loading services...");
            var result = await HospitalApiService.GetDepartmentServicesAsync(HospitalContext.Code, departmentName);
            UserDialogs.Instance.HideLoading();

            _allServices.Clear();
            if (result.Success && result.Data?.Services != null)
            {
                foreach (var svc in result.Data.Services)
                {
                    _allServices.Add(svc);
                }
            }

            // This automatically populates _visibleServices and refreshes the CollectionView safely
            ApplyFilter(ServiceSearchEntry.Text);
            UpdateTotalAmount();
        }

        private async void OnDepartmentSelectedIndexChanged(object sender, EventArgs e)
        {
            if (DepartmentPicker.SelectedItem is HospitalDepartment selectedDept)
            {
                await LoadServicesAsync(selectedDept.name);
            }
        }



       

        private void OnServiceCheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            UpdateTotalAmount();
        }

        private void UpdateTotalAmount()
        {
            decimal total = _allServices.Where(s => s.IsSelected).Sum(s => s.Amount);
            TotalAmountLabel.Text = $"₦{total:N2}";
        }
        private void OnServiceSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter(e.NewTextValue);
        }

        private void ApplyFilter(string term)
        {
            _suppressCheckEvents = true;
            try
            {
                _visibleServices.Clear();

                var query = string.IsNullOrWhiteSpace(term)
                    ? _allServices
                    : _allServices.Where(s => !string.IsNullOrEmpty(s.ServiceName) &&
                                              s.ServiceName.IndexOf(term.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);

                foreach (var svc in query)
                    _visibleServices.Add(svc);
            }
            finally
            {
                _suppressCheckEvents = false;
            }
        }

        private void OnToggleSelectAllClicked(object sender, EventArgs e)
        {
            if (!_visibleServices.Any()) return;

            bool selectAll = !_visibleServices.All(s => s.IsSelected);

            _suppressCheckEvents = true;
            foreach (var svc in _visibleServices)
                svc.IsSelected = selectAll;
            _suppressCheckEvents = false;

            // Re-apply so checkbox visuals refresh even if the model doesn't raise PropertyChanged.
            var term = ServiceSearchEntry.Text;
            ApplyFilter(term);

            SelectAllButton.Text = selectAll ? "Clear all" : "Select all";
            UpdateTotalAmount();
        }

      

      

        private string DepartmentName() =>
            DepartmentPicker.SelectedItem is HospitalDepartment d ? d.name : "—";

        // ---------------- Submit ----------------

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

            var selectedServices = _allServices.Where(s => s.IsSelected).Select(s => new RaiseBillServiceItem
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
            var payload = new RaiseBillRequest
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

            if (result.Success && result.Data != null)
            {
                await DisplayAlert("Success", $"Bill raised for {result.Data.PatientName}. Total: ₦{result.Data.GrandTotal:N2}", "OK");
                await Navigation.PopAsync();
            }
            else
            {
                await DisplayAlert("Error", result.ErrorMessage ?? "The bill could not be raised.", "OK");
            }
        }
        // ---------------- Bottom sheet ----------------

        private Task ShowSheetAsync(bool success, string title, string message,
                                    IList<KeyValuePair<string, string>> details = null)
        {
            SheetTitleLabel.Text = title;
            SheetMessageLabel.Text = message;
            SheetIconLabel.Text = success ? "✓" : "!";
            SheetIconLabel.TextColor = success ? Color.FromHex("#15803D") : Color.FromHex("#DC2626");
            SheetIconShell.BackgroundColor = success ? Color.FromHex("#E7F5EC") : Color.FromHex("#FDECEC");
            SheetPrimaryButton.Text = success ? "DONE" : "CLOSE";
            SheetPrimaryButton.BackgroundColor = success
                ? Color.FromHex("#004225")
                : Color.FromHex("#DC2626");

            SheetDetailStack.Children.Clear();
            SheetDetailShell.IsVisible = details != null && details.Count > 0;
            if (details != null)
            {
                foreach (var row in details)
                {
                    var grid = new Grid { ColumnSpacing = 8 };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var key = new Label
                    {
                        Text = row.Key,
                        FontSize = 12,
                        TextColor = Color.FromHex("#718096")
                    };
                    var value = new Label
                    {
                        Text = row.Value,
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromHex("#1A202C"),
                        HorizontalTextAlignment = TextAlignment.End
                    };

                    grid.Children.Add(key, 0, 0);
                    grid.Children.Add(value, 1, 0);
                    SheetDetailStack.Children.Add(grid);
                }
            }

            _sheetResult = new TaskCompletionSource<bool>();

            SheetOverlay.IsVisible = true;
            SheetContainer.TranslationY = 400;

            Device.BeginInvokeOnMainThread(async () =>
            {
                await Task.WhenAll(
                    SheetOverlay.FadeTo(1, 10),
                    SheetOverlay.BackgroundColorTo(Color.FromRgba(0, 0, 0, 0.45), 180),
                    SheetContainer.TranslateTo(0, 0, 250, Easing.CubicOut));
            });

            return _sheetResult.Task;
        }

        private async Task HideSheetAsync(bool pop)
        {
            await Task.WhenAll(
                SheetContainer.TranslateTo(0, 400, 200, Easing.CubicIn),
                SheetOverlay.BackgroundColorTo(Color.FromRgba(0, 0, 0, 0), 180));

            SheetOverlay.IsVisible = false;
            _sheetResult?.TrySetResult(true);
            _sheetResult = null;

            if (pop && _popOnSheetClose)
            {
                _popOnSheetClose = false;
                await Navigation.PopAsync();
            }
        }

        private async void OnSheetPrimaryClicked(object sender, EventArgs e) => await HideSheetAsync(true);

        private async void OnSheetDismissTapped(object sender, EventArgs e) => await HideSheetAsync(true);
    }

    /// <summary>
    /// Small helper so the dim overlay can animate its background colour.
    /// </summary>
    internal static class ViewAnimationExtensions
    {
        public static Task<bool> BackgroundColorTo(this VisualElement self, Color target, uint length = 250, Easing easing = null)
        {
            var from = self.BackgroundColor;
            var tcs = new TaskCompletionSource<bool>();

            new Animation(t =>
            {
                self.BackgroundColor = Color.FromRgba(
                    from.R + (target.R - from.R) * t,
                    from.G + (target.G - from.G) * t,
                    from.B + (target.B - from.B) * t,
                    from.A + (target.A - from.A) * t);
            })
            .Commit(self, "BackgroundColorTo", 16, length, easing ?? Easing.Linear,
                    (v, c) => tcs.SetResult(true));

            return tcs.Task;
        }
    }
}