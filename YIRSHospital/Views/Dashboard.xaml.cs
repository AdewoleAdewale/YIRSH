using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRSHospital.Services;

namespace YIRSHospital.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Dashboard : ContentPage
    {
        #region Constants

        private const string PRINTER_LAST_OK_KEY = "printer_last_ok_utc";
        private const int MAX_RETRY_COUNT = 3;
        private const int ACTIVITY_WINDOW_DAYS = 7;
        private const int ACTIVITY_ROW_LIMIT = 12;

        #endregion

        #region Fields

        private readonly DashboardViewModel _vm;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isInitialized;
        private bool _isLoadingData;
        private int _retryCount;
        private Exception _lastFetchError;

        #endregion

        #region Construction

        public Dashboard()
        {
            try
            {
                InitializeComponent();

                _vm = new DashboardViewModel();
                _vm.RefreshCommand = new Command(async () => await RefreshAsync(pullToRefresh: true));
                BindingContext = _vm;

                InitializeIdentity();

                Connectivity.ConnectivityChanged += OnConnectivityChanged;

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                HandleException(ex, "Dashboard failed to start");
            }
        }

        private void InitializeIdentity()
        {
            var agent = !string.IsNullOrWhiteSpace(LoginPage.Name) ? LoginPage.Name.Trim() : "Agent";

            _vm.Greeting = GetGreeting();
            _vm.AgentName = agent;
            _vm.AgentInitials = BuildInitials(agent);

            ApplyHospitalToViewModel();
            UpdateConnectivityStatus();
            UpdatePrinterStatus();
        }

        private void ApplyHospitalToViewModel()
        {
            if (HospitalContext.IsSelected)
            {
                _vm.HospitalName = HospitalBranding.Current.StoreName;
                _vm.HospitalSubline = BuildHospitalSubline();
            }
            else
            {
                _vm.HospitalName = "No hospital selected";
                _vm.HospitalSubline = "Tap to log in again";
            }
        }

        private static string BuildHospitalSubline()
        {
            var code = string.IsNullOrWhiteSpace(HospitalContext.Code) ? "—" : HospitalContext.Code;

            var point = !string.IsNullOrWhiteSpace(LoginPage.CollectionPoint)
                ? LoginPage.CollectionPoint.Trim()
                : HospitalContext.RevenueHead;

            return string.IsNullOrWhiteSpace(point) ? code : code + " · " + point;
        }

        private static string BuildInitials(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "AG";

            var parts = value.Split(new[] { ' ', '.', ',', '-' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0) return "AG";
            if (parts.Length == 1)
            {
                var single = parts[0];
                return (single.Length >= 2 ? single.Substring(0, 2) : single).ToUpperInvariant();
            }

            return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }

        private static string GetGreeting()
        {
            var hour = DateTime.Now.Hour;
            if (hour < 12) return "Good morning";
            if (hour < 17) return "Good afternoon";
            return "Good evening";
        }

        #endregion

        #region Data

        private async Task RefreshAsync(bool pullToRefresh)
        {
            if (_isLoadingData)
            {
                _vm.IsRefreshing = false;
                return;
            }

            _isLoadingData = true;
            _lastFetchError = null;

            try
            {
                if (!CheckInternetConnection())
                {
                    _vm.SetEmptyState("You're offline", "Reconnect to load today's collections.");
                    return;
                }

                if (!HospitalContext.IsSelected)
                {
                    _vm.SetEmptyState("No hospital selected", "Please log in again to select a hospital.");
                    return;
                }

                var endDate = DateTime.Now;
                var startDate = endDate.Date.AddDays(-ACTIVITY_WINDOW_DAYS);

                // Replicated directly from History.xaml.cs
                var result = await HospitalApiService.GetPaymentHistoryAsync(
                    LoginPage.ValidUserMail,
                    startDate,
                    endDate,
                    HospitalContext.Code,
                    CancellationToken.None);

                if (!result.Success)
                {
                    _lastFetchError = new Exception(result.ErrorMessage ?? "Could not load payment history.");
                    ApplyFetchFailureState();
                    return;
                }

                ApplyTransactions(result.Data);
            }
            catch (Exception ex)
            {
                _lastFetchError = ex;
                System.Diagnostics.Debug.WriteLine("[Dashboard] Refresh failed: " + ex.Message);
                ApplyFetchFailureState();
            }
            finally
            {
                _isLoadingData = false;
                Device.BeginInvokeOnMainThread(() => _vm.IsRefreshing = false);
            }
        }

        private void ApplyFetchFailureState()
        {
            if (ApiClient.IsTlsFailure(_lastFetchError))
            {
                _vm.SetEmptyState(
                    "Secure connection failed",
                    "This device could not verify the server's certificate. "
                    + "Update the app, then contact support if it persists.");
                return;
            }

            _vm.SetEmptyState("Couldn't load activity", "Pull down to try again.");
        }

        private void ApplyTransactions(List<HospitalPaymentHistoryItem> items)
        {
            var source = items ?? new List<HospitalPaymentHistoryItem>();
            var today = DateTime.Now.Date;

            var all = source
                .Select(i => RecentTransaction.FromApi(i, LoginPage.Name))
                .OrderByDescending(t => t.RecordedAt ?? DateTime.MinValue)
                .ThenByDescending(t => t.transactionId)
                .ToList();

            var todays = all.Where(t => t.RecordedAt.HasValue && t.RecordedAt.Value.Date == today).ToList();

            var collectedToday = todays.Sum(t => Math.Abs(t.amount));
            var weekTotal = all.Sum(t => Math.Abs(t.amount));
            var count = todays.Count;
            var average = count > 0 ? collectedToday / count : 0m;

            var latest = todays.FirstOrDefault();

            var rows = all.Take(ACTIVITY_ROW_LIMIT).ToList();

            Device.BeginInvokeOnMainThread(() =>
            {
                _vm.CollectedTodayText = FormatNaira(collectedToday);
                _vm.PaymentCountText = count.ToString(CultureInfo.InvariantCulture);
                _vm.AverageTicketText = count > 0 ? FormatNairaCompact(average) : "—";
                _vm.WeekTotalText = FormatNairaCompact(weekTotal);

                _vm.CollectedTodaySubline = count == 0
                    ? "No payments recorded yet today"
                    : count + (count == 1 ? " payment" : " payments")
                      + (latest != null && latest.RecordedAt.HasValue
                          ? " · last at " + latest.RecordedAt.Value.ToString("h:mm tt", CultureInfo.InvariantCulture)
                          : "");

                _vm.RecentTransactions.Clear();
                foreach (var row in rows)
                {
                    _vm.RecentTransactions.Add(row);
                }

                _vm.EmptyTitle = "Nothing collected yet";
                _vm.EmptyBody = "Payments you take will appear here.";
            });
        }

        private static string FormatNaira(decimal value)
        {
            return "₦" + value.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string FormatNairaCompact(decimal value)
        {
            if (value >= 1000000m)
                return "₦" + (value / 1000000m).ToString("0.#", CultureInfo.InvariantCulture) + "m";

            if (value >= 10000m)
                return "₦" + (value / 1000m).ToString("0.#", CultureInfo.InvariantCulture) + "k";

            return "₦" + value.ToString("N0", CultureInfo.InvariantCulture);
        }

        #endregion

        #region Models

        public class RecentTransaction
        {
            public string datelIst { get; set; }
            public string transactionId { get; set; }
            public string serviceTypeName { get; set; }
            public string HospitalNo { get; set; }
            public decimal amount { get; set; }
            public string payer { get; set; }
            public string agentName { get; set; }
            public string revenueHead { get; set; }
            public string remitaServiceName { get; set; }
            public string status { get; set; }

            public DateTime? RecordedAt { get; set; }

            public static RecentTransaction FromApi(HospitalPaymentHistoryItem item, string agentName)
            {
                return new RecentTransaction
                {
                    transactionId = string.IsNullOrWhiteSpace(item.transactionId) ? "N/A" : item.transactionId,
                    serviceTypeName = string.IsNullOrWhiteSpace(item.serviceName) ? "Unknown Service" : item.serviceName,
                    remitaServiceName = string.IsNullOrWhiteSpace(item.department) ? "N/A" : item.department,
                    revenueHead = HospitalContext.Label,
                    agentName = string.IsNullOrWhiteSpace(agentName) ? "N/A" : agentName,
                    amount = item.AmountValue,
                    RecordedAt = item.RecordedAt,
                    datelIst = item.RecordedAt.HasValue ? item.RecordedAt.Value.ToString("o", CultureInfo.InvariantCulture) : item.dateRecorded,
                    HospitalNo = "—",
                    payer = null,
                    status = "Paid"
                };
            }

            public string PrimaryLine
            {
                get
                {
                    if (!string.IsNullOrWhiteSpace(payer)) return payer.Trim();
                    return string.IsNullOrWhiteSpace(serviceTypeName) ? "Payment" : serviceTypeName.Trim();
                }
            }

            public string SecondaryLine
            {
                get
                {
                    var time = RecordedAt.HasValue
                        ? (RecordedAt.Value.Date == DateTime.Now.Date
                            ? RecordedAt.Value.ToString("h:mm tt", CultureInfo.InvariantCulture)
                            : RecordedAt.Value.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture))
                        : (datelIst ?? string.Empty).Trim();

                    var dept = string.IsNullOrWhiteSpace(remitaServiceName) || remitaServiceName == "N/A" ? null : remitaServiceName.Trim();

                    if (dept == null) return time;
                    return time.Length == 0 ? dept : dept + " · " + time;
                }
            }

            public string FormattedAmount => "₦" + Math.Abs(amount).ToString("N0", CultureInfo.InvariantCulture);

            public string Initials
            {
                get
                {
                    var source = PrimaryLine;
                    var parts = source.Split(new[] { ' ', '.', '-' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length >= 2)
                        return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();

                    return (source.Length >= 2 ? source.Substring(0, 2) : source).ToUpperInvariant();
                }
            }

            public string StatusDisplay => "Paid";
            public Color StatusColor => Color.FromHex("#0F6E56");
            public Color BadgeFill => Color.FromHex("#E1F5EE");
            public Color BadgeTextColor => Color.FromHex("#0F6E56");
        }

        #endregion

        #region Status strip

        private void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            UpdateConnectivityStatus();
        }

        private void UpdateConnectivityStatus()
        {
            try
            {
                var online = Connectivity.NetworkAccess == NetworkAccess.Internet;

                Device.BeginInvokeOnMainThread(() =>
                {
                    _vm.NetworkChipText = online ? "Online" : "Offline";
                    _vm.NetworkChipFill = online ? Color.FromHex("#E1F5EE") : Color.FromHex("#FCEBEB");
                    _vm.NetworkTextColor = online ? Color.FromHex("#0F6E56") : Color.FromHex("#A32D2D");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Dashboard] Connectivity update failed: " + ex.Message);
            }
        }

        private void UpdatePrinterStatus()
        {
            var stamp = Preferences.Get(PRINTER_LAST_OK_KEY, string.Empty);

            DateTime lastOk;
            var parsed = DateTime.TryParse(
                stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out lastOk);

            Device.BeginInvokeOnMainThread(() =>
            {
                if (!parsed)
                {
                    _vm.PrinterChipText = "Printer not tested";
                    _vm.PrinterChipFill = Color.FromHex("#FAEEDA");
                    _vm.PrinterTextColor = Color.FromHex("#854F0B");
                    return;
                }

                var local = lastOk.ToLocalTime();
                var isToday = local.Date == DateTime.Now.Date;

                _vm.PrinterChipText = isToday
                    ? "Printer OK " + local.ToString("h:mm tt", CultureInfo.InvariantCulture)
                    : "Printer OK " + local.ToString("MMM d", CultureInfo.InvariantCulture);

                _vm.PrinterChipFill = isToday ? Color.FromHex("#E1F5EE") : Color.FromHex("#FAEEDA");
                _vm.PrinterTextColor = isToday ? Color.FromHex("#0F6E56") : Color.FromHex("#854F0B");
            });
        }

        #endregion

        #region Navigation

        private async void NewPayment_Tapped(object sender, EventArgs e)
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                await SafeNavigateAsync(() => Navigation.PushAsync(new Views.UnifiedPatientWorkflow()));
            }, "Opening workflow…");
        }

        private async void PatientHistory_Tapped(object sender, EventArgs e)
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                await SafeNavigateAsync(() => Navigation.PushAsync(new Views.PatientTransaction()));
            }, "Loading patients…");
        }

        private async void ViewAllTransactions_Clicked(object sender, EventArgs e)
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                await SafeNavigateAsync(() => Navigation.PushAsync(new Views.History()));
            }, "Loading history…");
        }

        private async void HospitalChip_Tapped(object sender, EventArgs e)
        {
            try
            {
                var label = HospitalContext.IsSelected ? HospitalContext.Label : "no hospital";

                var confirmed = await DisplayAlert(
                    "Switch hospital",
                    "You're collecting for " + label + ".\n\nSwitching signs you out so you can log in "
                    + "against the other hospital. Continue?",
                    "Switch", "Stay");

                if (confirmed) PerformLogout();
            }
            catch (Exception ex)
            {
                HandleException(ex, "Could not switch hospital");
            }
        }

        private async void Logout_Tapped(object sender, EventArgs e)
        {
            try
            {
                var confirmed = await DisplayAlert(
                    "Log out", "Are you sure you want to log out?", "Log out", "Cancel");

                if (confirmed) PerformLogout();
            }
            catch (Exception ex)
            {
                HandleException(ex, "Logout failed");
            }
        }

        private void PerformLogout()
        {
            CleanupResources();

            SessionService.Clear();
            Preferences.Remove("IsLoggedIn");
            Preferences.Remove("UserToken");

            App.Current.Logout();

            Application.Current.MainPage = new NavigationPage(new LoginPage());
        }

        #endregion

        #region Settings

        private async void Settings_Tapped(object sender, EventArgs e)
        {
            try
            {
                var action = await DisplayActionSheet(
                    "Settings", "Cancel", null,
                    "Change PIN", "Change password", "App info", "Help and support");

                await HandleSettingsActionAsync(action);
            }
            catch (Exception ex)
            {
                HandleException(ex, "Settings menu failed");
            }
        }

        private async Task HandleSettingsActionAsync(string action)
        {
            try
            {
                switch (action)
                {
                    case "Change PIN":
                        await ExecuteWithLoadingAsync(async () =>
                        {
                            await SafeNavigateAsync(() => Navigation.PushModalAsync(new Views.ChangePin()));
                        }, "Loading…");
                        break;

                    case "Change password":
                        await ExecuteWithLoadingAsync(async () =>
                        {
                            await SafeNavigateAsync(() => Navigation.PushModalAsync(new Views.ChangePassword()));
                        }, "Loading…");
                        break;

                    case "App info":
                        await ShowAppInfoAsync();
                        break;

                    case "Help and support":
                        await ShowHelpSupportAsync();
                        break;
                }
            }
            catch (Exception ex)
            {
                HandleException(ex, "Settings action failed");
            }
        }

        private async Task ShowAppInfoAsync()
        {
            var hospital = HospitalContext.IsSelected
                ? HospitalContext.Label + " (" + HospitalContext.Code + ")"
                : "None selected";

            await DisplayAlert(
                "App info",
                "Version: " + VersionTracking.CurrentVersion
                + "\nBuild: " + VersionTracking.CurrentBuild
                + "\nHospital: " + hospital
                + "\nAgent: " + (LoginPage.ValidUserMail ?? "—"),
                "OK");
        }

        private async Task ShowHelpSupportAsync()
        {
            var action = await DisplayActionSheet(
                "Help and support", "Cancel", null, "Contact support", "Report an issue");

            switch (action)
            {
                case "Contact support":
                    await DisplayAlert("Contact support",
                        "Email: support@osoftpay.com\nPhone: +234 907 070 1616", "OK");
                    break;

                case "Report an issue":
                    await DisplayAlert("Report an issue",
                        "Send the hospital code, the time, and the transaction reference to "
                        + "support@osoftpay.com and the team will trace it.", "OK");
                    break;
            }
        }

        #endregion

        #region Printer

        private async void TestPrinter_Tapped(object sender, EventArgs e)
        {
            _retryCount = 0;

            await ExecuteWithLoadingAsync(async () =>
            {
                await TestPrinterAsync();
            }, "Testing printer…");
        }

        private async Task TestPrinterAsync()
        {
            try
            {
                await PrintTestReceiptAsync();

                Preferences.Set(PRINTER_LAST_OK_KEY, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                UpdatePrinterStatus();

                await DisplayAlert("Printer ready", "Test print completed.", "OK");
            }
            catch (PrinterException pex)
            {
                var retry = await DisplayAlert(
                    "Printer error",
                    pex.Message + "\n\nTry again?",
                    "Retry", "Cancel");

                if (retry && _retryCount < MAX_RETRY_COUNT)
                {
                    _retryCount++;
                    await Task.Delay(2000);
                    await TestPrinterAsync();
                }
            }
            catch (Exception ex)
            {
                HandleException(ex, "Printer test failed");
            }
        }

        private async Task PrintTestReceiptAsync()
        {
            using (var printerService = new BluetoothPrinterService(use80mm: false))
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                {
                    await printerService.PrintTestPageAsync(cts.Token);
                }
            }
        }

        #endregion

        #region Hospital confirmation

        private async Task ConfirmHospitalAsync()
        {
            if (!HospitalContext.IsSelected) return;

            var info = await HospitalApiService.GetHospitalInfoAsync(HospitalContext.Code);

            if (info.Success && info.Data != null)
            {
                await HospitalContext.SelectAsync(info.Data.code, info.Data.displayName);
                Device.BeginInvokeOnMainThread(ApplyHospitalToViewModel);
                return;
            }

            Device.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert(
                    "Hospital unavailable",
                    info.ErrorMessage ?? "Could not confirm your hospital. Please log in again.",
                    "OK");

                PerformLogout();
            });
        }

        #endregion

        #region Utilities

        private async Task ExecuteWithLoadingAsync(Func<Task> action, string loadingMessage = "Loading…")
        {
            try
            {
                ShowLoading(loadingMessage);
                await action();
            }
            catch (Exception ex)
            {
                HandleException(ex, "Operation failed");
            }
            finally
            {
                HideLoading();
            }
        }

        private async Task SafeNavigateAsync(Func<Task> navigationAction)
        {
            try
            {
                if (!CheckInternetConnection())
                {
                    await DisplayAlert("No connection",
                        "Check your internet connection and try again.", "OK");
                    return;
                }

                await navigationAction();
            }
            catch (Exception ex)
            {
                HandleException(ex, "Navigation failed");
            }
        }

        private bool CheckInternetConnection()
        {
            try
            {
                return Connectivity.NetworkAccess == NetworkAccess.Internet;
            }
            catch
            {
                return false;
            }
        }

        private void ShowLoading(string message)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                if (LoadingOverlay == null || LoadingText == null) return;

                LoadingText.Text = message;
                LoadingOverlay.IsVisible = true;
                LoadingOverlay.InputTransparent = false;
            });
        }

        private void HideLoading()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                if (LoadingOverlay == null) return;

                LoadingOverlay.IsVisible = false;
                LoadingOverlay.InputTransparent = true;
            });
        }

        private void HandleException(Exception ex, string context)
        {
            try
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    HideLoading();
                    await DisplayAlert(context, GetUserFriendlyErrorMessage(ex), "OK");
                    System.Diagnostics.Debug.WriteLine("[Dashboard] " + context + ": " + ex);
                });
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine("[Dashboard] Critical error in " + context + ": " + ex);
            }
        }

        private string GetUserFriendlyErrorMessage(Exception ex)
        {
            if (ex is PrinterException) return ex.Message;
            if (ApiClient.IsTlsFailure(ex)) return "Secure connection failed. This device could not verify the server's certificate.";
            if (ex is TaskCanceledException) return "That took too long. Please try again.";
            if (ex is HttpRequestException) return "Network error. Check your connection and try again.";
            if (ex is TimeoutException) return "The operation timed out. Please try again.";
            if (ex is UnauthorizedAccessException) return "Access denied. Check the app's permissions.";

            return "Something went wrong. Please try again.";
        }

        private void CleanupResources()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                Connectivity.ConnectivityChanged -= OnConnectivityChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Dashboard] Cleanup error: " + ex.Message);
            }
        }

        #endregion

        #region Lifecycle

        protected override void OnAppearing()
        {
            base.OnAppearing();

            if (!_isInitialized) return;

            try
            {
                _vm.Greeting = GetGreeting();

                UpdateConnectivityStatus();
                UpdatePrinterStatus();

                _ = ConfirmHospitalAsync();
                _ = RefreshAsync(pullToRefresh: false);
            }
            catch (Exception ex)
            {
                HandleException(ex, "Page load failed");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _cancellationTokenSource?.Cancel();
        }

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                var result = await DisplayAlert(
                    "Exit app", "Are you sure you want to close the app?", "Exit", "Stay");

                if (result)
                {
                    CleanupResources();
                    System.Diagnostics.Process.GetCurrentProcess().CloseMainWindow();
                }
            });

            return true;
        }

        #endregion

        #region View Model

        public class DashboardViewModel : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;

            public ObservableCollection<RecentTransaction> RecentTransactions { get; }
                = new ObservableCollection<RecentTransaction>();

            public Command RefreshCommand { get; set; }

            private bool _isRefreshing;
            public bool IsRefreshing
            {
                get { return _isRefreshing; }
                set { Set(ref _isRefreshing, value); }
            }

            private string _greeting = "Welcome";
            public string Greeting
            {
                get { return _greeting; }
                set { Set(ref _greeting, value); }
            }

            private string _agentName = "Agent";
            public string AgentName
            {
                get { return _agentName; }
                set { Set(ref _agentName, value); }
            }

            private string _agentInitials = "AG";
            public string AgentInitials
            {
                get { return _agentInitials; }
                set { Set(ref _agentInitials, value); }
            }

            private string _hospitalName = "—";
            public string HospitalName
            {
                get { return _hospitalName; }
                set { Set(ref _hospitalName, value); }
            }

            private string _hospitalSubline = "—";
            public string HospitalSubline
            {
                get { return _hospitalSubline; }
                set { Set(ref _hospitalSubline, value); }
            }

            private string _collectedTodayText = "₦0";
            public string CollectedTodayText
            {
                get { return _collectedTodayText; }
                set { Set(ref _collectedTodayText, value); }
            }

            private string _collectedTodaySubline = "Loading today's collections…";
            public string CollectedTodaySubline
            {
                get { return _collectedTodaySubline; }
                set { Set(ref _collectedTodaySubline, value); }
            }

            private string _paymentCountText = "—";
            public string PaymentCountText
            {
                get { return _paymentCountText; }
                set { Set(ref _paymentCountText, value); }
            }

            private string _averageTicketText = "—";
            public string AverageTicketText
            {
                get { return _averageTicketText; }
                set { Set(ref _averageTicketText, value); }
            }

            private string _weekTotalText = "—";
            public string WeekTotalText
            {
                get { return _weekTotalText; }
                set { Set(ref _weekTotalText, value); }
            }

            private string _networkChipText = "Checking…";
            public string NetworkChipText
            {
                get { return _networkChipText; }
                set { Set(ref _networkChipText, value); }
            }

            private Color _networkChipFill = Color.FromHex("#F1EFE8");
            public Color NetworkChipFill
            {
                get { return _networkChipFill; }
                set { Set(ref _networkChipFill, value); }
            }

            private Color _networkTextColor = Color.FromHex("#5F5E5A");
            public Color NetworkTextColor
            {
                get { return _networkTextColor; }
                set { Set(ref _networkTextColor, value); }
            }

            private string _printerChipText = "Printer not tested";
            public string PrinterChipText
            {
                get { return _printerChipText; }
                set { Set(ref _printerChipText, value); }
            }

            private Color _printerChipFill = Color.FromHex("#FAEEDA");
            public Color PrinterChipFill
            {
                get { return _printerChipFill; }
                set { Set(ref _printerChipFill, value); }
            }

            private Color _printerTextColor = Color.FromHex("#854F0B");
            public Color PrinterTextColor
            {
                get { return _printerTextColor; }
                set { Set(ref _printerTextColor, value); }
            }

            private string _emptyTitle = "Loading…";
            public string EmptyTitle
            {
                get { return _emptyTitle; }
                set { Set(ref _emptyTitle, value); }
            }

            private string _emptyBody = "Fetching your recent collections.";
            public string EmptyBody
            {
                get { return _emptyBody; }
                set { Set(ref _emptyBody, value); }
            }

            public void SetEmptyState(string title, string body)
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    RecentTransactions.Clear();
                    EmptyTitle = title;
                    EmptyBody = body;
                    CollectedTodaySubline = body;
                });
            }

            private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
            {
                if (EqualityComparer<T>.Default.Equals(field, value)) return;

                field = value;

                var handler = PropertyChanged;
                if (handler != null) handler(this, new PropertyChangedEventArgs(name));
            }
        }

        #endregion
    }
}