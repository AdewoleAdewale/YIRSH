using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
    public partial class UnifiedPatientWorkflow : ContentPage
    {
        // ─────────────────────────────────────────────────────────
        //  NESTED MODELS  (merged from both source files)
        // ─────────────────────────────────────────────────────────

        #region Models

        public class Department : INotifyPropertyChanged
        {
            private string _name;
            private int _id;
            public string name { get => _name; set { _name = value; OnPropertyChanged(); } }
            public int id { get => _id; set { _id = value; OnPropertyChanged(); } }
            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged([CallerMemberName] string p = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }

        public class ServiceItem : INotifyPropertyChanged
        {
            private string _serviceName;
            private decimal _amount;
            private bool _isSelected;
            private int _quantity = 1;
            private string _departmentName;
            private bool _initialAmountWasZero;

            public string serviceName { get => _serviceName; set { _serviceName = value; OnPropertyChanged(); } }

            public decimal amount
            {
                get => _amount;
                set
                {
                    _amount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedAmount));
                    OnPropertyChanged(nameof(SubTotal));
                    OnPropertyChanged(nameof(AmountInputText));
                }
            }

            public bool IsSelected
            {
                get => _isSelected;
                set { _isSelected = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTotal)); }
            }
            public int Quantity { get => _quantity; set { _quantity = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTotal)); } }
            public string DepartmentName { get => _departmentName; set { _departmentName = value; OnPropertyChanged(); } }
            public string FormattedAmount => $"₦{amount:N2}";
            public decimal SubTotal => IsSelected ? amount * Quantity : 0;

            // ── NEW: DRF zero-amount manual entry support ──────────────────────────

            /// Call once right after the service is loaded from the API.
            public void MarkInitialAmount() => _initialAmountWasZero = (_amount == 0);

            /// True only for a service literally named "DRF" that came back with amount = 0.
            public bool RequiresManualAmount =>
                string.Equals(serviceName?.Trim(), "DRF", StringComparison.OrdinalIgnoreCase)
                && _initialAmountWasZero;

            public bool ShowFormattedAmount => !RequiresManualAmount;

            /// Two-way bindable text for the manual amount Entry.
            public string AmountInputText
            {
                get => _amount == 0 ? string.Empty : _amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                set
                {
                    if (decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal val) && val >= 0)
                        amount = val;
                    else if (string.IsNullOrWhiteSpace(value))
                        amount = 0;
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged([CallerMemberName] string p = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
        public class PaymentServiceItem { public string serviceName { get; set; } public int quantity { get; set; } }

        public class PaymentRequest
        {
            public string revName { get; set; }
            public string department { get; set; }
            public string email { get; set; }
            public string hospitalNo { get; set; }
            public string pin { get; set; }
            public string PaymentMethod { get; set; }
            public List<PaymentServiceItem> services { get; set; }
        }

        public class PaymentResponse
        {
            public string respondCode { get; set; }
            public string transactionNo { get; set; }
            public string message { get; set; }
            public string status { get; set; }
            public string payerId { get; set; }
            public decimal totalAmount { get; set; }
            public string PaymentMethod { get; set; }
            public List<BreakdownItem> breakdown { get; set; }
        }

        public class BreakdownItem
        {
            public string serviceName { get; set; }
            public decimal amount { get; set; }
            public int quantity { get; set; }
            public decimal subTotal { get; set; }
        }

        public class PaymentResultData
        {
            public bool IsSuccess { get; set; }
            public string Title { get; set; }
            public string Message { get; set; }
            public decimal TotalAmount { get; set; }
            public List<string> TransactionNumbers { get; set; }
            public List<PaymentResponse> Responses { get; set; }
            public string ErrorDetails { get; set; }
            public string PaymentMethod { get; set; }
        }

        // ── CashConnect models ────────────────────────────────────────────
        private class CashConnectInitResponse { public string Reference { get; set; } public string Status { get; set; } }
        private class CashConnectPollResponse { public bool IsApproved { get; set; } public string Reference { get; set; } public string Message { get; set; } public string Rrn { get; set; } }

        public class HospitalViewModel : INotifyPropertyChanged
        {
            private ObservableCollection<Department> _departments;
            private ObservableCollection<ServiceItem> _allServices;
            private ObservableCollection<ServiceItem> _displayedServices;
            private Department _selectedDepartment;
            private bool _isLoading;
            private string _statusText;
            private string _loadingMessage;

            public HospitalViewModel()
            {
                Departments = new ObservableCollection<Department>();
                AllServices = new ObservableCollection<ServiceItem>();
                DisplayedServices = new ObservableCollection<ServiceItem>();
                StatusText = "Ready";
                LoadingMessage = "Loading...";
            }

            public ObservableCollection<Department> Departments { get => _departments; set { _departments = value; OnPropertyChanged(); } }
            public ObservableCollection<ServiceItem> AllServices { get => _allServices; set { _allServices = value; OnPropertyChanged(); } }
            public ObservableCollection<ServiceItem> DisplayedServices
            {
                get => _displayedServices;
                set { _displayedServices = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasServices)); UpdateCalculations(); }
            }
            public Department SelectedDepartment { get => _selectedDepartment; set { _selectedDepartment = value; OnPropertyChanged(); } }
            public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }
            public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
            public string LoadingMessage { get => _loadingMessage; set { _loadingMessage = value; OnPropertyChanged(); } }

            public bool HasServices => DisplayedServices?.Any() == true;
            public bool HasSelectedServices => AllServices?.Any(s => s.IsSelected) == true;
            public string SelectedServicesCount => $"{AllServices?.Count(s => s.IsSelected) ?? 0} service(s)";
            public string TotalAmount => $"₦{AllServices?.Where(s => s.IsSelected).Sum(s => s.SubTotal):N2}";

            public void UpdateCalculations()
            {
                OnPropertyChanged(nameof(HasServices));
                OnPropertyChanged(nameof(HasSelectedServices));
                OnPropertyChanged(nameof(SelectedServicesCount));
                OnPropertyChanged(nameof(TotalAmount));
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged([CallerMemberName] string p = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }

        #endregion

        // ─────────────────────────────────────────────────────────
        //  FIELDS
        // ─────────────────────────────────────────────────────────

        #region Fields

        private readonly HospitalViewModel _viewModel;
        private HttpClient _httpClient;

        private const string BASE_URL = "https://yobe.osoftpay.net/api/Agents";
        private string REVENUE_NAME => LoginPage.CollectionPoint?.ToString() ?? "Hospital Services";
        private const string BLUETOOTH_UUID = "00001101-0000-1000-8000-00805f9b34fb";

        // CashConnect config
        private const string CASHCONNECT_BASE_URL = "https://api.cashconnect.ng/v1";
        private const string CASHCONNECT_MERCHANT_ID = "YOUR_MERCHANT_ID";
        private const string CASHCONNECT_TERMINAL_ID = "YOUR_TERMINAL_ID";
        private const string CASHCONNECT_API_KEY = "YOUR_API_KEY";

        // Workflow state
        private PaymentResultData _currentPaymentResult;
        private string _selectedPaymentMethod = "Cash";
        private string _registeredPatientId;       // set after successful registration
        private bool _isNewPatientMode = true;      // true = Register first; false = Existing patient
        private CancellationTokenSource _cardPaymentCts;

        #endregion

        // ─────────────────────────────────────────────────────────
        //  CONSTRUCTOR
        // ─────────────────────────────────────────────────────────

        public UnifiedPatientWorkflow()
        {
            try
            {
                InitializeComponent();
                _viewModel = new HospitalViewModel();
                BindingContext = _viewModel;
                _httpClient = CreateHttpClient();
                InitializePage();
            }
            catch (Exception ex)
            {
                HandleCriticalError("Failed to initialise page", ex);
            }
        }

        private async void InitializePage()
        {
            try
            {
                await LoadDepartmentsAndServices();
                // Default: cash method selected
                SetPaymentMethod("Cash");
            }
            catch (Exception ex) { HandleError("Failed to load data", ex); }
        }

        // ─────────────────────────────────────────────────────────
        //  HTTP CLIENT
        // ─────────────────────────────────────────────────────────


        private void EnsureHttpClientInitialized()
        {
            if (_httpClient == null)
                _httpClient = CreateHttpClient();
        }


        private HttpClient CreateHttpClient()
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (m, c, ch, e) => true,
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls11
                };
                return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
            }
            catch
            {
                return new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            }
        }

        // ─────────────────────────────────────────────────────────
        //  MODE SWITCHER  (New Patient / Existing Patient)
        // ─────────────────────────────────────────────────────────

        private void OnSelectNewPatientMode(object sender, EventArgs e) => SetWorkflowMode(newPatient: true);
        private void OnSelectExistingPatientMode(object sender, EventArgs e) => SetWorkflowMode(newPatient: false);

        private void SetWorkflowMode(bool newPatient)
        {
            _isNewPatientMode = newPatient;
            Device.BeginInvokeOnMainThread(() =>
            {
                SectionRegistration.IsVisible = newPatient;
                SectionExistingPatient.IsVisible = !newPatient;

                // Mode button highlights
                ModeBtnNewPatient.BackgroundColor = newPatient ? Color.White : Color.Transparent;
                ModeBtnExistingPatient.BackgroundColor = !newPatient ? Color.White : Color.Transparent;

                var activeLabel = newPatient
                    ? (Label)ModeBtnNewPatient.Content
                    : (Label)ModeBtnExistingPatient.Content;
                var inactiveLabel = newPatient
                    ? (Label)ModeBtnExistingPatient.Content
                    : (Label)ModeBtnNewPatient.Content;

                if (activeLabel != null) activeLabel.TextColor = Color.FromHex("#004225");
                if (inactiveLabel != null) inactiveLabel.TextColor = Color.FromHex("#FFFFFFCC");

                if (!newPatient)
                {
                    // Existing-patient mode: services/payment already unlocked IF an ID is pre-filled
                    UpdateStepperToStep(1);
                }
                else
                {
                    // Reset back to step 1
                    UpdateStepperToStep(1);
                    LockSection(SectionServices, "Complete registration to unlock");
                    LockSection(SectionPayment, "Select services to unlock payment", hide: true);
                }
            });
        }

        // ─────────────────────────────────────────────────────────
        //  STEPPER HELPERS
        // ─────────────────────────────────────────────────────────

        private void UpdateStepperToStep(int activeStep)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                // Step 1
                ApplyStepStyle(Step1Badge, Step1Icon, activeStep, 1);
                // Step 2
                ApplyStepStyle(Step2Badge, Step2Icon, activeStep, 2);
                // Step 3
                ApplyStepStyle(Step3Badge, Step3Icon, activeStep, 3);
            });
        }

        private void ApplyStepStyle(Xamarin.Forms.PancakeView.PancakeView badge, Label icon, int active, int step)
        {
            if (step < active)
            {
                badge.BackgroundColor = Color.FromHex("#10B981");
                icon.Text = "✓";
            }
            else if (step == active)
            {
                badge.BackgroundColor = Color.FromHex("#004225");
                icon.Text = step.ToString();
            }
            else
            {
                badge.BackgroundColor = Color.FromHex("#CBD5E0");
                icon.Text = step.ToString();
            }
        }

        private void UnlockSection(Xamarin.Forms.PancakeView.PancakeView section, string subtitle,
            Xamarin.Forms.PancakeView.PancakeView statusBadge = null,
            Label statusLabel = null, string badgeText = "Ready", bool show = false)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                section.Opacity = 1.0;
                section.IsEnabled = true;
                if (show) section.IsVisible = true;

                if (statusBadge != null)
                    statusBadge.BackgroundColor = Color.FromHex("#D1FAE5");
                if (statusLabel != null)
                {
                    statusLabel.Text = badgeText;
                    statusLabel.TextColor = Color.FromHex("#065F46");
                }

                // Update subtitle if accessible
                try
                {
                    if (section == SectionServices)
                        ServicesSectionSubtitle.Text = subtitle;
                    else if (section == SectionPayment)
                        PaymentSectionSubtitle.Text = subtitle;
                }
                catch { }
            });
        }

        private void LockSection(Xamarin.Forms.PancakeView.PancakeView section, string reason, bool hide = false)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                section.Opacity = 0.45;
                section.IsEnabled = false;
                if (hide) section.IsVisible = false;
            });
        }

        // ─────────────────────────────────────────────────────────
        //  SECTION 1A: PATIENT REGISTRATION
        // ─────────────────────────────────────────────────────────

        #region Registration

        private async void OnRegisterPatient(object sender, EventArgs e)
        {
            var (isValid, error) = ValidateRegistrationForm();
            if (!isValid)
            {
                await DisplayAlert("Validation", error, "OK");
                return;
            }

            try
            {
                _viewModel.IsLoading = true;
                _viewModel.LoadingMessage = "Registering patient…";

                var data = new PatientRegistrationObject
                {
                    FullName = RegFullName.Text?.Trim(),
                    PatentNo = RegPatientNo.Text?.Trim(),
                    PhoneNumber = RegPhoneNumber.Text?.Trim(),
                    Address = RegAddress.Text?.Trim(),
                    Gender = RegGenderPicker.SelectedItem?.ToString(),
                    Age = RegAge.Text?.Trim(),
                    MaritalStatus = RegMaritalPicker.SelectedItem?.ToString()
                };

                var response = await SubmitRegistration(data);

                _viewModel.IsLoading = false;

                if (response != null && response.Code == "00")
                {
                    _registeredPatientId = response.PatientId;
                    ShowRegistrationSuccessPopup(response);
                }
                else
                {
                    await DisplayAlert("Registration Failed", response?.Message ?? "Please try again.", "OK");
                }
            }
            catch (HttpRequestException ex)
            {
                _viewModel.IsLoading = false;
                await DisplayAlert("Network Error", "Check your connection and try again.", "OK");
                Debug.WriteLine($"[Register] Network: {ex.Message}");
            }
            catch (Exception ex)
            {
                _viewModel.IsLoading = false;
                HandleError("Registration error", ex);
            }
        }

        private (bool isValid, string error) ValidateRegistrationForm()
        {
            if (string.IsNullOrWhiteSpace(RegFullName?.Text) || RegFullName.Text.Trim().Length < 3)
                return (false, "Full Name must be at least 3 characters.");
            if (string.IsNullOrWhiteSpace(RegPatientNo?.Text))
                return (false, "Patient Number is required.");
            if (string.IsNullOrWhiteSpace(RegPhoneNumber?.Text) ||
                !System.Text.RegularExpressions.Regex.IsMatch(RegPhoneNumber.Text, @"^\d{10,11}$"))
                return (false, "Phone number must be 10–11 digits.");
            if (string.IsNullOrWhiteSpace(RegAddress?.Text) || RegAddress.Text.Trim().Length < 3)
                return (false, "Address is required.");
            if (RegGenderPicker.SelectedIndex < 0)
                return (false, "Please select a gender.");
            if (string.IsNullOrWhiteSpace(RegAge?.Text) ||
                !int.TryParse(RegAge.Text, out int age) || age < 1 || age > 150)
                return (false, "Enter a valid age between 1 and 150.");
            if (RegMaritalPicker.SelectedIndex < 0)
                return (false, "Please select marital status.");
            return (true, string.Empty);
        }

        private async Task<PatientRegistrationResponseObject> SubmitRegistration(PatientRegistrationObject data)
        {
            EnsureHttpClientInitialized();
            const int maxRetry = 3;
            const int delayMs = 2000;

            for (int attempt = 1; attempt <= maxRetry; attempt++)
            {
                try
                {
                    var formData = new List<KeyValuePair<string, string>>
                    {

                        new KeyValuePair<string, string>("FullName",       data.FullName ?? ""),
                        new KeyValuePair<string, string>("PatentNo",       data.PatentNo ?? ""),
                        new KeyValuePair<string, string>("PhoneNumber",    data.PhoneNumber ?? ""),
                        new KeyValuePair<string, string>("Address",        data.Address ?? ""),
                        new KeyValuePair<string, string>("Gender",         data.Gender ?? ""),
                        new KeyValuePair<string, string>("Age",            data.Age ?? ""),
                        new KeyValuePair<string, string>("MaritalStatus",  data.MaritalStatus ?? ""),
                        new KeyValuePair<string, string>("AgentName",      LoginPage.Name ?? "")

                    };

                    var content = new FormUrlEncodedContent(formData);
                    var response = await _httpClient.PostAsync($"{BASE_URL}/RegisterPatient", content);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        return JsonConvert.DeserializeObject<PatientRegistrationResponseObject>(json);
                    }
                }
                catch (Exception ex) when (attempt < maxRetry)
                {
                    Debug.WriteLine($"[Register] Attempt {attempt} failed: {ex.Message}");
                    await Task.Delay(delayMs);
                }
            }
            throw new Exception("Registration failed after multiple attempts.");
        }

        private void ShowRegistrationSuccessPopup(PatientRegistrationResponseObject response)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                RegSuccessPatientId.Text = response.PatientId ?? "N/A";
                CopyStatusLabel.Text = "Tap to copy";
                CopyStatusIcon.Text = "📋";

                // Auto-copy on show
                TryCopyToClipboard(response.PatientId);

                RegSuccessPopup.IsVisible = true;

                // Update reg status badge
                RegStatusBadge.BackgroundColor = Color.FromHex("#D1FAE5");
                RegStatusLabel.Text = "Done ✓";
                RegStatusLabel.TextColor = Color.FromHex("#065F46");
            });
        }

        private async void OnCopyPatientId(object sender, EventArgs e)
        {
            TryCopyToClipboard(RegSuccessPatientId.Text);
            CopyStatusIcon.Text = "✓";
            CopyStatusLabel.Text = "Copied!";
            await Task.Delay(2000);
            CopyStatusIcon.Text = "📋";
            CopyStatusLabel.Text = "Tap to copy";
        }

        private void TryCopyToClipboard(string text)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(text))
                    Clipboard.SetTextAsync(text);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Clipboard] {ex.Message}");
            }
        }

        private void OnRegSuccessProceedToServices(object sender, EventArgs e)
        {
            RegSuccessPopup.IsVisible = false;
            ActivateServicesSection();
        }

        private void OnRegisterAnother(object sender, EventArgs e)
        {
            RegSuccessPopup.IsVisible = false;
            ClearRegistrationForm();
            // Reset steps
            UpdateStepperToStep(1);
            LockSection(SectionServices, "Complete registration to unlock");
            LockSection(SectionPayment, "Select services to unlock payment", hide: true);
        }

        private void ClearRegistrationForm()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                RegFullName.Text = string.Empty;
                RegPatientNo.Text = string.Empty;
                RegPhoneNumber.Text = string.Empty;
                RegAddress.Text = string.Empty;
                RegGenderPicker.SelectedIndex = -1;
                RegAge.Text = string.Empty;
                RegMaritalPicker.SelectedIndex = -1;
                RegStatusBadge.BackgroundColor = Color.FromHex("#FEF3C7");
                RegStatusLabel.Text = "Pending";
                RegStatusLabel.TextColor = Color.FromHex("#92400E");
            });
        }

        #endregion

        // ─────────────────────────────────────────────────────────
        //  SECTION 1B: EXISTING PATIENT LOOKUP
        // ─────────────────────────────────────────────────────────

        private async void OnLookupExistingPatient(object sender, EventArgs e)
        {
            string patientId = ExistingPatientIdEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(patientId))
            {
                await DisplayAlert("Patient ID Required", "Please enter a patient ID.", "OK");
                return;
            }

            _registeredPatientId = patientId;

            Device.BeginInvokeOnMainThread(() =>
            {
                ExistingPatientName.Text = $"Patient ID: {patientId}";
                ExistingPatientDetails.Text = "Proceeding to service selection…";
                ExistingPatientInfoCard.IsVisible = true;
            });

            // Pre-fill payment patient ID field
            if (PatientNo != null)
                PatientNo.Text = patientId;

            ActivateServicesSection();
        }

        // ─────────────────────────────────────────────────────────
        //  SECTION TRANSITIONS
        // ─────────────────────────────────────────────────────────

        private void ActivateServicesSection()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                UnlockSection(SectionServices, "Select the services required",
                    ServicesStatusBadge, ServicesStatusLabel, "Active ✓");
                UpdateStepperToStep(2);

                // Scroll to services section
                // (best-effort — works on most platforms)
                try
                {
                    var scrollView = (ScrollView)((Grid)Content).Children
                        .OfType<ScrollView>().First();
                    scrollView.ScrollToAsync(SectionServices, ScrollToPosition.Start, animated: true);
                }
                catch { }
            });
        }

        private void ActivatePaymentSection(List<ServiceItem> selectedServices)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                // Build services summary
                PaymentServicesContainer.Children.Clear();
                foreach (var g in selectedServices.GroupBy(s => s.DepartmentName))
                {
                    PaymentServicesContainer.Children.Add(new Label
                    {
                        Text = $"📋 {g.Key}",
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromHex("#004225"),
                        Margin = new Thickness(0, 8, 0, 4)
                    });

                    foreach (var svc in g)
                    {
                        var frame = new Xamarin.Forms.PancakeView.PancakeView
                        {
                            BackgroundColor = Color.FromHex("#F7FAFC"),
                            BorderColor = Color.FromHex("#E2E8F0"),
                            BorderThickness = 1,
                            CornerRadius = 10,
                            Padding = new Thickness(14),
                            Margin = new Thickness(0, 4)
                        };
                        var grid = new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitionCollection
                            {
                                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                                new ColumnDefinition { Width = GridLength.Auto }
                            }
                        };
                        grid.Children.Add(new Label
                        {
                            Text = svc.serviceName,
                            FontSize = 13,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromHex("#1A202C")
                        });
                        Grid.SetColumn(grid.Children.Last(), 0);

                        grid.Children.Add(new Label
                        {
                            Text = $"₦{svc.SubTotal:N2}",
                            FontSize = 14,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromHex("#FF6B35"),
                            VerticalOptions = LayoutOptions.Center
                        });
                        Grid.SetColumn(grid.Children.Last(), 1);

                        frame.Content = grid;
                        PaymentServicesContainer.Children.Add(frame);
                    }
                }

                decimal total = selectedServices.Sum(s => s.SubTotal);
                PaymentTotalLabel.Text = $"₦{total:N2}";
                PaymentPatientIdLabel.Text = _registeredPatientId ?? "—";

                // Pre-fill patient number
                if (!string.IsNullOrWhiteSpace(_registeredPatientId) && PatientNo != null)
                    PatientNo.Text = _registeredPatientId;

                UnlockSection(SectionPayment, "Complete payment below",
                    PaymentStatusBadge, PaymentStatusLabel, "Active ✓", show: true);
                UpdateStepperToStep(3);
            });
        }

        // ─────────────────────────────────────────────────────────
        //  API: LOAD DEPARTMENTS & SERVICES
        // ─────────────────────────────────────────────────────────

        #region API Calls

        private async Task LoadDepartmentsAndServices()
        {
            EnsureHttpClientInitialized();
            try
            {
                _viewModel.IsLoading = true;
                _viewModel.LoadingMessage = "Loading services…";

                var deptResponse = await _httpClient.GetAsync($"{BASE_URL}/ListDepartment");
                if (!deptResponse.IsSuccessStatusCode)
                    throw new HttpRequestException($"Server returned {deptResponse.StatusCode}");

                var deptJson = await deptResponse.Content.ReadAsStringAsync();
                var departments = JsonConvert.DeserializeObject<List<Department>>(deptJson);

                if (departments == null || !departments.Any())
                    throw new InvalidOperationException("No departments found");

                Device.BeginInvokeOnMainThread(() =>
                {
                    _viewModel.Departments.Clear();
                    foreach (var d in departments.OrderBy(x => x.name))
                        _viewModel.Departments.Add(d);
                });

                var allServices = new List<ServiceItem>();

                foreach (var dept in departments)
                {
                    try
                    {
                        string url = $"{BASE_URL}/ListRevServices?RevHead={Uri.EscapeDataString(REVENUE_NAME)}&Dept={Uri.EscapeDataString(dept.name)}";
                        var res = await _httpClient.GetAsync(url);
                        if (res.IsSuccessStatusCode)
                        {
                            var json = await res.Content.ReadAsStringAsync();
                            var services = JsonConvert.DeserializeObject<List<ServiceItem>>(json);
                            if (services?.Any() == true)
                            {
                                foreach (var s in services)
                                {
                                    s.DepartmentName = dept.name;
                                    s.Quantity = 1;
                                    s.IsSelected = false;
                                    s.MarkInitialAmount();          // ← ADD THIS LINE
                                    s.PropertyChanged += OnServiceItemPropertyChanged;
                                    allServices.Add(s);
                                }
                            }
                        }
                        await Task.Delay(80);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Services] {dept.name}: {ex.Message}");
                    }
                }

                Device.BeginInvokeOnMainThread(() =>
                {
                    _viewModel.AllServices.Clear();
                    _viewModel.DisplayedServices.Clear();
                    foreach (var s in allServices.OrderBy(x => x.DepartmentName).ThenBy(x => x.serviceName))
                    {
                        _viewModel.AllServices.Add(s);
                        _viewModel.DisplayedServices.Add(s);
                    }
                    _viewModel.StatusText = $"{departments.Count} departments, {allServices.Count} services";
                    _viewModel.UpdateCalculations();
                });
            }
            catch (Exception ex)
            {
                HandleError("Failed to load services", ex);
            }
            finally
            {
                _viewModel.IsLoading = false;
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────
        //  SERVICES EVENT HANDLERS
        // ─────────────────────────────────────────────────────────

        private void OnDepartmentChanged(object sender, EventArgs e)
        {
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    _viewModel.DisplayedServices.Clear();
                    var q = _viewModel.AllServices.AsEnumerable();
                    if (_viewModel.SelectedDepartment != null && !string.IsNullOrWhiteSpace(_viewModel.SelectedDepartment.name))
                        q = q.Where(s => s.DepartmentName == _viewModel.SelectedDepartment.name);
                    foreach (var s in q.OrderBy(x => x.DepartmentName).ThenBy(x => x.serviceName))
                        _viewModel.DisplayedServices.Add(s);
                    _viewModel.UpdateCalculations();
                });
            }
            catch (Exception ex) { HandleError("Filter error", ex); }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string q = e.NewTextValue?.Trim().ToLower() ?? string.Empty;
                Device.BeginInvokeOnMainThread(() =>
                {
                    _viewModel.DisplayedServices.Clear();
                    var query = _viewModel.AllServices.AsEnumerable();
                    if (_viewModel.SelectedDepartment?.name != null)
                        query = query.Where(s => s.DepartmentName == _viewModel.SelectedDepartment.name);
                    if (!string.IsNullOrWhiteSpace(q))
                        query = query.Where(s =>
                            (s.serviceName?.ToLower().Contains(q) ?? false) ||
                            (s.DepartmentName?.ToLower().Contains(q) ?? false));
                    foreach (var s in query.OrderBy(x => x.DepartmentName).ThenBy(x => x.serviceName))
                        _viewModel.DisplayedServices.Add(s);
                    _viewModel.UpdateCalculations();
                });
            }
            catch (Exception ex) { HandleError("Search error", ex); }
        }



        private void OnServiceSelectionChanged(object sender, CheckedChangedEventArgs e)
            => _viewModel?.UpdateCalculations();

        private void OnQuantityChanged(object sender, EventArgs e)
            => _viewModel?.UpdateCalculations();

    

        private void OnServiceItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ServiceItem.IsSelected)
                || e.PropertyName == nameof(ServiceItem.Quantity)
                || e.PropertyName == nameof(ServiceItem.amount))
                _viewModel?.UpdateCalculations();
        }

        // ─────────────────────────────────────────────────────────
        //  PROCEED TO PAYMENT
        // ─────────────────────────────────────────────────────────

        private async void OnProceedToPayment(object sender, EventArgs e)
        {
            var selected = _viewModel.AllServices?.Where(s => s.IsSelected).ToList();
            if (selected == null || !selected.Any())
            {
                await DisplayAlert("Selection Required", "Please select at least one service.", "OK");
                return;
            }

            // NEW: block proceeding if DRF needs a manual amount and it's still 0
            var missingDrfAmount = selected.FirstOrDefault(s => s.RequiresManualAmount && s.amount <= 0);
            if (missingDrfAmount != null)
            {
                await DisplayAlert("Amount Required", "Please enter an amount for DRF before proceeding.", "OK");
                return;
            }

            decimal total = selected.Sum(s => s.SubTotal);
            // Amount validation
            if (!ValidatePaymentAmount(total, out string amountError))
            {
                ShowAmountWarning(amountError);
                return;
            }

            HideAmountWarning();
            ActivatePaymentSection(selected);
        }

        private bool ValidatePaymentAmount(decimal amount, out string error)
        {
            error = null;
            if (amount <= 0)
            {
                error = "No service amount detected. Please select a service with a valid amount.";
                return false;
            }
            if (amount < 100)
            {
                error = $"Amount ₦{amount:N2} is below the minimum. Total must be at least ₦100 before payment can proceed.";
                return false;
            }
            return true;
        }

        private void ShowAmountWarning(string message)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                AmountWarningLabel.Text = $"⚠  {message}";
                AmountWarningCard.IsVisible = true;
            });
        }

        private void HideAmountWarning()
        {
            Device.BeginInvokeOnMainThread(() => AmountWarningCard.IsVisible = false);
        }

        // ─────────────────────────────────────────────────────────
        //  PAYMENT METHOD SELECTION
        // ─────────────────────────────────────────────────────────

        private void OnSelectCash(object sender, EventArgs e) => SetPaymentMethod("Cash");
        private void OnSelectTransfer(object sender, EventArgs e) => SetPaymentMethod("Transfer");
        private void OnSelectCard(object sender, EventArgs e) => SetPaymentMethod("Card");

        private void SetPaymentMethod(string method)
        {
            _selectedPaymentMethod = method;
            Device.BeginInvokeOnMainThread(() =>
            {
                void Deselect(Xamarin.Forms.PancakeView.PancakeView card, Label lbl)
                {
                    card.BackgroundColor = Color.White;
                    card.BorderColor = Color.FromHex("#E2E8F0");
                    lbl.TextColor = Color.FromHex("#718096");
                }
                void Select(Xamarin.Forms.PancakeView.PancakeView card, Label lbl)
                {
                    card.BackgroundColor = Color.FromHex("#004225");
                    card.BorderColor = Color.FromHex("#004225");
                    lbl.TextColor = Color.White;
                }

                Deselect(CashMethodCard, CashMethodLabel);
                Deselect(TransferMethodCard, TransferMethodLabel);
                Deselect(CardMethodCard, CardMethodLabel);

                switch (method)
                {
                    case "Cash":
                        Select(CashMethodCard, CashMethodLabel);
                        SelectedMethodBadge.Text = "✔  Cash selected";
                        ProcessButtonLabel.Text = "PROCESS PAYMENT";
                        break;
                    case "Transfer":
                        Select(TransferMethodCard, TransferMethodLabel);
                        SelectedMethodBadge.Text = "✔  Transfer selected";
                        ProcessButtonLabel.Text = "PROCESS PAYMENT";
                        break;
                    case "Card":
                        Select(CardMethodCard, CardMethodLabel);
                        SelectedMethodBadge.Text = "✔  Card Payment selected";
                        ProcessButtonLabel.Text = "CHARGE CARD";
                        break;
                }
            });
        }

        // ─────────────────────────────────────────────────────────
        //  PROCESS PAYMENT
        // ─────────────────────────────────────────────────────────

        private async void OnProcessPayment(object sender, EventArgs e)
        {
            // PIN validation
            if (PaymentPinEntry == null || string.IsNullOrWhiteSpace(PaymentPinEntry.Text))
            {
                await DisplayAlert("PIN Required", "Please enter your 4-digit agent PIN.", "OK");
                return;
            }
            if (PaymentPinEntry.Text.Length != 4 || !PaymentPinEntry.Text.All(char.IsDigit))
            {
                await DisplayAlert("Invalid PIN", "PIN must be exactly 4 digits.", "OK");
                return;
            }

            // Amount re-validation
            var selected = _viewModel.AllServices?.Where(s => s.IsSelected).ToList();
            decimal total = selected?.Sum(s => s.SubTotal) ?? 0;
            if (!ValidatePaymentAmount(total, out string amountError))
            {
                ShowAmountWarning(amountError);
                return;
            }

            // Card flow
            if (_selectedPaymentMethod == "Card")
            {
                await InitiateCardPayment();
                return;
            }

            // Cash / Transfer flow
            try
            {
                _viewModel.IsLoading = true;
                _viewModel.LoadingMessage = "Processing payment…";

                var selectedServices = _viewModel.AllServices?.Where(s => s.IsSelected).ToList();
                if (selectedServices == null || !selectedServices.Any())
                    throw new InvalidOperationException("No services selected");

                string userEmail = LoginPage.ValidUserMail;
                if (string.IsNullOrWhiteSpace(userEmail))
                    throw new InvalidOperationException("User email not found. Please log in again.");

                var allResponses = new List<PaymentResponse>();
                var errors = new List<string>();

                foreach (var deptGroup in selectedServices.GroupBy(s => s.DepartmentName))
                {
                    try
                    {
                        var req = new PaymentRequest
                        {
                            revName = REVENUE_NAME,
                            department = deptGroup.Key,
                            email = userEmail,
                            pin = PaymentPinEntry.Text,
                            hospitalNo = PatientNo?.Text ?? _registeredPatientId ?? "",
                            PaymentMethod = _selectedPaymentMethod,
                            services = deptGroup.Select(s => new PaymentServiceItem
                            {
                                serviceName = s.serviceName,
                                quantity = s.Quantity
                            }).ToList()
                        };

                        var res = await ProcessPaymentRequest(req);
                        if (res?.respondCode == "00")
                            allResponses.Add(res);
                        else
                            errors.Add($"{deptGroup.Key}: {res?.message ?? "Payment failed"}");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{deptGroup.Key}: {ex.Message}");
                    }
                }

                FinalisePaymentResult(allResponses, errors, _selectedPaymentMethod);
            }
            catch (InvalidOperationException ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
            catch (Exception ex)
            {
                FinalisePaymentResult(new List<PaymentResponse>(), new List<string> { ex.Message }, _selectedPaymentMethod);
            }
            finally
            {
                _viewModel.IsLoading = false;
            }
        }

        private async Task<PaymentResponse> ProcessPaymentRequest(PaymentRequest request)
        {
            EnsureHttpClientInitialized();
            if (request == null) throw new ArgumentNullException(nameof(request));

            string url = $"{BASE_URL}/ProcessPayment";
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            Debug.WriteLine($"[Payment Request] {json}");

            var response = await _httpClient.PostAsync(url, content);
            var responseJson = await response.Content.ReadAsStringAsync();

            Debug.WriteLine($"[Payment Response] {responseJson}");

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Payment failed: {response.StatusCode}");

            var result = JsonConvert.DeserializeObject<PaymentResponse>(responseJson);
            return result ?? throw new InvalidOperationException("Failed to parse payment response");
        }

        // ─────────────────────────────────────────────────────────
        //  FINALISE & SHOW RESULT
        // ─────────────────────────────────────────────────────────

        private void FinalisePaymentResult(List<PaymentResponse> responses, List<string> errors, string method)
        {
            var result = new PaymentResultData
            {
                IsSuccess = responses.Any(),
                Responses = responses,
                TransactionNumbers = responses.Select(r => r.transactionNo).ToList(),
                TotalAmount = responses.Sum(r => r.totalAmount),
                ErrorDetails = errors.Any() ? string.Join("\n", errors) : null,
                PaymentMethod = method
            };

            if (responses.Any() && !errors.Any())
            {
                result.Title = "Payment Successful! ✓";
                result.Message = $"Processed via {method}";
            }
            else if (responses.Any())
            {
                result.Title = "Partial Success ⚠";
                result.Message = $"{responses.Count} succeeded, {errors.Count} failed";
            }
            else
            {
                result.Title = "Payment Failed ✗";
                result.Message = "All attempts failed";
            }

            _currentPaymentResult = result;
            ShowPaymentResultPopup(result);
        }

        private void ShowPaymentResultPopup(PaymentResultData result)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                if (result.IsSuccess)
                {
                    ResultIconLabel.Text = result.ErrorDetails == null ? "✓" : "⚠";
                    ResultIconLabel.TextColor = result.ErrorDetails == null
                        ? Color.FromHex("#10B981") : Color.FromHex("#F59E0B");
                    ResultIconFrame.BackgroundColor = result.ErrorDetails == null
                        ? Color.FromHex("#D1FAE5") : Color.FromHex("#FEF3C7");
                }
                else
                {
                    ResultIconLabel.Text = "✗";
                    ResultIconLabel.TextColor = Color.FromHex("#EF4444");
                    ResultIconFrame.BackgroundColor = Color.FromHex("#FEE2E2");
                }

                ResultTitleLabel.Text = result.Title;
                ResultMessageLabel.Text = result.Message;
                ResultDetailsContainer.Children.Clear();

                if (result.IsSuccess && result.Responses?.Any() == true)
                {
                    foreach (var resp in result.Responses)
                    {
                        var frame = new Xamarin.Forms.PancakeView.PancakeView
                        {
                            BackgroundColor = Color.FromHex("#F0F9FF"),
                            BorderColor = Color.FromHex("#BAE6FD"),
                            BorderThickness = 1,
                            CornerRadius = 10,
                            Padding = new Thickness(16),
                            Margin = new Thickness(0, 6)
                        };

                        var stack = new StackLayout { Spacing = 6 };
                        stack.Children.Add(new Label { Text = $"Ref: {resp.transactionNo ?? "N/A"}", FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#1A202C") });
                        stack.Children.Add(new Label { Text = $"Amount: ₦{resp.totalAmount:N2}", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#10B981") });

                        if (resp.breakdown?.Any() == true)
                        {
                            foreach (var item in resp.breakdown)
                                stack.Children.Add(new Label { Text = $"  · {item.serviceName} ×{item.quantity} = ₦{item.subTotal:N2}", FontSize = 12, TextColor = Color.FromHex("#475569") });
                        }

                        frame.Content = stack;
                        ResultDetailsContainer.Children.Add(frame);
                    }

                    if (result.Responses.Count > 1)
                    {
                        var totalFrame = new Xamarin.Forms.PancakeView.PancakeView
                        {
                            BackgroundGradientStartColor = Color.FromHex("#004225"),
                            BackgroundGradientEndColor = Color.FromHex("#006B3C"),
                            BackgroundGradientAngle = 90,
                            CornerRadius = 10,
                            Padding = new Thickness(16),
                            Margin = new Thickness(0, 8)
                        };
                        var totalStack = new StackLayout { Orientation = StackOrientation.Horizontal };
                        totalStack.Children.Add(new Label { Text = "Grand Total:", TextColor = Color.White, FontSize = 15, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.StartAndExpand });
                        totalStack.Children.Add(new Label { Text = $"₦{result.TotalAmount:N2}", TextColor = Color.White, FontSize = 18, FontAttributes = FontAttributes.Bold });
                        totalFrame.Content = totalStack;
                        ResultDetailsContainer.Children.Add(totalFrame);
                    }
                }

                if (!string.IsNullOrWhiteSpace(result.ErrorDetails))
                {
                    var errFrame = new Xamarin.Forms.PancakeView.PancakeView
                    {
                        BackgroundColor = Color.FromHex("#FEF2F2"),
                        BorderColor = Color.FromHex("#FCA5A5"),
                        BorderThickness = 1,
                        CornerRadius = 10,
                        Padding = new Thickness(14),
                        Margin = new Thickness(0, 8)
                    };
                    errFrame.Content = new Label { Text = $"⚠ {result.ErrorDetails}", FontSize = 12, TextColor = Color.FromHex("#DC2626"), LineBreakMode = LineBreakMode.WordWrap };
                    ResultDetailsContainer.Children.Add(errFrame);
                }

                ResultPrintButton.IsVisible = result.IsSuccess;
                ResultGoBackButtonText.Text = result.IsSuccess ? "NEW TRANSACTION" : "TRY AGAIN";
                PaymentResultPopup.IsVisible = true;

                // Mark payment step done
                if (result.IsSuccess)
                    PaymentStatusBadge.BackgroundColor = Color.FromHex("#D1FAE5");
            });
        }

        private void OnClosePaymentResult(object sender, EventArgs e)
        {
            PaymentResultPopup.IsVisible = false;
            if (_currentPaymentResult?.IsSuccess == true)
                ResetWorkflow();
            _currentPaymentResult = null;
        }

        private void ResetWorkflow()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                // Clear services
                foreach (var s in _viewModel.AllServices)
                {
                    s.IsSelected = false;
                    s.Quantity = 1;
                }
                _viewModel.UpdateCalculations();

                // Clear payment fields
                PaymentPinEntry.Text = string.Empty;
                HideAmountWarning();

                // Reset registration
                ClearRegistrationForm();
                _registeredPatientId = null;

                // Reset workflow state
                LockSection(SectionServices, "Complete registration to unlock");
                LockSection(SectionPayment, "Select services to unlock payment", hide: true);
                UpdateStepperToStep(1);
            });
        }

        // ─────────────────────────────────────────────────────────
        //  RECEIPT BUILDING & PRINTING
        // ─────────────────────────────────────────────────────────

        private ReceiptData BuildCombinedPaymentReceiptData(List<PaymentResponse> responses)
        {
            if (responses == null || !responses.Any())
                throw new ArgumentNullException(nameof(responses));

            var items = new List<ReceiptItem>();

            string patientNo = PatientNo?.Text?.Trim() ?? _registeredPatientId ?? "N/A";
            if (!string.IsNullOrWhiteSpace(patientNo))
                items.Add(new ReceiptItem { Description = "Patient ID", SubText = patientNo });

            items.Add(new ReceiptItem { Description = "Payment Method", SubText = _currentPaymentResult?.PaymentMethod ?? _selectedPaymentMethod ?? "N/A" });
            items.Add(new ReceiptItem { Description = "Services", SubText = string.Empty });

            foreach (var response in responses)
            {
                if (response.breakdown?.Any() == true)
                {
                    foreach (var item in response.breakdown)
                    {
                        items.Add(new ReceiptItem
                        {
                            Description = item.serviceName ?? "Service",
                            Amount = item.subTotal,
                            SubText = $"Qty {item.quantity} × ₦{item.amount:N2}"
                        });
                    }
                }
            }

            decimal grandTotal = responses.Sum(r => r.totalAmount);
            string combinedRef = string.Join(", ", responses.Select(r => r.transactionNo).Where(t => !string.IsNullOrWhiteSpace(t)));

            return new ReceiptData
            {
                StoreName = App.RevenueServiceName ?? "YOBE STATE HOSPITALS MANAGEMENT BOARD",
                StorePhone = "Contact: +234-810-046-6363",
                ReceiptBannerText = "PAYMENT RECEIPT",
                ReceiptNumber = string.IsNullOrWhiteSpace(combinedRef) ? "N/A" : combinedRef,
                AgentName = LoginPage.Name,
                CollectionPoint = REVENUE_NAME,
                PrintDate = DateTime.Now,
                Items = items,
                TotalAmount = grandTotal,
                FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY",
                BarcodeLabel = (responses.Count == 1 && !string.IsNullOrWhiteSpace(responses[0].transactionNo))
                    ? $"https://yobe.osoftpay.net/singlecollections/verify?TransactId={Uri.EscapeDataString(responses[0].transactionNo)}"
                    : null
            };
        }

        private async void OnPrintReceipt(object sender, EventArgs e)
        {
            if (_currentPaymentResult == null || !_currentPaymentResult.IsSuccess)
            {
                await DisplayAlert("Error", "No successful transactions to print.", "OK");
                return;
            }

            try
            {
                _viewModel.IsLoading = true;
                _viewModel.LoadingMessage = "Printing receipt…";

                var data = BuildCombinedPaymentReceiptData(_currentPaymentResult.Responses);

                using (var printer = new BluetoothPrinterService(use80mm: false))
                {
                    await printer.PrintReceiptAsync(data, "Logo.png", "YOBE STATE HOSPITAL");
                }

                await DisplayAlert("Print Status", "Receipt printed successfully.", "OK");
            }
            catch (PrinterException pex)
            {
                await DisplayAlert("Printer Error", pex.Message, "OK");
            }
            catch (Exception ex)
            {
                HandleError("Print failed", ex);
            }
            finally
            {
                _viewModel.IsLoading = false;
            }
        }
        // ─────────────────────────────────────────────────────────
        //  CARD PAYMENT FLOW  (CashConnect)
        // ─────────────────────────────────────────────────────────

        private async Task InitiateCardPayment()
        {
            var selected = _viewModel.AllServices?.Where(s => s.IsSelected).ToList();
            if (selected == null || !selected.Any()) return;

            decimal total = selected.Sum(s => s.SubTotal);

            Device.BeginInvokeOnMainThread(() =>
            {
                CardAmountLabel.Text = $"₦{total:N2}";
                CardStatusLabel.Text = "Initialising terminal…";
                CardReferenceStack.IsVisible = false;
                CardActivityIndicator.IsRunning = true;
                CardCancelButton.IsVisible = true;
                CardPaymentOverlay.IsVisible = true;
            });

            _cardPaymentCts = new CancellationTokenSource();

            try
            {
                var init = await InitiateCashConnectTransaction(total, _cardPaymentCts.Token);
                if (init == null)
                {
                    await ShowCardError("Could not reach the CashConnect terminal. Please try again.");
                    return;
                }

                Device.BeginInvokeOnMainThread(() =>
                {
                    CardStatusLabel.Text = "Please tap, insert or swipe card on terminal…";
                    CardReferenceStack.IsVisible = true;
                    CardReferenceLabel.Text = init.Reference;
                });

                var poll = await PollCashConnectTransaction(init.Reference, total, _cardPaymentCts.Token);

                if (poll == null || !poll.IsApproved)
                {
                    await ShowCardError(poll?.Message ?? "Card declined or timed out.");
                    return;
                }

                Device.BeginInvokeOnMainThread(() => CardStatusLabel.Text = "Card approved ✓ — posting payment…");
                await SubmitHospitalPaymentAfterCard(poll.Reference);
            }
            catch (OperationCanceledException)
            {
                await ShowCardError("Transaction cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Card] {ex.Message}");
                await ShowCardError("An unexpected error occurred. Please try again.");
            }
            finally
            {
                _cardPaymentCts?.Dispose();
                _cardPaymentCts = null;
            }
        }

        private async Task<CashConnectInitResponse> InitiateCashConnectTransaction(decimal amount, CancellationToken ct)
        {
            try
            {
                EnsureHttpClientInitialized();
                var body = new
                {
                    merchantId = CASHCONNECT_MERCHANT_ID,
                    terminalId = CASHCONNECT_TERMINAL_ID,
                    amount = (long)(amount * 100),
                    currency = "NGN",
                    reference = $"YOBS-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}"
                };
                var content = new StringContent(JsonConvert.SerializeObject(body), System.Text.Encoding.UTF8, "application/json");
                _httpClient.DefaultRequestHeaders.Remove("x-api-key");
                _httpClient.DefaultRequestHeaders.Add("x-api-key", CASHCONNECT_API_KEY);
                var res = await _httpClient.PostAsync($"{CASHCONNECT_BASE_URL}/transactions/initiate", content, ct);
                if (!res.IsSuccessStatusCode) return null;
                return JsonConvert.DeserializeObject<CashConnectInitResponse>(await res.Content.ReadAsStringAsync());
            }
            catch (Exception ex) { Debug.WriteLine($"[CC Init] {ex.Message}"); return null; }
        }

        private async Task<CashConnectPollResponse> PollCashConnectTransaction(string reference, decimal amount, CancellationToken ct)
        {
            const int max = 24, delay = 5;
            for (int i = 1; i <= max; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var res = await _httpClient.GetAsync($"{CASHCONNECT_BASE_URL}/transactions/status?reference={Uri.EscapeDataString(reference)}", ct);
                    var json = await res.Content.ReadAsStringAsync();
                    if (res.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(json))
                    {
                        var parsed = JsonConvert.DeserializeObject<CashConnectStatusResponse>(json);
                        string status = parsed?.status?.ToLowerInvariant() ?? "";
                        if (status is "approved" || status is "success" || status is "completed")
                            return new CashConnectPollResponse { IsApproved = true, Reference = reference, Message = "Approved", Rrn = parsed?.rrn ?? "" };
                        if (status is "declined" || status is "failed")
                            return new CashConnectPollResponse { IsApproved = false, Reference = reference, Message = parsed?.message ?? "Declined" };
                        Device.BeginInvokeOnMainThread(() =>
                            CardStatusLabel.Text = $"Waiting for card… ({(max - i) * delay}s)\nPresent card on terminal");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Debug.WriteLine($"[CC Poll #{i}] {ex.Message}"); }
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
            return new CashConnectPollResponse { IsApproved = false, Reference = reference, Message = "Transaction timed out." };
        }

        private async Task SubmitHospitalPaymentAfterCard(string cardReference)
        {
            var selected = _viewModel.AllServices?.Where(s => s.IsSelected).ToList();
            if (selected == null || !selected.Any()) return;

            var allResponses = new List<PaymentResponse>();
            var errors = new List<string>();

            foreach (var deptGroup in selected.GroupBy(s => s.DepartmentName))
            {
                try
                {
                    var req = new PaymentRequest
                    {
                        revName = REVENUE_NAME,
                        department = deptGroup.Key,
                        email = LoginPage.ValidUserMail,
                        pin = PaymentPinEntry?.Text ?? "",
                        hospitalNo = PatientNo?.Text ?? _registeredPatientId ?? "",
                        PaymentMethod = "Card",
                        services = deptGroup.Select(s => new PaymentServiceItem { serviceName = s.serviceName, quantity = s.Quantity }).ToList()
                    };
                    var res = await ProcessPaymentRequest(req);
                    if (res?.respondCode == "00") allResponses.Add(res);
                    else errors.Add($"{deptGroup.Key}: {res?.message ?? "API failed"}");
                }
                catch (Exception ex) { errors.Add($"{deptGroup.Key}: {ex.Message}"); }
            }

            Device.BeginInvokeOnMainThread(() => CardPaymentOverlay.IsVisible = false);
            FinalisePaymentResult(allResponses, errors, "Card");
        }

        private void OnCancelCardPayment(object sender, EventArgs e)
        {
            _cardPaymentCts?.Cancel();
            Device.BeginInvokeOnMainThread(() => CardPaymentOverlay.IsVisible = false);
        }

        private async Task ShowCardError(string message)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                CardActivityIndicator.IsRunning = false;
                CardStatusLabel.Text = $"❌ {message}";
                CardStatusFrame.BackgroundColor = Color.FromHex("#FEF2F2");
                CardStatusFrame.BorderColor = Color.FromHex("#FCA5A5");
                CardStatusLabel.TextColor = Color.FromHex("#DC2626");
            });
            await Task.Delay(3500);
            Device.BeginInvokeOnMainThread(() =>
            {
                CardPaymentOverlay.IsVisible = false;
                CardStatusFrame.BackgroundColor = Color.FromHex("#EFF6FF");
                CardStatusFrame.BorderColor = Color.FromHex("#BFDBFE");
                CardStatusLabel.TextColor = Color.FromHex("#1E40AF");
                CardActivityIndicator.IsRunning = true;
            });
        }

        // ─────────────────────────────────────────────────────────
        //  LIFECYCLE & HELPERS
        // ─────────────────────────────────────────────────────────

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            try
            {
                if (_viewModel?.AllServices != null)
                    foreach (var s in _viewModel.AllServices)
                        s.PropertyChanged -= OnServiceItemPropertyChanged;
                _httpClient?.Dispose();
                _httpClient = null;
            }
            catch (Exception ex) { Debug.WriteLine($"[OnDisappearing] {ex.Message}"); }
        }

        private void HandleError(string message, Exception ex)
        {
            Debug.WriteLine($"[Error] {message}: {ex.Message}\n{ex.StackTrace}");
            Device.BeginInvokeOnMainThread(async () =>
            {
                try { await DisplayAlert("Error", $"{message}. Please try again.", "OK"); }
                catch { }
            });
        }

        private async void OnBackNavClicked(object sender, EventArgs e)
        {
            try { await Navigation.PopAsync(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[History] Back navigation error: {ex}");
            }
        }

        private void HandleCriticalError(string message, Exception ex)
        {
            Debug.WriteLine($"[Critical] {message}: {ex.Message}");
            Device.BeginInvokeOnMainThread(async () =>
            {
                try { await DisplayAlert("Critical Error", $"{message}\n\nPlease restart the application.", "OK"); }
                catch { }
            });
        }


    }

    // ── CashConnect status response (kept at namespace level) ──────────────────
    public class CashConnectStatusResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public string rrn { get; set; }
    }

    // ── Patient registration data models (merged from RegisterPatient.xaml.cs) ─
    public class PatientRegistrationObject
    {
        public string FullName { get; set; }
        public string PatentNo { get; set; }
        public string PhoneNumber { get; set; }
        public string Address { get; set; }
        public string Gender { get; set; }
        public string Age { get; set; }
        public string MaritalStatus { get; set; }
    }

    public class PatientRegistrationResponseObject
    {
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("patientId")] public string PatientId { get; set; }
        [JsonProperty("patient")] public PatientData Patient { get; set; }
    }

    public class PatientData
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("fullName")] public string FullName { get; set; }
        [JsonProperty("patentNo")] public string PatentNo { get; set; }
        [JsonProperty("phoneNumber")] public string PhoneNumber { get; set; }
        [JsonProperty("address")] public string Address { get; set; }
        [JsonProperty("gender")] public string Gender { get; set; }
        [JsonProperty("age")] public string Age { get; set; }
        [JsonProperty("maritalStatus")] public string MaritalStatus { get; set; }
    }
}