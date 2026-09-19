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

        private double _lastWidth = -1;
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
                PasswordToggleButton.Text = isVisible ? "Hide" : "Show";
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





        private async void HandleSuccessfulLogin(LoginResult result)
        {
            var agent = result.LoginResponse.agent;

            ValidUserMail = agent.email ?? EmailEntry.Text.Trim();
            Passwords = agent.password;
            Name = agent.name;
            category = agent.category;
            Pin = agent.pin;
            Super_Agent = agent.SuperAgent;
            CollectionPoint = agent.collectionPoint;

            var merchantNo = agent.ResolveMerchantNo();
            SessionService.MerchantNo = merchantNo ?? string.Empty;

            if (string.IsNullOrWhiteSpace(merchantNo))
            {
                // Remove this block once CandidateKeys above includes the real key —
                // this line tells you exactly what to add.
                System.Diagnostics.Debug.WriteLine("[Login] No merchant number matched. Agent fields were: "
                    + agent.DescribeAvailableFields());
            }

            string cp = agent.collectionPoint ?? string.Empty;
            string cat = agent.category ?? string.Empty;

            string resolvedCode = "DEFAULT";
            string resolvedDisplayName = "Yobe State Specialist Hospital";

            // Auto-select hospital based on CollectionPoint response
            if (cp.IndexOf("Potiskum", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cp.IndexOf("Portiskum", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                resolvedCode = "POTISKUM";
                resolvedDisplayName = "State Specialist Hospital Potiskum";
            }
            else if (cp.IndexOf("Damagum", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                resolvedCode = "DAMAGUM";
                resolvedDisplayName = "General Hospital Damagum";
            }
            else if (cp.IndexOf("Specialist", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                resolvedCode = "DEFAULT";
                resolvedDisplayName = "Yobe State Specialist Hospital";
            }

            await HospitalContext.SelectAsync(resolvedCode, resolvedDisplayName);
            await SessionService.SaveAsync(agent.name, agent.email, agent.category, agent.collectionPoint,
                resolvedCode, resolvedDisplayName, merchantNo);

            Device.BeginInvokeOnMainThread(() =>
            {
                Application.Current.MainPage = new NavigationPage(new Views.Dashboard());
            });
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

        // Nothing in the login response is currently mapped to a merchant/wallet
        // number, and /ProcessPatientBill requires one. Rather than guess a JSON
        // key name and bind to nothing silently, this captures every field the
        // server actually sends. TryResolveMerchantNo below checks it against the
        // handful of plausible names; if none match, ResolveMerchantNo logs the
        // full set of keys the server sent so you can see the real one and add it
        // to CandidateKeys.
        [Newtonsoft.Json.JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken> ExtraFields { get; set; }

        private static readonly string[] CandidateKeys =
        {
            "merchantNo", "merchantNumber", "MerchantNo", "merchant_no",
            "walletMerchantNo", "walletNo"
        };

        /// <summary>
        /// Returns the merchant number if the response used a name we already
        /// know about; otherwise null (never throws, never guesses).
        /// </summary>
        public string ResolveMerchantNo()
        {
            if (ExtraFields == null) return null;

            foreach (var key in CandidateKeys)
            {
                if (ExtraFields.TryGetValue(key, out var token) && token != null)
                {
                    var value = token.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
            return null;
        }

        /// <summary>Every field name the server actually sent, for a one-time debug check.</summary>
        public string DescribeAvailableFields()
        {
            if (ExtraFields == null || ExtraFields.Count == 0) return "(no extra fields on agent object)";
            return string.Join(", ", ExtraFields.Keys);
        }
    }

    internal class LoginResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public LoginResponse LoginResponse { get; set; }
    }
    #endregion
}