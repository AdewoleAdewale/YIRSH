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



        private async void OnNavigatePatientHistory(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new StaffBillHistory());
        }

        // ── Utility Actions ───────────────────────────────────────────

        private async void TestPrinter_Tapped()
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
            var action = await DisplayActionSheet("Settings", "Cancel", null, "Test Print","Change PIN", "Change Password", "Log Out");

            if (action == "Change PIN")
                await Navigation.PushModalAsync(new ChangePin());
            else if (action == "Change Password")
                await Navigation.PushModalAsync(new ChangePassword());
            else if (action == "Log Out")
                PerformLogout();
            else if (action == "Test Pint")
                TestPrinter_Tapped();
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

            /// <summary>Two-letter initials for the avatar badge, e.g. "Test Musa" → "TM".</summary>
            public string Initials => BuildInitials(LoginPage.Name);

            /// <summary>Sourced from StaffContext.Department, set at login from agent.department
            /// (e.g. "RADIOLOGY") and restored on app restart — see App.TryRestoreSessionAsync.</summary>
            public string DepartmentLabel =>
                string.IsNullOrWhiteSpace(StaffContext.Department) ? "No department" : StaffContext.Department;

            /// <summary>Sourced from HospitalContext, set at login from agent.hospitalName
            /// (e.g. "YOBE STATE SPECIALIST HOSPITAL").</summary>
            public string HospitalLabel => HospitalContext.Label;

            public string SecureSessionLabel => "Signed in securely · " + (LoginPage.ValidUserMail ?? "");

            private static string BuildInitials(string fullName)
            {
                if (string.IsNullOrWhiteSpace(fullName)) return "S";

                var parts = fullName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpperInvariant();

                return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
            }

            public ObservableCollection<RecentStaffBillModel> RecentBills { get; } = new ObservableCollection<RecentStaffBillModel>();
            public async Task LoadRecentBillsAsync()
            {
                try
                {
                    var email = LoginPage.ValidUserMail;
                    var code = HospitalContext.Code ?? "DEFAULT";

                    // Utilizing the newly adjusted API response
                    var result = await HospitalApiService.GetStaffBillHistoryAsync(email, code);

                    if (result.Success && result.Data?.Code == "00" && result.Data.BillGroups != null)
                    {
                        // Take only the top 5 most recent bills for the dashboard overview
                        var staffBills = result.Data.BillGroups
                            .OrderByDescending(b => b.DateRaised)
                            .Take(5)
                            .Select(b => new RecentStaffBillModel
                            {
                                PatientName = string.IsNullOrWhiteSpace(b.PatientName) ? "Unknown Patient" : b.PatientName,
                                DateRecorded = b.FormattedDate, // Uses the model's built-in date helper
                                Amount = b.GrandTotal,
                                Status = b.Status ?? "Pending",
                                StatusColor = b.StatusColor,
                                StatusBgColor = b.StatusBgColor
                            }).ToList();

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
                    System.Diagnostics.Debug.WriteLine($"[StaffDashboard] Failed to load recent bills: {ex.Message}");
                    // Silently fail so the dashboard remains operational
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

        public class RecentStaffBillModel
        {
            public string PatientName { get; set; }
            public string DateRecorded { get; set; }
            public decimal Amount { get; set; }
            public string Status { get; set; }
            public Color StatusColor { get; set; }
            public Color StatusBgColor { get; set; }
        }
    }
}