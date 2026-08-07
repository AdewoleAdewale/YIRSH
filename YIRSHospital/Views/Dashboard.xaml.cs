using Android.Bluetooth;
using Java.Util;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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

        #region Private Fields
        private CancellationTokenSource _cancellationTokenSource;
        private CancellationTokenSource _animationCancellationToken;
        private readonly HttpClient _httpClient;
        private bool _isInitialized = false;
        private readonly System.Threading.Timer _clockTimer;
        private int _retryCount = 0;
        private const int MAX_RETRY_COUNT = 3;
        private const int STAGGER_DELAY = 100;
        private const int ANIMATION_DURATION = 600;
        private const int QUICK_ANIMATION = 200;
        private const int FLOATING_ANIMATION = 800;
        #endregion

        public class RecentTransaction
        {
            public string TransactionId { get; set; }
            public string ServiceName { get; set; }
            public decimal Amount { get; set; }
            public DateTime DateRecorded { get; set; }
            public string TransactionType { get; set; }
            public string Status { get; set; }

            public string FormattedAmount => Amount >= 0 ? $"+₦{Amount:N2}" : $"-₦{Math.Abs(Amount):N2}";
            public string FormattedDate => DateRecorded.ToString("MMM dd, h:mm tt");
            public string AmountColor => Amount >= 0 ? "#00C851" : "#FF4444";
            public string TypeIcon => Amount >= 0 ? "▼" : "▲";
        }

        public Dashboard()
        {
            try
            {
                InitializeComponent();
                InitializeData();

                _httpClient = new HttpClient();
                _httpClient.Timeout = TimeSpan.FromSeconds(30);

                _animationCancellationToken = new CancellationTokenSource();
                var token = _animationCancellationToken.Token;

                _isInitialized = true;

                FloatingButtonsContainer.Opacity = 0;
                FloatingButtonsContainer.TranslationX = 100;
                FloatingButtonsContainer.IsVisible = true;

                Device.StartTimer(TimeSpan.FromMilliseconds(1000), () =>
                {
                    Device.BeginInvokeOnMainThread(async () =>
                    {
                        try
                        {
                            if (!token.IsCancellationRequested && FloatingButtonsContainer.IsVisible)
                            {
                                await AnimateFloatingButtons(token);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Floating button animation error: {ex.Message}");
                        }
                    });
                    return false;
                });

                // Load recent transactions
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await LoadRecentTransactions();
                });
            }
            catch (Exception ex)
            {
                HandleException(ex, "Constructor initialization failed");
            }
        }

        private async Task LoadRecentTransactions()
        {
            try
            {
                var endDate = DateTime.Now;
                var startDate = endDate.AddDays(-2);

                string searchStringFrom = startDate.ToString("MM/dd/yyyy");
                string searchStringTo = endDate.ToString("MM/dd/yyyy");

                string url = $"https://yobe.osoftpay.net/api/TaskPayers/gettransaction?Email={Uri.EscapeDataString(LoginPage.ValidUserMail)}&SearchFrom={Uri.EscapeDataString(searchStringFrom)}&SearchTo={Uri.EscapeDataString(searchStringTo)}";

                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                    {
                        // Accept all certificates (adjust for production)
                        return true;
                    },
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls11
                };

                using (HttpClient client = new HttpClient(handler))
                {

                    var response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var allTransactions = JsonConvert.DeserializeObject<List<RecentTransaction>>(json);

                        if (allTransactions != null && allTransactions.Any())
                        {
                            var recentTransactions = allTransactions.OrderByDescending(t => t.DateRecorded).Take(10).ToList();

                            Device.BeginInvokeOnMainThread(() =>
                            {
                                RecentTransactionsListView.ItemsSource = recentTransactions;
                                RecentTransactionsSection.IsVisible = true;
                                TransactionCountLabel.Text = $"{recentTransactions.Count} Recent";
                            });
                        }
                        else
                        {
                            Device.BeginInvokeOnMainThread(() =>
                            {
                                RecentTransactionsSection.IsVisible = false;
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading recent transactions: {ex.Message}");
                Device.BeginInvokeOnMainThread(() =>
                {
                    RecentTransactionsSection.IsVisible = false;
                });
            }
        }

        private async Task AnimateFloatingButtons(CancellationToken token)
        {
            try
            {
                if (FloatingButtonsContainer == null || token.IsCancellationRequested)
                    return;

                await Task.WhenAll(
                    FloatingButtonsContainer.FadeTo(1, FLOATING_ANIMATION, Easing.CubicOut),
                    FloatingButtonsContainer.TranslateTo(0, 0, FLOATING_ANIMATION, Easing.BounceOut)
                );

                if (!token.IsCancellationRequested)
                {
                    _ = Task.Run(async () => await StartFloatingButtonBreathingAnimation(token));
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("Floating button animation cancelled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Floating button animation error: {ex.Message}");
            }
        }

        private async Task StartFloatingButtonBreathingAnimation(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (PrintFloatingButton == null || LogoutFloatingButton == null)
                        break;

                    await Device.InvokeOnMainThreadAsync(async () =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            await Task.WhenAll(
                                PrintFloatingButton.ScaleTo(1.1, 2000, Easing.SinInOut),
                                LogoutFloatingButton.ScaleTo(1.1, 2200, Easing.SinInOut)
                            );
                        }
                    });

                    if (token.IsCancellationRequested) break;

                    await Device.InvokeOnMainThreadAsync(async () =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            await Task.WhenAll(
                                PrintFloatingButton.ScaleTo(1.0, 2000, Easing.SinInOut),
                                LogoutFloatingButton.ScaleTo(1.0, 2200, Easing.SinInOut)
                            );
                        }
                    });

                    if (token.IsCancellationRequested) break;

                    await Task.Delay(3000, token);
                }
            }
            catch (OperationCanceledException)
            {
                try
                {
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        if (PrintFloatingButton != null) PrintFloatingButton.Scale = 1.0;
                        if (LogoutFloatingButton != null) LogoutFloatingButton.Scale = 1.0;
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error restoring button scale: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Breathing animation error: {ex.Message}");
            }
        }

        private void CleanupResources()
        {
            try
            {
                _animationCancellationToken?.Cancel();
                _animationCancellationToken?.Dispose();
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _clockTimer?.Dispose();
                _httpClient?.Dispose();

                Connectivity.ConnectivityChanged -= OnConnectivityChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");
            }
        }

        #region Initialization
        private void InitializeData()
        {
            try
            {
                WelcomeMessage.Text = !string.IsNullOrEmpty(LoginPage.Name)
                    ? LoginPage.Name
                    : "Guest User";

                collectionP.Text = !string.IsNullOrEmpty(LoginPage.CollectionPoint)
                    ? LoginPage.CollectionPoint
                    : "TestPoint";

                AgentName.Text = !string.IsNullOrEmpty(LoginPage.Super_Agent)
                    ? LoginPage.Super_Agent
                    : "TestAgent";
                UserGreeting.Text = GetGreeting();

                Connectivity.ConnectivityChanged += OnConnectivityChanged;
                UpdateConnectivityStatus();
            }
            catch (Exception ex)
            {
                HandleException(ex, "Data initialization failed");
            }
        }

        private string GetGreeting()
        {
            var hour = DateTime.Now.Hour;
            if (hour < 12) return "Good Morning";
            if (hour < 17) return "Good Afternoon";
            return "Good Evening";
        }


        #endregion

        #region Connectivity Management
        private void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            UpdateConnectivityStatus();
        }

        private void UpdateConnectivityStatus()
        {
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    var current = Connectivity.NetworkAccess;
                });
            }
            catch (Exception ex)
            {
                HandleException(ex, "Connectivity status update failed");
            }
        }
        #endregion

        #region Navigation Methods
        private async void ServiceList_Tapped(object sender, EventArgs e)
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                await SafeNavigateAsync(() => Navigation.PushAsync(new Views.UnifiedPatientWorkflow()));
            }, "Loading Workflow...");
        }

        private async void ChangePin_Tapped(object sender, EventArgs e)
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                await SafeNavigateAsync(() => Navigation.PushModalAsync(new Views.ChangePin()));
            }, "Loading...");
        }

        private async void ChangePassword_Tapped(object sender, EventArgs e)
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                await SafeNavigateAsync(() => Navigation.PushModalAsync(new Views.ChangePassword()));
            }, "Loading...");
        }

        private async void Settings_Tapped(object sender, EventArgs e)
        {
            try
            {
                await ShowSettingsMenuAsync();
            }
            catch (Exception ex)
            {
                HandleException(ex, "Settings menu failed");
            }
        }

        private async void Logout_Tapped(object sender, EventArgs e)
        {

            try
            {
                bool confirmed = await DisplayAlert(
                    "Logout Confirmation", "Are you sure you want to logout?", "Yes", "No");

                if (confirmed)
                {
                    SessionService.Clear();
                    Preferences.Remove("IsLoggedIn");
                    Preferences.Remove("UserToken");
                    App.Current.Logout();
                    Xamarin.Forms.Application.Current.MainPage =
                        new Xamarin.Forms.NavigationPage(new LoginPage());
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"An error occurred: {ex.Message}", "OK");
            }
        }

        private async void ViewAllTransactions_Clicked(object sender, EventArgs e)
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                await SafeNavigateAsync(() => Navigation.PushAsync(new Views.History()));
            }, "Loading transaction history...");
        }
        #endregion

        #region Settings Menu
        private async Task ShowSettingsMenuAsync()
        {
            try
            {
                string action = await DisplayActionSheet(
                    "SETTINGS",
                    "Cancel",
                    null,
                     "Change Pin",
                      "Change Password",
                    "App Settings",
                    "Help & Support");

                await HandleSettingsActionAsync(action);
            }
            catch (Exception ex)
            {
                HandleException(ex, "Settings menu display failed");
            }
        }

        private async Task HandleSettingsActionAsync(string action)
        {
            try
            {
                switch (action)
                {
                    case "App Settings":
                        await ShowAppSettingsAsync();
                        break;
                    case "Help & Support":
                        await ShowHelpSupportAsync();
                        break;
                    case "Change Pin":
                        await ShowChangePinAsync();
                        break;
                    case "Change Password":
                        await ShowChangePasswordAsync();
                        break;
                }
            }
            catch (Exception ex)
            {
                HandleException(ex, $"Settings action '{action}' failed");
            }
        }

        private async Task ShowChangePasswordAsync()
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                await SafeNavigateAsync(() => Navigation.PushModalAsync(new Views.ChangePassword()));
            }, "Loading...");
        }

        private async Task ShowChangePinAsync()
        {
            await ExecuteWithLoadingAsync(async () =>
            {
                await SafeNavigateAsync(() => Navigation.PushModalAsync(new Views.ChangePin()));
            }, "Loading...");
        }

        private async Task ShowAppSettingsAsync()
        {
            await DisplayAlert("App Settings",
                "Version: 2.0.0\nBuild: 2025.01\nLast Update: Today", "OK");
        }

        private async Task ShowHelpSupportAsync()
        {
            var action = await DisplayActionSheet("Help & Support", "Cancel", null,
                "Contact Support", "View FAQ", "Report Issue");

            switch (action)
            {
                case "Contact Support":
                    await DisplayAlert("Contact Support",
                        "Email: support@osoftpay.com\nPhone: +234-XXX-XXXX", "OK");
                    break;

            }
        }
        #endregion

        #region Printer Functionality
        private async Task TestPrinterWithRetryAsync()
        {
            _retryCount = 0;
            await ExecuteWithLoadingAsync(async () =>
            {
                await TestPrinterAsync();
            }, "Testing printer connection...");
        }

        private async Task TestPrinterAsync()
        {
            try
            {
                await PrintTestReceiptAsync();
            }
            catch (PrinterException pex)
            {
                // Friendly retry loop for known printer problems
                bool retry = await DisplayAlert(
                    "Printer Error",
                    pex.Message + "\n\nWould you like to retry?",
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

            DisplyAlert("Success", "Test print completed successfully!", "OK");
        }

        private void DisplyAlert(string v1, string v2, string v3)
        {
            throw new NotImplementedException();
        }

        // ── Tap handler (wired up in XAML via TapGestureRecognizer) ──────────────

        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
        {
            await TestPrinterWithRetryAsync();
        }


        private List<string> GetSupportedPrinters()
        {
            return new List<string>
            {
                "MPT-II", "printer001", "RPP02N", "RPP210", "InnerPrinter",
                "b906", "ANDROID BT", "FP8800", "IposPrinter", "CS10",
                "MTP-II_89EB", "MP300", "MTP-II-6111", "Internal Bluetooth Printer"
            };
        }

        private void ValidateBluetoothAdapter(BluetoothAdapter adapter)
        {
            if (adapter == null)
                throw new BluetoothException("No Bluetooth adapter found");

            if (!adapter.IsEnabled)
                throw new BluetoothException("Bluetooth is not enabled");
        }

        private BluetoothDevice FindBluetoothPrinter(BluetoothAdapter adapter, List<string> printers)
        {
            return adapter.BondedDevices?.FirstOrDefault(device =>
                printers.Any(printer =>
                    string.Equals(device.Name, printer, StringComparison.OrdinalIgnoreCase)));
        }

        private async Task ShowPrinterNotFoundDialog()
        {
            var retry = await DisplayAlert("Printer Not Found",
                "No compatible Bluetooth printer found. Would you like to retry?",
                "Retry", "Cancel");

            if (retry && _retryCount < MAX_RETRY_COUNT)
            {
                _retryCount++;
                await Task.Delay(1000);
                await TestPrinterAsync();
            }
        }

        private async Task ConnectAndPrintAsync(BluetoothDevice device, string printText)
        {
            using (var socket = device.CreateRfcommSocketToServiceRecord(
                UUID.FromString("00001101-0000-1000-8000-00805f9b34fb")))
            {
                _cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                await socket.ConnectAsync();

                if (socket.IsConnected)
                {
                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(printText);
                    await socket.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    await Task.Delay(2000, _cancellationTokenSource.Token);
                    socket.Close();

                    await DisplayAlert("Success", "Test print completed successfully!", "OK");
                }
                else
                {
                    throw new BluetoothException("Failed to connect to printer");
                }
            }
        }

        private async Task HandleBluetoothExceptionAsync(BluetoothException bex)
        {
            var retry = await DisplayAlert("Bluetooth Error",
                $"{bex.Message}\n\nWould you like to retry?", "Retry", "Cancel");

            if (retry && _retryCount < MAX_RETRY_COUNT)
            {
                _retryCount++;
                await Task.Delay(2000);
                await TestPrinterAsync();
            }
        }

        private const string ESC_ALIGN_CENTER = "\x1B\x61\x01";
        private const string ESC_ALIGN_LEFT = "\x1B\x61\x00";
        private const string ESC_BOLD_ON = "\x1B\x21\x08";
        private const string ESC_BOLD_OFF = "\x1B\x21\x00";
        #endregion

        #region Utility Methods
        private async Task ExecuteWithLoadingAsync(Func<Task> action, string loadingMessage = "Loading...")
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
                    await DisplayAlert("Connection Error",
                        "Please check your internet connection.", "OK");
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
                var current = Connectivity.NetworkAccess;
                return current == NetworkAccess.Internet;
            }
            catch
            {
                return false;
            }
        }

        private void ShowLoading(string message)
        {
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    if (LoadingOverlay != null && LoadingText != null)
                    {
                        LoadingText.Text = message;
                        LoadingOverlay.IsVisible = true;
                        LoadingOverlay.InputTransparent = false;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Show loading error: {ex.Message}");
            }
        }

        private void HideLoading()
        {
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    if (LoadingOverlay != null)
                    {
                        LoadingOverlay.IsVisible = false;
                        LoadingOverlay.InputTransparent = true;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hide loading error: {ex.Message}");
            }
        }



        private void HandleException(Exception ex, string context)
        {
            try
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    HideLoading();

                    var errorMessage = GetUserFriendlyErrorMessage(ex);
                    await DisplayAlert("Error", $"{context}\n\n{errorMessage}", "OK");

                    System.Diagnostics.Debug.WriteLine($"Error in {context}: {ex}");
                });
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"Critical error in {context}: {ex}");
            }
        }

        private string GetUserFriendlyErrorMessage(Exception ex)
        {
            switch (ex)
            {
                case BluetoothException _:
                    return "Bluetooth connection issue. Please check your printer.";
                case TimeoutException _:
                    return "Operation timed out. Please try again.";
                case UnauthorizedAccessException _:
                    return "Access denied. Please check permissions.";
                case System.Net.NetworkInformation.NetworkInformationException _:
                    return "Network connection issue. Please check your internet.";
                default:
                    return "An unexpected error occurred. Please try again.";
            }
        }
        #endregion

        #region Page Lifecycle
        protected override void OnAppearing()
        {
            try
            {
                base.OnAppearing();
                _ = ConfirmHospitalAsync();
                if (_isInitialized)
                {
                    UpdateConnectivityStatus();
                }
            }
            catch (Exception ex)
            {
                HandleException(ex, "Page load failed");
            }
        }

        protected override void OnDisappearing()
        {
            try
            {
                base.OnDisappearing();
                _cancellationTokenSource?.Cancel();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Page cleanup error: {ex.Message}");
            }
        }

        protected override bool OnBackButtonPressed()
        {
            try
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    var result = await DisplayAlert("Exit App",
                        "Are you sure you want to exit the application?", "Yes", "No");

                    if (result)
                    {
                        CleanupResources();
                        System.Diagnostics.Process.GetCurrentProcess().CloseMainWindow();
                    }
                });
                return true;
            }
            catch (Exception ex)
            {
                HandleException(ex, "Back button handling failed");
                return base.OnBackButtonPressed();
            }
        }
        #endregion

        #region Custom Exceptions
        public class BluetoothException : Exception
        {
            public BluetoothException(string message) : base(message) { }
            public BluetoothException(string message, Exception innerException) : base(message, innerException) { }
        }
        #endregion


        private async void TapGestureRecognizer_Tapped(object sender, EventArgs e) => await Navigation.PushAsync(new PatientTransaction());


        private void TapGestureRecognizer_Tapped_2(object sender, EventArgs e)
        {
            Navigation.PushAsync(new Views.UnifiedPatientWorkflow());
        }

        private async Task ConfirmHospitalAsync()
        {
            if (!HospitalContext.IsSelected) return;

            var info = await HospitalApiService.GetHospitalInfoAsync(HospitalContext.Code);

            if (info.Success && info.Data != null)
            {
                await HospitalContext.SelectAsync(info.Data.code, info.Data.displayName);
                Device.BeginInvokeOnMainThread(() => collectionP.Text = HospitalContext.Label);
            }
            else
            {
                // Stale or withdrawn hospital — don't let them transact against it
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Hospital unavailable",
                        info.ErrorMessage ?? "Could not confirm your hospital. Please log in again.", "OK");
                    App.Current.Logout();
                });
            }
        }
    }
}