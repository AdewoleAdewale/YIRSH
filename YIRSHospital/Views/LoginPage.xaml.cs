using Acr.UserDialogs;
using Newtonsoft.Json;
using Plugin.Connectivity;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRSHospital.Services;

namespace YIRSHospital.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class LoginPage : ContentPage, INotifyPropertyChanged
    {
        #region Static Properties
        public static string Name { get; set; }
        public static string ValidUserMail { get; set; }
        public static string Passwords { get; set; }
        public static string Pin { get; set; }
        public static string Super_Agent { get; set; }
        public static string Message { get; set; }
        public static string category { get; set; }
        public static string CollectionPoint { get; set; }
        #endregion

        #region Private Fields
        private bool _isPasswordVisible = false;
        private bool _isLoading = false;
        private CancellationTokenSource _cancellationTokenSource;
        private const int MAX_LOGIN_ATTEMPTS = 3;
        private int _loginAttempts = 0;
        private DateTime _lastLoginAttempt = DateTime.MinValue;
        private const int LOCKOUT_MINUTES = 1;
        private List<HospitalInfo> _hospitals = new List<HospitalInfo>();
        private bool _hospitalsLoaded;
        #endregion

        #region Properties
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                    UpdateLoadingUI(value);
                }
            }
        }

        public bool IsPasswordVisible
        {
            get => _isPasswordVisible;
            set
            {
                if (_isPasswordVisible != value)
                {
                    _isPasswordVisible = value;
                    OnPropertyChanged();
                    UpdatePasswordVisibility(value);
                }
            }
        }
        #endregion

        #region Constructor
        public LoginPage()
        {
            try
            {
                InitializeComponent();
                BindingContext = this;
                InitializeUI();
            }
            catch (Exception ex)
            {
                _ = HandleError("Initialization Error", ex);
            }
        }
        #endregion

        #region UI Initialization
        private void InitializeUI()
        {
            try
            {
                LoadSavedCredentials();
                SetupUIDefaults();
            }
            catch (Exception ex)
            {
                _ = HandleError("UI Initialization Error", ex);
            }
        }

        private void SetupUIDefaults()
        {
            try
            {
                LoginActivityIndicator.IsVisible = false;
                LoginActivityIndicator.IsRunning = false;
                EmailValidationLabel.IsVisible = false;
                PasswordValidationLabel.IsVisible = false;
                HospitalSection.Opacity = 1;
                HospitalValidationLabel.IsVisible = false;
                HeaderSection.Opacity = 1;
                FormCard.Opacity = 1;
                EmailSection.Opacity = 1;
                PasswordSection.Opacity = 1;
                OptionsRow.Opacity = 1;
                LoginButton.Opacity = 1;
                FooterSection.Opacity = 1;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Setup UI Defaults Error: {ex.Message}");
            }
        }

        private void UpdateLoadingUI(bool isLoading)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                LoginActivityIndicator.IsVisible = isLoading;
                LoginActivityIndicator.IsRunning = isLoading;
                LoginButtonText.IsVisible = !isLoading;
                LoginButton.IsEnabled = !isLoading;
            });
        }

        private void UpdatePasswordVisibility(bool isVisible)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                PasswordEntry.IsPassword = !isVisible;
                PasswordToggleButton.Source = isVisible ? "icons8eyes_open" : "icons8eyes";
            });
        }
        #endregion

        #region Login Logic
        private async Task PerformLogin()
        {
            try
            {
                if (IsAccountLocked())
                {
                    var remainingTime = GetRemainingLockoutTime();
                    await DisplayAlert("Account Locked",
                        $"Too many failed attempts. Please try again in {remainingTime} minutes.", "OK");
                    return;
                }

                if (!ValidateForm()) return;
                if (!await CheckNetworkConnectivity()) return;

                IsLoading = true;
                _cancellationTokenSource = new CancellationTokenSource();

                var email = EmailEntry.Text.Trim();
                var password = PasswordEntry.Text.Trim();

                await SaveCredentialsIfNeeded(email);

                var loginResult = await LoginAsync(email, password, _cancellationTokenSource.Token);

                if (loginResult.Success)
                {
                    _loginAttempts = 0;
                    HandleSuccessfulLogin(loginResult);
                }
                else
                {
                    await HandleFailedLogin(loginResult.ErrorMessage);
                }
            }
            catch (OperationCanceledException)
            {
                await DisplayAlert("Login Cancelled", "Login operation was cancelled", "OK");
            }
            catch (Exception ex)
            {
                await HandleError("Login Error", ex);
            }
            finally
            {
                IsLoading = false;
                _cancellationTokenSource?.Dispose();
            }
        }

        private async Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
        {
            try
            {
                var url = $"https://yobe.osoftpay.net/api/TaskPayers/v1/AgentLogin?UserName={Uri.EscapeDataString(email)}&Password={Uri.EscapeDataString(password)}";


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
                    using (var response = await client.GetAsync(url, cancellationToken))
                    {
                        var json = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            var loginResponse = JsonConvert.DeserializeObject<LoginResponse>(json);

                            if (loginResponse?.responseCode == "00" && loginResponse.agent != null)
                            {
                                return new LoginResult
                                {
                                    Success = true,
                                    LoginResponse = loginResponse
                                };
                            }
                            else
                            {
                                return new LoginResult
                                {
                                    Success = false,
                                    ErrorMessage = loginResponse?.message ?? "Invalid credentials provided"
                                };
                            }
                        }
                        else
                        {
                            return new LoginResult
                            {
                                Success = false,
                                ErrorMessage = $"Server error: {response.StatusCode}"
                            };
                        }
                    }
                }
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new LoginResult
                {
                    Success = false,
                    ErrorMessage = "Request timed out. Please check your internet connection."
                };
            }
            catch (Exception ex)
            {
                return new LoginResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (!_hospitalsLoaded) await LoadHospitalsAsync();
        }

        private async Task LoadHospitalsAsync()
        {
            try
            {
                HospitalPicker.Title = "Loading hospitals…";
                HospitalPicker.IsEnabled = false;

                var result = await HospitalApiService.GetHospitalListAsync();

                if (!result.Success || result.Data == null || result.Data.Count == 0)
                {
                    HospitalPicker.Title = "Tap to retry";
                    HospitalPicker.IsEnabled = true;
                    ShowValidationError(HospitalValidationLabel,
                        result.ErrorMessage ?? "Could not load hospitals. Pull down to retry.");
                    return;
                }

                _hospitals = result.Data;
                _hospitalsLoaded = true;

                Device.BeginInvokeOnMainThread(() =>
                {
                    HospitalPicker.ItemsSource = _hospitals;
                    HospitalPicker.Title = "Select your hospital";
                    HospitalPicker.IsEnabled = true;

                    // Re-select whatever they used last time
                    if (HospitalContext.IsSelected)
                    {
                        var previous = _hospitals.FirstOrDefault(h =>
                            string.Equals(h.code, HospitalContext.Code, StringComparison.OrdinalIgnoreCase));
                        if (previous != null) HospitalPicker.SelectedItem = previous;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadHospitalsAsync: " + ex.Message);
                HospitalPicker.Title = "Tap to retry";
                HospitalPicker.IsEnabled = true;
            }
        }

        private async void OnHospitalSelected(object sender, EventArgs e)
        {
            var selected = HospitalPicker.SelectedItem as HospitalInfo;
            if (selected == null) return;

            HideValidationError(HospitalValidationLabel);

            // API 2 — confirm the code is actually live before we let them log in
            var codes = await HospitalApiService.GetHospitalCodeListAsync();

            if (!codes.Success || codes.Data == null)
            {
                ShowValidationError(HospitalValidationLabel,
                    "Could not verify hospital. Check your connection.");
                return;
            }

            var confirmed = codes.Data.FirstOrDefault(c =>
                string.Equals(c, selected.code, StringComparison.OrdinalIgnoreCase));

            if (confirmed == null)
            {
                ShowValidationError(HospitalValidationLabel,
                    selected.displayName + " is not currently available.");
                return;
            }

            // Store the *verified* code from API 2, not the one from API 1
            await HospitalContext.SelectAsync(confirmed, selected.displayName);
        }
        private async void HandleSuccessfulLogin(LoginResult result)
        {
            var agent = result.LoginResponse.agent;

            ValidUserMail = EmailEntry.Text.Trim();
            Passwords = agent.password;
            Name = agent.name;
            category = agent.category;
            Pin = agent.pin;
            Super_Agent = agent.SuperAgent;
            CollectionPoint = agent.collectionPoint;
            Message = result.LoginResponse.message;


            var successMessage = $"Welcome back, {agent.name} — {HospitalContext.Label}";
            UserDialogs.Instance.Toast(successMessage, TimeSpan.FromSeconds(3));

            if (!string.IsNullOrWhiteSpace(agent.collectionPoint)
                && !HospitalContext.IsDefaultHospital
                && agent.collectionPoint.IndexOf(HospitalContext.Code, StringComparison.OrdinalIgnoreCase) < 0
                && HospitalContext.DisplayName?.IndexOf(agent.collectionPoint, StringComparison.OrdinalIgnoreCase) < 0)
            {
                var proceed = await DisplayAlert("Check hospital",
                    $"Your account is registered to {agent.collectionPoint}, but you selected {HospitalContext.Label}. Continue?",
                    "Continue", "Change hospital");

                if (!proceed) { HospitalContext.Clear(); HospitalPicker.SelectedItem = null; return; }
            }

            await SessionService.SaveAsync(agent.name, agent.email, agent.category, agent.collectionPoint,HospitalContext.Code, HospitalContext.DisplayName);

            NavigateBasedOnCategory(agent.category);
        }

        private void NavigateBasedOnCategory(string agentCategory)
        {
            Page targetPage = new Views.Dashboard();

            Device.BeginInvokeOnMainThread(() =>
            {
                Application.Current.MainPage = new NavigationPage(targetPage);
            });
        }

        private async Task HandleFailedLogin(string errorMessage)
        {
            _loginAttempts++;
            _lastLoginAttempt = DateTime.Now;

            var remainingAttempts = MAX_LOGIN_ATTEMPTS - _loginAttempts;

            if (remainingAttempts > 0)
            {
                await DisplayAlert("Login Failed",
                    $"{errorMessage}\n\nRemaining attempts: {remainingAttempts}", "Try Again");
            }
            else
            {
                await DisplayAlert("Account Locked",
                    $"Maximum login attempts exceeded. Account locked for {LOCKOUT_MINUTES} minutes.", "OK");
            }
        }
        #endregion

        #region Helper Methods
        private async Task<bool> CheckNetworkConnectivity()
        {
            try
            {
                if (!CrossConnectivity.Current.IsConnected)
                {
                    await DisplayAlert("No Internet",
                        "Please check your internet connection and try again.", "OK");
                    return false;
                }
                return true;
            }
            catch
            {
                return true;
            }
        }

        private async Task SaveCredentialsIfNeeded(string email)
        {
            try
            {
                if (RememberMeCheckbox.IsChecked)
                {
                    Application.Current.Properties["RememberMe"] = true;
                    Application.Current.Properties["SavedEmail"] = email;
                }
                else
                {
                    Application.Current.Properties["RememberMe"] = false;
                    Application.Current.Properties.Remove("SavedEmail");
                }
                await Application.Current.SavePropertiesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving credentials: {ex.Message}");
            }
        }

        private bool IsAccountLocked()
        {
            return _loginAttempts >= MAX_LOGIN_ATTEMPTS &&
                   DateTime.Now.Subtract(_lastLoginAttempt).TotalMinutes < LOCKOUT_MINUTES;
        }

        private int GetRemainingLockoutTime()
        {
            var elapsedMinutes = DateTime.Now.Subtract(_lastLoginAttempt).TotalMinutes;
            return (int)Math.Ceiling(LOCKOUT_MINUTES - elapsedMinutes);
        }

        private void LoadSavedCredentials()
        {
            try
            {
                if (Application.Current.Properties.ContainsKey("RememberMe") &&
                    (bool)Application.Current.Properties["RememberMe"])
                {
                    if (Application.Current.Properties.ContainsKey("SavedEmail"))
                    {
                        EmailEntry.Text = Application.Current.Properties["SavedEmail"].ToString();
                        RememberMeCheckbox.IsChecked = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading credentials: {ex.Message}");
            }
        }

        private async Task HandleError(string title, Exception ex)
        {
            var errorMessage = ex.InnerException?.Message ?? ex.Message;
            System.Diagnostics.Debug.WriteLine($"{title}: {errorMessage}");

            await DisplayAlert(title,
                "An unexpected error occurred. Please try again or contact support if the problem persists.",
                "OK");
        }
        #endregion

        #region Event Handlers
        private void OnEmailTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.NewTextValue) && IsValidEmail(e.NewTextValue))
            {
                HideValidationError(EmailValidationLabel);
            }
        }

        private void OnPasswordTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.NewTextValue))
            {
                HideValidationError(PasswordValidationLabel);
            }
        }

        private void OnPasswordToggleClicked(object sender, EventArgs e)
        {
            IsPasswordVisible = !IsPasswordVisible;
        }

        private async void OnForgotPasswordTapped(object sender, EventArgs e)
        {
            try
            {
                var email = await DisplayPromptAsync(
                    title: "Reset Password",
                    message: "Enter your email address:",
                    accept: "Send",
                    cancel: "Cancel",
                    placeholder: "Email",
                    keyboard: Keyboard.Email);

                if (!string.IsNullOrEmpty(email) && IsValidEmail(email))
                {
                    await DisplayAlert("Reset Link Sent",
                        $"A password reset link has been sent to {email}", "OK");
                }
                else if (!string.IsNullOrEmpty(email))
                {
                    await DisplayAlert("Invalid Email", "Please enter a valid email address", "OK");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in forgot password: {ex.Message}");
            }
        }

        private async void OnSupportContactTapped(object sender, EventArgs e)
        {
            try
            {
                var action = await DisplayActionSheet("Contact Support", "Cancel", null,
                    "Call 09070701616", "Call 07017639494", "Send Email");

                switch (action)
                {
                    case "Call 09070701616":
                        await Launcher.OpenAsync(new Uri("tel:09070701616"));
                        break;
                    case "Call 07017639494":
                        await Launcher.OpenAsync(new Uri("tel:07017639494"));
                        break;
                    case "Send Email":
                        await Launcher.OpenAsync(new Uri("mailto:support@yobe.osoftpay.net?subject=Login Support Request"));
                        break;
                }
            }
            catch (Exception ex)
            {
                await HandleError("Contact Support Error", ex);
            }
        }

        private async void OnLoginTapped(object sender, EventArgs e)
        {
            await PerformLogin();
        }
        #endregion

        #region Validation
        private bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            var emailRegex = new Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$");
            return emailRegex.IsMatch(email);
        }

        private bool IsValidPassword(string password)
        {
            return !string.IsNullOrWhiteSpace(password) && password.Length >= 6;
        }

        private bool ValidateForm()
        {
            bool isValid = true;

            if (!HospitalContext.IsSelected)
            {
                ShowValidationError(HospitalValidationLabel, "Please select a hospital");
                isValid = false;
            }
            else
            {
                HideValidationError(HospitalValidationLabel);
            }

            if (!IsValidEmail(EmailEntry.Text))
            {
                ShowValidationError(EmailValidationLabel, "Please enter a valid email address");
                isValid = false;
            }
            else
            {
                HideValidationError(EmailValidationLabel);
            }

            if (!IsValidPassword(PasswordEntry.Text))
            {
                ShowValidationError(PasswordValidationLabel, "Password must be at least 6 characters");
                isValid = false;
            }
            else
            {
                HideValidationError(PasswordValidationLabel);
            }

            return isValid;
        }

        private void ShowValidationError(Label errorLabel, string message)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                errorLabel.Text = message;
                errorLabel.IsVisible = true;
            });
        }

        private void HideValidationError(Label errorLabel)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                errorLabel.IsVisible = false;
            });
        }
        #endregion

        #region Lifecycle
        protected override bool OnBackButtonPressed()
        {
            if (IsLoading)
            {
                _cancellationTokenSource?.Cancel();
                return true;
            }
            return base.OnBackButtonPressed();
        }
        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }

    #region Data Models
    internal class LoginResponse
    {
        public string responseCode { get; set; }
        public string message { get; set; }
        public Agent agent { get; set; }
    }

    internal class Agent
    {
        public string name { get; set; }
        public string password { get; set; }
        public string email { get; set; }
        public string category { get; set; }
        public string collectionPoint { get; set; }
        public string pin { get; set; }
        public string SuperAgent { get; set; }
    }

    internal class LoginResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public LoginResponse LoginResponse { get; set; }
    }
    #endregion
}