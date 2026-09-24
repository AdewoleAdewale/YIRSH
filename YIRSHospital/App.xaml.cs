using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xamarin.Forms;
using YIRSHospital.Services;
using YIRSHospital.Views;

namespace YIRSHospital
{
    public partial class App : Application
    {
        public static string PrinterFooter { get; set; }
        public static string RevenueServiceName { get; set; }
        public static string CentralPortalURL { get; set; }
        public static string CentralPortalURLkeke { get; set; }
        public static string ThankYouMessage { get; set; }

        private const int SESSION_TIMEOUT_MINUTES = 60;
        private DateTime _lastActivityTime;
        private bool _isUserLoggedIn = false;
        private bool _isTimerRunning = false;

        public App()
        {
            InitializeComponent();


            MainPage = new NavigationPage(new LoginPage());
            _lastActivityTime = DateTime.Now;
        }
        protected override void OnStart()
        {
            // SessionService.Role/HospitalCode etc. are in-memory statics — on a
            // cold process start (app closed and reopened) they are never
            // populated, so checking them directly here always fell through to
            // the Hospital dashboard regardless of who was logged in. The actual
            // persisted session lives in SecureStorage/Preferences and has to be
            // loaded and its fields re-hydrated before we can route correctly.
            _ = TryRestoreSessionAsync();
        }

        protected override void OnSleep() { /* timer keeps running */ }

        protected override async void OnResume()
        {
            if (_isUserLoggedIn)
            {
                if (DateTime.Now - _lastActivityTime >= TimeSpan.FromMinutes(SESSION_TIMEOUT_MINUTES))
                    await HandleSessionTimeout();
                else
                {
                    UpdateLastActivity();
                    if (!_isTimerRunning) StartSessionTimer();
                }
            }
            else
            {
                // May have been cleared externally — try restore again
                await TryRestoreSessionAsync();
            }
        }

        // ── Session restore ───────────────────────────────────────────────

        private async Task TryRestoreSessionAsync()
        {
            try
            {
                var session = await SessionService.LoadAsync();

                if (session == null || !session.IsValid)
                {
                    Debug.WriteLine("[App] No valid session — showing login.");
                    return; // stay on LoginPage
                }

                Debug.WriteLine($"[App] Restoring session for {session.Email}");

                // Populate static fields exactly as login does
                LoginPage.Name = session.FullName;
                LoginPage.ValidUserMail = session.Email;
                LoginPage.category = session.Category;
                LoginPage.CollectionPoint = session.CollectionPoint;
                SessionService.MerchantNo = session.MerchantNo ?? string.Empty;

                // These two are what OnStart used to (incorrectly) read before
                // TryRestoreSessionAsync had ever run. Restore them from the
                // persisted session, falling back to Category for older sessions
                // saved before the Role field existed.
                SessionService.Role = string.IsNullOrWhiteSpace(session.Role) ? session.Category : session.Role;
                SessionService.CurrentDepartment = session.Department;

                if (!string.IsNullOrWhiteSpace(session.HospitalCode)) await HospitalContext.SelectAsync(session.HospitalCode, session.HospitalDisplayName);
                else await HospitalContext.RestoreAsync();
                if (!HospitalContext.IsSelected)
                {
                    Debug.WriteLine("[App] Session has no hospital — showing login.");
                    return;
                }

                // Billers are locked to one department (RaisePatientBill reads
                // StaffContext.Department) — restore that lock too, or a staff
                // member reopening the app loses their department scoping.
                await StaffContext.RestoreAsync();

                NavigateToDashboard(SessionService.Role);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] TryRestoreSessionAsync error: {ex.Message}");
                // Fail safe — stay on login
            }
        }

        // ── Navigation helpers ────────────────────────────────────────────

        public void NavigateToDashboard(string role = null)
        {
            IsUserLoggedIn = true;

            Page targetPage = LoginCategories.IsBillers(role)
                ? (Page)new Views.Staff.StaffDashboard()
                : new Dashboard();

            MainPage = new NavigationPage(targetPage);
            UpdateLastActivity();
        }

        public void Logout()
        {
            SessionService.Clear();
            IsUserLoggedIn = false;
            MainPage = new NavigationPage(new LoginPage());
        }

        // ── Session timer ─────────────────────────────────────────────────

        public bool IsUserLoggedIn
        {
            get => _isUserLoggedIn;
            set
            {
                _isUserLoggedIn = value;
                if (value) StartSessionTimer(); else StopSessionTimer();
            }
        }

        public void UpdateLastActivity() => _lastActivityTime = DateTime.Now;

        private async void StartSessionTimer()
        {
            if (_isTimerRunning) return;
            _isTimerRunning = true;

            while (_isUserLoggedIn && _isTimerRunning)
            {
                await Task.Delay(60_000);
                if (_isUserLoggedIn &&
                    DateTime.Now - _lastActivityTime >= TimeSpan.FromMinutes(SESSION_TIMEOUT_MINUTES))
                {
                    await HandleSessionTimeout();
                    break;
                }
            }

            _isTimerRunning = false;
        }

        private void StopSessionTimer() => _isTimerRunning = false;

        private async Task HandleSessionTimeout()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                await Current.MainPage.DisplayAlert("Session Expired",
                    "Your session expired due to inactivity. Please log in again.", "OK");
                Logout();
            });
        }

        public static new App Current => (App)Application.Current;
    }
}