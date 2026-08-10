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
        private readonly HttpClient _httpClient;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isInitialized;
        private bool _isLoadingData;
        private int _retryCount;

        #endregion

        #region Construction

        public Dashboard()
        {
            try
            {
                InitializeComponent();

                _httpClient = new HttpClient();
                _httpClient.Timeout = TimeSpan.FromSeconds(30);

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

        /// <summary>
        /// Everything that is known the moment the page is constructed: who the
        /// agent is, which hospital they are transacting for, and what the
        /// device thinks its network and printer state are.
        /// </summary>
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

        /// <summary>
        /// The hospital chip is the single most important piece of state on this
        /// screen — an agent collecting against the wrong revenue head is only
        /// discovered at receipt-print time, which is far too late.
        /// </summary>
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

        /// <summary>
        /// One network call feeds the whole screen. Today's total, the payment
        /// count, the average ticket and the seven-day total are all derived
        /// client-side from the same list that renders the activity rows, so
        /// there is nothing extra to fetch and nothing that can disagree.
        /// </summary>
        private async Task RefreshAsync(bool pullToRefresh)
        {
            if (_isLoadingData)
            {
                _vm.IsRefreshing = false;
                return;
            }

            _isLoadingData = true;

            try
            {
                if (!CheckInternetConnection())
                {
                    _vm.SetEmptyState(
                        "You're offline",
                        "Reconnect to load today's collections.");
                    return;
                }

                var transactions = await FetchTransactionsAsync();

                if (transactions == null)
                {
                    _vm.SetEmptyState(
                        "Couldn't load activity",
                        "Pull down to try again.");
                    return;
                }

                ApplyTransactions(transactions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Dashboard] Refresh failed: " + ex.Message);

                _vm.SetEmptyState(
                    "Couldn't load activity",
                    "Pull down to try again.");
            }
            finally
            {
                _isLoadingData = false;
                Device.BeginInvokeOnMainThread(() => _vm.IsRefreshing = false);
            }
        }

        /// <summary>
        /// The endpoint renders dates day-first ("10/08/26") but it is not
        /// documented which way round it wants the search parameters, and the
        /// two readings are indistinguishable for days 1-12. So: ask month-first
        /// (what the app has always sent), and if that comes back empty, ask
        /// again day-first before concluding there is genuinely no activity.
        /// </summary>
        private async Task<List<RecentTransaction>> FetchTransactionsAsync()
        {
            var endDate = DateTime.Now;
            var startDate = endDate.Date.AddDays(-ACTIVITY_WINDOW_DAYS);

            var rows = await RequestTransactionsAsync(startDate, endDate, "MM/dd/yyyy");

            if (rows != null && rows.Count > 0) return rows;

            var fallback = await RequestTransactionsAsync(startDate, endDate, "dd/MM/yyyy");

            if (fallback != null && fallback.Count > 0) return fallback;

            return rows ?? fallback;
        }

 

        private async Task<List<RecentTransaction>> RequestTransactionsAsync(DateTime from, DateTime to, string dateFormat)
        {
            var url = "https://yobe.osoftpay.net/api/TaskPayers/gettransaction"
                    + "?Email=" + Uri.EscapeDataString(LoginPage.ValidUserMail ?? string.Empty)
                    + "&SearchFrom=" + Uri.EscapeDataString(from.ToString(dateFormat, CultureInfo.InvariantCulture))
                    + "&SearchTo=" + Uri.EscapeDataString(to.ToString(dateFormat, CultureInfo.InvariantCulture));

            var response = await _httpClient.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[Dashboard] gettransaction " + (int)response.StatusCode + ": " + Snippet(body));
                return null;
            }

            if (string.IsNullOrWhiteSpace(body)) return new List<RecentTransaction>();

            // The endpoint answers with a bare object (not an array) when the
            // agent has no records, so don't let that read as a hard failure.
            if (!body.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                System.Diagnostics.Debug.WriteLine("[Dashboard] Non-array payload: " + Snippet(body));
                return new List<RecentTransaction>();
            }

            try
            {
                // DateParseHandling.None keeps Newtonsoft from trying to coerce
                // the date strings itself — RecentTransaction parses them.
                var settings = new JsonSerializerSettings
                {
                    DateParseHandling = DateParseHandling.None
                };

                var parsed = JsonConvert.DeserializeObject<List<RecentTransaction>>(body, settings);
                return parsed ?? new List<RecentTransaction>();
            }
            catch (JsonException jex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[Dashboard] Could not read transactions: " + jex.Message + " | " + Snippet(body));
                return null;
            }
        }

        private static string Snippet(string value)
        {
            if (string.IsNullOrEmpty(value)) return "(empty)";
            return value.Length <= 300 ? value : value.Substring(0, 300) + "…";
        }

        private void ApplyTransactions(List<RecentTransaction> all)
        {
            var today = DateTime.Now.Date;

            // Rows whose date could not be parsed still count toward the seven-day
            // figure but are kept out of "today" rather than guessed into it.
            var todays = all.Where(t => t.HasValidDate && t.DateRecorded.Date == today).ToList();

            var collectedToday = todays.Sum(t => Math.Abs(t.Amount));
            var weekTotal = all.Sum(t => Math.Abs(t.Amount));
            var count = todays.Count;
            var average = count > 0 ? collectedToday / count : 0m;

            var latest = todays
                .OrderByDescending(t => t.DateRecorded)
                .FirstOrDefault();

            var rows = all
                .OrderByDescending(t => t.DateRecorded)
                .Take(ACTIVITY_ROW_LIMIT)
                .ToList();

            Device.BeginInvokeOnMainThread(() =>
            {
                _vm.CollectedTodayText = FormatNaira(collectedToday);
                _vm.PaymentCountText = count.ToString(CultureInfo.InvariantCulture);
                _vm.AverageTicketText = count > 0 ? FormatNairaCompact(average) : "—";
                _vm.WeekTotalText = FormatNairaCompact(weekTotal);

                _vm.CollectedTodaySubline = count == 0
                    ? "No payments recorded yet today"
                    : count + (count == 1 ? " payment" : " payments")
                      + " · last at " + latest.DateRecorded.ToString("h:mm tt", CultureInfo.InvariantCulture);

                _vm.RecentTransactions.Clear();
                foreach (var row in rows) _vm.RecentTransactions.Add(row);

                _vm.EmptyTitle = "Nothing collected yet";
                _vm.EmptyBody = "Payments you take will appear here.";
            });
        }

        private static string FormatNaira(decimal value)
        {
            return "₦" + value.ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>Keeps the three small stat tiles from wrapping on narrow screens.</summary>
        private static string FormatNairaCompact(decimal value)
        {
            if (value >= 1000000m)
                return "₦" + (value / 1000000m).ToString("0.#", CultureInfo.InvariantCulture) + "m";

            if (value >= 10000m)
                return "₦" + (value / 1000m).ToString("0.#", CultureInfo.InvariantCulture) + "k";

            return "₦" + value.ToString("N0", CultureInfo.InvariantCulture);
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

        /// <summary>
        /// The app cannot read the printer's bond state from netstandard, so
        /// rather than invent a status it reports the last successful test —
        /// which is the thing an agent actually wants to know before a shift.
        /// </summary>
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

        /// <summary>
        /// Switching hospital is a re-authentication, not a silent context swap:
        /// the agent's credentials are scoped to one hospital's revenue head.
        /// </summary>
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

            // Stale or withdrawn hospital — don't let them transact against it.
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

                // Returning from a payment should show the money, not a stale total.
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

        #region Models

        /// <summary>
        /// Shape returned by /api/TaskPayers/gettransaction. The display members
        /// are derived so the row template binds directly with no converters.
        /// </summary>
        public class RecentTransaction
        {
            // ── Wire shape ────────────────────────────────────────────────────
            // Every field arrives as a JSON string, including the amount. Typing
            // any of these as DateTime or decimal makes Newtonsoft throw on the
            // first row and return null for the entire list.

            [JsonProperty("businessName")]
            public string BusinessName { get; set; }

            [JsonProperty("serviceName")]
            public string ServiceName { get; set; }

            [JsonProperty("payerId")]
            public string PayerId { get; set; }

            [JsonProperty("transactionId")]
            public string TransactionId { get; set; }

            [JsonProperty("amount")]
            public string AmountRaw { get; set; }

            [JsonProperty("dateRecorded")]
            public string DateRecordedRaw { get; set; }

            /// <summary>Absent from this endpoint today; tolerated if it appears.</summary>
            [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
            public string Status { get; set; }

            // ── Derived ───────────────────────────────────────────────────────

            private static readonly string[] DateFormats =
            {
                "dd/MM/yy hh:mm tt",
                "dd/MM/yyyy hh:mm tt",
                "dd/MM/yy HH:mm",
                "dd/MM/yyyy HH:mm",
                "dd/MM/yy",
                "dd/MM/yyyy"
            };

            private bool _dateResolved;
            private DateTime _dateRecorded;

            /// <summary>
            /// Day-first, confirmed against live data: the feed returns 10/08/26
            /// for 10 August. Parsing month-first would silently reorder the list
            /// rather than error, so the formats are explicit and invariant.
            /// </summary>
            [JsonIgnore]
            public DateTime DateRecorded
            {
                get
                {
                    if (_dateResolved) return _dateRecorded;
                    _dateResolved = true;

                    var raw = (DateRecordedRaw ?? string.Empty).Trim();

                    if (raw.Length > 0)
                    {
                        DateTime parsed;

                        if (DateTime.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture,
                                                   DateTimeStyles.None, out parsed))
                        {
                            _dateRecorded = parsed;
                            return _dateRecorded;
                        }

                        // en-GB is day-first too, so this stays consistent with
                        // the explicit formats above rather than fighting them.
                        if (DateTime.TryParse(raw, new CultureInfo("en-GB"),
                                              DateTimeStyles.None, out parsed))
                        {
                            _dateRecorded = parsed;
                            return _dateRecorded;
                        }

                        System.Diagnostics.Debug.WriteLine("[Dashboard] Unparsed date: " + raw);
                    }

                    _dateRecorded = DateTime.MinValue;
                    return _dateRecorded;
                }
            }

            [JsonIgnore]
            public bool HasValidDate
            {
                get { return DateRecorded != DateTime.MinValue; }
            }

            [JsonIgnore]
            public decimal Amount
            {
                get
                {
                    var raw = (AmountRaw ?? string.Empty).Replace("\u20A6", string.Empty).Trim();

                    decimal value;
                    return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
                        ? value
                        : 0m;
                }
            }

            /// <summary>Patient name when the feed supplies one, service otherwise.</summary>
            public string PrimaryLine
            {
                get
                {
                    if (!string.IsNullOrWhiteSpace(PayerId)) return PayerId.Trim();
                    return string.IsNullOrWhiteSpace(ServiceName) ? "Payment" : ServiceName.Trim();
                }
            }

            public string SecondaryLine
            {
                get
                {
                    var time = HasValidDate
                        ? (DateRecorded.Date == DateTime.Now.Date
                            ? DateRecorded.ToString("h:mm tt", CultureInfo.InvariantCulture)
                            : DateRecorded.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture))
                        : (DateRecordedRaw ?? string.Empty).Trim();

                    var service = string.IsNullOrWhiteSpace(ServiceName) ? null : ServiceName.Trim();

                    if (service == null) return time;
                    return time.Length == 0 ? service : service + " · " + time;
                }
            }

            public string FormattedAmount
            {
                get { return "₦" + Math.Abs(Amount).ToString("N0", CultureInfo.InvariantCulture); }
            }

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

            /// <summary>
            /// This feed carries no status field, so a returned row is a settled
            /// row. If the API starts sending one, it is honoured.
            /// </summary>
            private bool IsApproved
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(Status)) return true;

                    var s = Status.Trim();
                    return s.IndexOf("approve", StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("paid", StringComparison.OrdinalIgnoreCase) >= 0
                        || s.Equals("00", StringComparison.OrdinalIgnoreCase);
                }
            }

            public string StatusDisplay
            {
                get { return IsApproved ? "Paid" : Status.Trim(); }
            }

            public Color StatusColor
            {
                get { return IsApproved ? Color.FromHex("#0F6E56") : Color.FromHex("#854F0B"); }
            }

            public Color BadgeFill
            {
                get { return IsApproved ? Color.FromHex("#E1F5EE") : Color.FromHex("#FAEEDA"); }
            }

            public Color BadgeTextColor
            {
                get { return IsApproved ? Color.FromHex("#0F6E56") : Color.FromHex("#854F0B"); }
            }
        }

        /// <summary>
        /// Mirrors the pattern already used by History.TransactionDataContext so
        /// the dashboard stops poking labels through Device.BeginInvokeOnMainThread.
        /// </summary>
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