using Acr.UserDialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRSHospital.Services;

namespace YIRSHospital.Views.Staff
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class StaffDashboard : ContentPage
    {
        private readonly StaffDashboardViewModel _vm;
        public StaffDashboard()
        {
            InitializeComponent();
            _vm = new StaffDashboardViewModel();
            BindingContext = _vm;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _ = _vm.LoadRecentBillsAsync();
        }

        // ── Navigation Actions ────────────────────────────────────────

        private async void OnNavigateRaiseBill(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new RaisePatientBill());
        }

        private async void OnNavigateProcessBill(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new ProcessPatientBill());
        }

        private async void OnNavigateConfirmPayment(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new ConfirmPatientPayment());
        }

        private async void OnNavigatePatientHistory(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new PatientTransaction());
        }

        // ── Utility Actions ───────────────────────────────────────────

        private async void TestPrinter_Tapped(object sender, EventArgs e)
        {
            try
            {
                UserDialogs.Instance.ShowLoading("Testing Printer...");
                using (var printerService = new BluetoothPrinterService(use80mm: false))
                {
                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30)))
                    {
                        await printerService.PrintTestPageAsync(cts.Token);
                    }
                }
                UserDialogs.Instance.HideLoading();
                await DisplayAlert("Success", "Test print completed.", "OK");
            }
            catch (Exception ex)
            {
                UserDialogs.Instance.HideLoading();
                await DisplayAlert("Printer Error", ex.Message, "OK");
            }
        }

        private async void Settings_Tapped(object sender, EventArgs e)
        {
            var action = await DisplayActionSheet("Settings", "Cancel", null, "Change PIN", "Change Password", "Log Out");

            if (action == "Change PIN")
                await Navigation.PushModalAsync(new ChangePin());
            else if (action == "Change Password")
                await Navigation.PushModalAsync(new ChangePassword());
            else if (action == "Log Out")
                PerformLogout();
        }

        private void PerformLogout()
        {
            SessionService.Clear();
            Application.Current.MainPage = new NavigationPage(new LoginPage());
        }

        // ── View Model ────────────────────────────────────────────────

        public class StaffDashboardViewModel : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;

            public string Greeting => GetGreeting();
            public string StaffName => LoginPage.Name ?? "Staff Member";

            public ObservableCollection<RecentBillModel> RecentBills { get; } = new ObservableCollection<RecentBillModel>();

            public async Task LoadRecentBillsAsync()
            {
                try
                {
                    // Call the appropriate API to fetch bills raised by this staff member.
                    // Assuming GetPaymentHistoryAsync or a similar endpoint supports filtering by agent email.
                    var endDate = DateTime.Now;
                    var startDate = endDate.AddDays(-7);

                    var result = await HospitalApiService.GetRaiseBillHistoryAsync(
                        LoginPage.ValidUserMail, startDate, endDate, HospitalContext.Code, System.Threading.CancellationToken.None);

                    if (result.Success && result.Data != null)
                    {
                        var staffBills = result.Data
                            .OrderByDescending(b => b.RecordedAt)
                            .Take(10) // Show top 10 recent
                            .Select(b => new RecentBillModel
                            {
                                PatientName = string.IsNullOrWhiteSpace(b.payer) ? "Payer" : b.payer,
                                DateRecorded = b.RecordedAt?.ToString("MMM dd, yyyy - hh:mm tt") ?? b.dateRecorded,
                                Amount = b.AmountValue
                            });

                        Device.BeginInvokeOnMainThread(() =>
                        {
                            RecentBills.Clear();
                            foreach (var bill in staffBills)
                            {
                                RecentBills.Add(bill);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[StaffDashboard] Failed to load bills: {ex.Message}");
                }
            }

            private string GetGreeting()
            {
                var hour = DateTime.Now.Hour;
                if (hour < 12) return "Good morning";
                if (hour < 17) return "Good afternoon";
                return "Good evening";
            }

            protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public class RecentBillModel
        {
            public string PatientName { get; set; }
            public string DateRecorded { get; set; }
            public decimal Amount { get; set; }
        }
    }
}