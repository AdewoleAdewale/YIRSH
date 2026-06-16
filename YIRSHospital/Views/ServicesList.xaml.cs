using Android.Bluetooth;
using Java.Util;
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
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRSHospital.Services;

namespace YIRSHospital.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class HospitalServicesList : ContentPage
    {
        private string _searchText;

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
            }
        }

        #region Models
        public class Department : INotifyPropertyChanged
        {
            private string _name;
            private int _id;

            public string name
            {
                get => _name;
                set { _name = value; OnPropertyChanged(); }
            }

            public int id
            {
                get => _id;
                set { _id = value; OnPropertyChanged(); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public class ServiceItem : INotifyPropertyChanged
        {
            private string _serviceName;
            private decimal _amount;
            private bool _isSelected;
            private int _quantity = 1;
            private string _departmentName;

            public string serviceName
            {
                get => _serviceName;
                set { _serviceName = value; OnPropertyChanged(); }
            }

            public decimal amount
            {
                get => _amount;
                set { _amount = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedAmount)); OnPropertyChanged(nameof(SubTotal)); }
            }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SubTotal));
                }
            }

            public int Quantity
            {
                get => _quantity;
                set { _quantity = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTotal)); }
            }

            public string DepartmentName
            {
                get => _departmentName;
                set { _departmentName = value; OnPropertyChanged(); }
            }

            public string FormattedAmount => $"₦{amount:N2}";
            public decimal SubTotal => IsSelected ? amount * Quantity : 0;

            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }


        public class PaymentServiceItem
        {
            public string serviceName { get; set; }
            public int quantity { get; set; }
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
            public string PaymentMethod { get; set; }   // ← NEW
        }

        public class PaymentRequest
        {
            public string revName { get; set; }
            public string department { get; set; }
            public string email { get; set; }
            public string hospitalNo { get; set; }
            public string pin { get; set; }
            public string PaymentMethod { get; set; }   // ← NEW: "Cash" | "Pay by Transfer" | "Card"
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

            public ObservableCollection<Department> Departments
            {
                get => _departments;
                set { _departments = value; OnPropertyChanged(); }
            }

            public ObservableCollection<ServiceItem> AllServices
            {
                get => _allServices;
                set { _allServices = value; OnPropertyChanged(); }
            }

            public ObservableCollection<ServiceItem> DisplayedServices
            {
                get => _displayedServices;
                set
                {
                    _displayedServices = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasServices));
                    UpdateCalculations();
                }
            }

            public Department SelectedDepartment
            {
                get => _selectedDepartment;
                set { _selectedDepartment = value; OnPropertyChanged(); }
            }

            public bool IsLoading
            {
                get => _isLoading;
                set { _isLoading = value; OnPropertyChanged(); }
            }

            public string StatusText
            {
                get => _statusText;
                set { _statusText = value; OnPropertyChanged(); }
            }

            public string LoadingMessage
            {
                get => _loadingMessage;
                set { _loadingMessage = value; OnPropertyChanged(); }
            }

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
            protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        #endregion

        #region Fields
        private readonly HospitalViewModel _viewModel;
        private HttpClient _httpClient;
        private const string BASE_URL = "https://yobe.osoftpay.net/api/Agents";
        private string REVENUE_NAME => LoginPage.CollectionPoint?.ToString() ?? "Hospital Services";
        private const string BLUETOOTH_UUID = "00001101-0000-1000-8000-00805f9b34fb";
        private PaymentResultData _currentPaymentResult;
        private string _selectedPaymentMethod = "Cash"; // default

        // CashConnect config — fill in your real base URL & credentials
        private const string CASHCONNECT_BASE_URL = "https://api.cashconnect.ng/v1";
        private const string CASHCONNECT_MERCHANT_ID = "YOUR_MERCHANT_ID";
        private const string CASHCONNECT_TERMINAL_ID = "YOUR_TERMINAL_ID";
        private const string CASHCONNECT_API_KEY = "YOUR_API_KEY";
        #endregion

        #region Constructor
        public HospitalServicesList()
        {
            try
            {
                InitializeComponent();

                _viewModel = new HospitalViewModel();
                BindingContext = _viewModel;

                _httpClient = CreateHttpClient();
                if (ServiceSearchBar != null)
                {
                    ServiceSearchBar.TextColor = Color.FromHex("#1A202C");
                    ServiceSearchBar.PlaceholderColor = Color.FromHex("#718096");
                    ServiceSearchBar.BackgroundColor = Color.White;
                }

                InitializePage();
            }
            catch (Exception ex)
            {
                HandleCriticalError("Failed to initialize page", ex);
            }
        }
        #endregion

        private async void InitializePage()
        {
            try
            {
                await LoadDepartmentsAndServices();
            }
            catch (Exception ex)
            {
                HandleError("Failed to load data", ex);
            }
        }

        #region Initialization

        private void EnsureHttpClientInitialized()
        {
            if (_httpClient == null)
            {
                Debug.WriteLine("HttpClient was null, reinitializing...");
                _httpClient = CreateHttpClient();
            }
        }

        private HttpClient CreateHttpClient()
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                    {
                        // Accept all certificates (adjust for production)
                        return true;
                    },
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls11
                };

                return new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(60)
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating HTTP client: {ex.Message}");
                // Return a basic HttpClient as fallback instead of throwing
                return new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(60)
                };
            }
        }
        private async Task PrintReceipt(string receipt)
        {
            if (string.IsNullOrWhiteSpace(receipt))
            {
                Debug.WriteLine("Empty receipt - skipping print");
                return;
            }

            BluetoothAdapter bluetoothAdapter = null;
            BluetoothSocket socket = null;

            try
            {
#pragma warning disable CS0618
                bluetoothAdapter = BluetoothAdapter.DefaultAdapter;
#pragma warning restore CS0618

                if (bluetoothAdapter == null)
                {
                    Debug.WriteLine("Bluetooth not available");
                    throw new InvalidOperationException("Bluetooth is not available on this device");
                }

                if (!bluetoothAdapter.IsEnabled)
                {
                    Debug.WriteLine("Bluetooth is disabled");
                    throw new InvalidOperationException("Bluetooth is disabled. Please enable it and try again");
                }

                var device = FindBluetoothPrinter(bluetoothAdapter);
                if (device == null)
                {
                    Debug.WriteLine("No printer found");
                    throw new InvalidOperationException("No paired Bluetooth printer found");
                }

                socket = device.CreateRfcommSocketToServiceRecord(UUID.FromString(BLUETOOTH_UUID));
                if (socket == null)
                {
                    Debug.WriteLine("Failed to create socket");
                    throw new InvalidOperationException("Failed to create Bluetooth connection");
                }

                await socket.ConnectAsync();

                if (!socket.IsConnected)
                {
                    Debug.WriteLine("Failed to connect");
                    throw new InvalidOperationException("Failed to connect to printer");
                }

                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(receipt);
                await Task.Delay(1000);
                await socket.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                await socket.OutputStream.FlushAsync();

                Debug.WriteLine("Receipt printed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Print error: {ex.Message}");
                throw;
            }
            finally
            {
                try
                {
                    socket?.Close();
                    socket?.Dispose();
                }
                catch (Exception disposeEx)
                {
                    Debug.WriteLine($"Error disposing socket: {disposeEx.Message}");
                }
            }
        }

        private BluetoothDevice FindBluetoothPrinter(BluetoothAdapter adapter)
        {
            if (adapter == null) return null;

            try
            {
                var printerNames = new[]
                {
                    "MPT-II", "printer001", "RPP02N", "RPP210", "InnerPrinter",
                    "b906", "ANDROID BT", "FP8800", "IposPrinter", "CS10",
                    "MTP-II_89EB", "MP300", "MTP-II-6111", "Internal Bluetooth Printer", "TPS900"
                };

                var bondedDevices = adapter.BondedDevices;
                if (bondedDevices == null || !bondedDevices.Any()) return null;

                return bondedDevices.FirstOrDefault(device =>
                    device?.Name != null && printerNames.Contains(device.Name, StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding printer: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region Helper Methods
        private void ResetForm()
        {
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    if (_viewModel?.AllServices != null)
                    {
                        foreach (var service in _viewModel.AllServices)
                        {
                            service.IsSelected = false;
                            service.Quantity = 1;
                        }
                        _viewModel.UpdateCalculations();
                    }

                    if (PaymentPinEntry != null)
                    {
                        PaymentPinEntry.Text = string.Empty;
                    }

                    if (DepartmentPicker != null)
                    {
                        DepartmentPicker.SelectedItem = null;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resetting form: {ex.Message}");
            }
        }

        private void HandleError(string message, Exception ex)
        {
            Debug.WriteLine($"Error: {message} - {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");

            Device.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await DisplayAlert("Error", $"{message}. Please try again.", "OK");
                }
                catch (Exception displayEx)
                {
                    Debug.WriteLine($"Failed to display error: {displayEx.Message}");
                }
            });
        }

        private void HandleCriticalError(string message, Exception ex)
        {
            Debug.WriteLine($"Critical Error: {message} - {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");

            Device.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await DisplayAlert("Critical Error", $"{message}\n\nPlease restart the application.", "OK");
                }
                catch (Exception displayEx)
                {
                    Debug.WriteLine($"Failed to display critical error: {displayEx.Message}");
                }
            });
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            try
            {
                if (_viewModel?.AllServices != null)
                {
                    foreach (var service in _viewModel.AllServices)
                    {
                        service.PropertyChanged -= OnServiceItemPropertyChanged;
                    }
                }

                _httpClient?.Dispose();
                _httpClient = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }
        #endregion

        #region API Calls
        private async Task LoadDepartmentsAndServices()
        {
            EnsureHttpClientInitialized();
            if (_httpClient == null)
            {
                HandleError("HTTP client not initialized", new InvalidOperationException("HTTP client is null"));
                return;
            }

            try
            {
                _viewModel.IsLoading = true;
                _viewModel.LoadingMessage = "Loading departments and services...";
                _viewModel.StatusText = "Loading...";

                // Step 1: Load all departments
                string deptUrl = $"{BASE_URL}/ListDepartment";
                Debug.WriteLine($"Loading departments from: {deptUrl}");

                var deptResponse = await _httpClient.GetAsync(deptUrl);

                if (!deptResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Server returned {deptResponse.StatusCode}. Please check your connection.");
                }

                var deptJson = await deptResponse.Content.ReadAsStringAsync();
                Debug.WriteLine($"Departments response: {deptJson}");

                if (string.IsNullOrWhiteSpace(deptJson))
                {
                    throw new InvalidOperationException("Empty response from server");
                }

                var departments = JsonConvert.DeserializeObject<List<Department>>(deptJson);

                if (departments == null || !departments.Any())
                {
                    throw new InvalidOperationException("No departments found");
                }

                Debug.WriteLine($"Loaded {departments.Count} departments");

                // Update departments on UI thread
                Device.BeginInvokeOnMainThread(() =>
                {
                    _viewModel.Departments.Clear();
                    foreach (var dept in departments.OrderBy(d => d.name))
                    {
                        _viewModel.Departments.Add(dept);
                    }
                });

                // Step 2: Load services for each department
                _viewModel.LoadingMessage = "Loading all services...";
                var allServices = new List<ServiceItem>();

                foreach (var dept in departments)
                {
                    try
                    {
                        Debug.WriteLine($"Loading services for department: {dept.name}");

                        string servicesUrl = $"{BASE_URL}/ListRevServices?RevHead={Uri.EscapeDataString(REVENUE_NAME)}&Dept={Uri.EscapeDataString(dept.name)}";
                        Debug.WriteLine($"Services URL: {servicesUrl}");

                        var servicesResponse = await _httpClient.GetAsync(servicesUrl);

                        if (servicesResponse.IsSuccessStatusCode)
                        {
                            var servicesJson = await servicesResponse.Content.ReadAsStringAsync();
                            Debug.WriteLine($"Services response for {dept.name}: {servicesJson}");

                            if (!string.IsNullOrWhiteSpace(servicesJson))
                            {
                                var services = JsonConvert.DeserializeObject<List<ServiceItem>>(servicesJson);

                                if (services != null && services.Any())
                                {
                                    Debug.WriteLine($"Found {services.Count} services for {dept.name}");

                                    foreach (var service in services)
                                    {
                                        service.DepartmentName = dept.name;
                                        service.Quantity = 1;
                                        service.IsSelected = false;
                                        service.PropertyChanged += OnServiceItemPropertyChanged;
                                        allServices.Add(service);
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine($"No services found for {dept.name}");
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"Failed to load services for {dept.name}: {servicesResponse.StatusCode}");
                        }

                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading services for {dept.name}: {ex.Message}");
                    }
                }

                Debug.WriteLine($"Total services loaded: {allServices.Count}");

                Device.BeginInvokeOnMainThread(() =>
                {
                    _viewModel.AllServices.Clear();
                    _viewModel.DisplayedServices.Clear();

                    foreach (var service in allServices.OrderBy(s => s.DepartmentName).ThenBy(s => s.serviceName))
                    {
                        _viewModel.AllServices.Add(service);
                        _viewModel.DisplayedServices.Add(service);
                    }

                    _viewModel.StatusText = $"{departments.Count} departments, {allServices.Count} services available";
                    _viewModel.UpdateCalculations();
                });
            }
            catch (HttpRequestException ex)
            {
                HandleError("Network error loading data", ex);
                _viewModel.StatusText = "Failed to load - Check connection";
            }
            catch (JsonException ex)
            {
                HandleError("Failed to parse data", ex);
                _viewModel.StatusText = "Failed to load - Data error";
            }
            catch (Exception ex)
            {
                HandleError("Failed to load data", ex);
                _viewModel.StatusText = "Failed to load";
            }
            finally
            {
                _viewModel.IsLoading = false;
            }
        }
        #endregion

        #region Event Handlers
        private void OnDepartmentChanged(object sender, EventArgs e)
        {
            try
            {
                if (_viewModel?.SelectedDepartment != null && !string.IsNullOrWhiteSpace(_viewModel.SelectedDepartment.name))
                {
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        _viewModel.DisplayedServices.Clear();

                        var filtered = _viewModel.AllServices
                            .Where(s => s.DepartmentName == _viewModel.SelectedDepartment.name)
                            .OrderBy(s => s.serviceName);

                        foreach (var service in filtered)
                        {
                            _viewModel.DisplayedServices.Add(service);
                        }

                        _viewModel.StatusText = $"{_viewModel.DisplayedServices.Count} services in {_viewModel.SelectedDepartment.name}";
                        _viewModel.UpdateCalculations();
                    });
                }
                else
                {
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        _viewModel.DisplayedServices.Clear();
                        foreach (var service in _viewModel.AllServices.OrderBy(s => s.DepartmentName).ThenBy(s => s.serviceName))
                        {
                            _viewModel.DisplayedServices.Add(service);
                        }
                        _viewModel.StatusText = $"{_viewModel.DisplayedServices.Count} total services";
                    });
                }
            }
            catch (Exception ex)
            {
                HandleError("Failed to filter services", ex);
            }
        }

        private void OnServiceSelectionChanged(object sender, CheckedChangedEventArgs e)
        {
            try
            {
                _viewModel?.UpdateCalculations();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating calculations: {ex.Message}");
            }
        }

        private void OnQuantityChanged(object sender, EventArgs e)
        {
            try
            {
                _viewModel?.UpdateCalculations();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating calculations: {ex.Message}");
            }
        }

        private void OnServiceItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == nameof(ServiceItem.IsSelected) || e.PropertyName == nameof(ServiceItem.Quantity))
                {
                    _viewModel?.UpdateCalculations();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling property change: {ex.Message}");
            }
        }

        private async void OnProceedToPayment(object sender, EventArgs e)
        {
            try
            {
                if (_viewModel?.AllServices == null)
                {
                    await DisplayAlert("Error", "No services available", "OK");
                    return;
                }

                var selectedServices = _viewModel.AllServices.Where(s => s.IsSelected).ToList();

                if (!selectedServices.Any())
                {
                    await DisplayAlert("Selection Required", "Please select at least one service", "OK");
                    return;
                }

                ShowPaymentSheet(selectedServices);
            }
            catch (Exception ex)
            {
                HandleError("Failed to proceed to payment", ex);
            }
        }

        private void ShowPaymentSheet(List<ServiceItem> selectedServices)
        {
            try
            {
                if (selectedServices == null || !selectedServices.Any())
                {
                    throw new ArgumentException("No services selected");
                }

                Debug.WriteLine($"Showing payment sheet for {selectedServices.Count} services");

                var departments = string.Join(", ", selectedServices.Select(s => s.DepartmentName).Distinct());

                if (PaymentDepartmentLabel != null)
                {
                    PaymentDepartmentLabel.Text = departments;
                    Debug.WriteLine($"Departments: {departments}");
                }

                if (SelectedServicesContainer != null)
                {
                    SelectedServicesContainer.Children.Clear();

                    var headerStack = new StackLayout
                    {
                        Orientation = StackOrientation.Horizontal,
                        Spacing = 8,
                        Margin = new Thickness(0, 0, 0, 5)
                    };
                    headerStack.Children.Add(new Label
                    {
                        Text = "🛒",
                        FontSize = 16,
                        VerticalOptions = LayoutOptions.Center
                    });
                    headerStack.Children.Add(new Label
                    {
                        Text = "Selected Services",
                        FontSize = 15,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromHex("#1A202C"),
                        VerticalOptions = LayoutOptions.Center
                    });
                    SelectedServicesContainer.Children.Add(headerStack);

                    var groupedServices = selectedServices.GroupBy(s => s.DepartmentName);

                    foreach (var group in groupedServices)
                    {
                        var deptLabel = new Label
                        {
                            Text = $"📋 {group.Key}",
                            FontSize = 13,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromHex("#004225"),
                            Margin = new Thickness(0, 10, 0, 5)
                        };
                        SelectedServicesContainer.Children.Add(deptLabel);

                        foreach (var service in group)
                        {
                            Debug.WriteLine($"Adding: {service.serviceName}, Qty: {service.Quantity}, Amount: {service.SubTotal}");

                            var serviceFrame = new Xamarin.Forms.PancakeView.PancakeView
                            {
                                BackgroundColor = Color.FromHex("#F7FAFC"),
                                BorderColor = Color.FromHex("#E2E8F0"),
                                BorderThickness = 1,
                                CornerRadius = 10,
                                Padding = new Thickness(14),
                                Margin = new Thickness(0, 6)
                            };

                            var grid = new Grid
                            {
                                ColumnDefinitions = new ColumnDefinitionCollection
                                {
                                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                                    new ColumnDefinition { Width = GridLength.Auto }
                                },
                                RowDefinitions = new RowDefinitionCollection
                                {
                                    new RowDefinition { Height = GridLength.Auto },
                                    new RowDefinition { Height = GridLength.Auto }
                                }
                            };

                            var nameLabel = new Label
                            {
                                Text = service.serviceName ?? "Unknown Service",
                                FontSize = 14,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromHex("#1A202C")
                            };
                            Grid.SetRow(nameLabel, 0);
                            Grid.SetColumn(nameLabel, 0);

                            var qtyLabel = new Label
                            {
                                Text = $"Qty: {service.Quantity} × ₦{service.amount:N2}",
                                FontSize = 12,
                                TextColor = Color.FromHex("#718096")
                            };
                            Grid.SetRow(qtyLabel, 1);
                            Grid.SetColumn(qtyLabel, 0);

                            var amountLabel = new Label
                            {
                                Text = $"₦{service.SubTotal:N2}",
                                FontSize = 16,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromHex("#FF6B35"),
                                VerticalOptions = LayoutOptions.Center
                            };
                            Grid.SetRow(amountLabel, 0);
                            Grid.SetRowSpan(amountLabel, 2);
                            Grid.SetColumn(amountLabel, 1);

                            grid.Children.Add(nameLabel);
                            grid.Children.Add(qtyLabel);
                            grid.Children.Add(amountLabel);

                            serviceFrame.Content = grid;
                            SelectedServicesContainer.Children.Add(serviceFrame);
                        }
                    }
                }

                decimal total = selectedServices.Sum(s => s.SubTotal);
                if (PaymentTotalLabel != null)
                {
                    PaymentTotalLabel.Text = $"₦{total:N2}";
                    Debug.WriteLine($"Total: ₦{total:N2}");
                }

                if (PaymentPinEntry != null)
                {
                    PaymentPinEntry.Text = string.Empty;
                }

                if (PaymentSheet != null)
                {
                    PaymentSheet.IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                HandleError("Failed to show payment sheet", ex);
            }
        }

        private void OnClosePaymentSheet(object sender, EventArgs e)
        {
            try
            {
                if (PaymentSheet != null)
                {
                    PaymentSheet.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error closing payment sheet: {ex.Message}");
            }
        }

        private async void OnProcessPayment(object sender, EventArgs e)
        {
            if (PaymentPinEntry == null || string.IsNullOrWhiteSpace(PaymentPinEntry.Text))
            {
                await DisplayAlert("PIN Required", "Please enter your 4-digit agent PIN", "OK");
                return;
            }

            if (PaymentPinEntry.Text.Length != 4 || !PaymentPinEntry.Text.All(char.IsDigit))
            {
                await DisplayAlert("Invalid PIN", "PIN must be exactly 4 digits", "OK");
                return;
            }

            // For card payments, hand off to the card flow immediately
            if (_selectedPaymentMethod == "Card")
            {
                _viewModel.IsLoading = true;
                _viewModel.LoadingMessage = "Processing payment...";

                var selectedServices = _viewModel.AllServices?.Where(s => s.IsSelected).ToList();
                if (selectedServices == null || !selectedServices.Any())
                    throw new InvalidOperationException("No services selected");

                string userEmail = LoginPage.ValidUserMail;
                if (string.IsNullOrWhiteSpace(userEmail))
                    throw new InvalidOperationException("User email not found. Please login again.");

                var groupedByDept = selectedServices.GroupBy(s => s.DepartmentName);
                var allResponses = new List<PaymentResponse>();
                var errors = new List<string>();

                foreach (var deptGroup in groupedByDept)
                {
                    try
                    {
                        var paymentRequest = new PaymentRequest
                        {
                            revName = REVENUE_NAME,
                            department = deptGroup.Key,
                            email = userEmail,
                            pin = PaymentPinEntry.Text,
                            hospitalNo = PatientNo.Text,
                            PaymentMethod = _selectedPaymentMethod,   // ← "Cash" or "Pay by Transfer"
                            services = deptGroup.Select(s => new PaymentServiceItem
                            {
                                serviceName = s.serviceName,
                                quantity = s.Quantity
                            }).ToList()
                        };

                        var response = await ProcessPaymentRequest(paymentRequest);

                        if (response != null && response.respondCode == "00")
                            allResponses.Add(response);
                        else
                            errors.Add($"{deptGroup.Key}: {response?.message ?? "Payment failed"}");
                    }
                    catch (Exception deptEx)
                    {
                        errors.Add($"{deptGroup.Key}: {deptEx.Message}");
                    }
                }

                if (PaymentSheet != null)
                    PaymentSheet.IsVisible = false;

                FinalisePaymentResult(allResponses, errors, _selectedPaymentMethod);
            }

            // ── Cash / Transfer path ──────────────────────────────────────────────
            try
            {
                _viewModel.IsLoading = true;
                _viewModel.LoadingMessage = "Processing payment...";

                var selectedServices = _viewModel.AllServices?.Where(s => s.IsSelected).ToList();
                if (selectedServices == null || !selectedServices.Any())
                    throw new InvalidOperationException("No services selected");

                string userEmail = LoginPage.ValidUserMail;
                if (string.IsNullOrWhiteSpace(userEmail))
                    throw new InvalidOperationException("User email not found. Please login again.");

                var groupedByDept = selectedServices.GroupBy(s => s.DepartmentName);
                var allResponses = new List<PaymentResponse>();
                var errors = new List<string>();

                foreach (var deptGroup in groupedByDept)
                {
                    try
                    {
                        var paymentRequest = new PaymentRequest
                        {
                            revName = REVENUE_NAME,
                            department = deptGroup.Key,
                            email = userEmail,
                            pin = PaymentPinEntry.Text,
                            hospitalNo = PatientNo.Text,
                            PaymentMethod = _selectedPaymentMethod,   // ← "Cash" or "Pay by Transfer"
                            services = deptGroup.Select(s => new PaymentServiceItem
                            {
                                serviceName = s.serviceName,
                                quantity = s.Quantity
                            }).ToList()
                        };

                        var response = await ProcessPaymentRequest(paymentRequest);

                        if (response != null && response.respondCode == "00")
                            allResponses.Add(response);
                        else
                            errors.Add($"{deptGroup.Key}: {response?.message ?? "Payment failed"}");
                    }
                    catch (Exception deptEx)
                    {
                        errors.Add($"{deptGroup.Key}: {deptEx.Message}");
                    }
                }

                if (PaymentSheet != null)
                    PaymentSheet.IsVisible = false;

                FinalisePaymentResult(allResponses, errors, _selectedPaymentMethod);
            }
            catch (InvalidOperationException ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
            catch (HttpRequestException ex)
            {
                FinalisePaymentResult(new List<PaymentResponse>(),
                    new List<string> { ex.Message }, _selectedPaymentMethod);
            }
            catch (Exception ex)
            {
                FinalisePaymentResult(new List<PaymentResponse>(),
                    new List<string> { ex.Message }, _selectedPaymentMethod);
            }
            finally
            {
                _viewModel.IsLoading = false;
            }
        }

        /// <summary>Builds PaymentResultData and shows the result popup.</summary>
        private void FinalisePaymentResult(
            List<PaymentResponse> allResponses,
            List<string> errors,
            string paymentMethod)
        {
            var resultData = new PaymentResultData
            {
                IsSuccess = allResponses.Any(),
                Responses = allResponses,
                TransactionNumbers = allResponses.Select(r => r.transactionNo).ToList(),
                TotalAmount = allResponses.Sum(r => r.totalAmount),
                ErrorDetails = errors.Any() ? string.Join("\n", errors) : null,
                PaymentMethod = paymentMethod   // ← carry to receipt
            };

            if (allResponses.Any() && !errors.Any())
            {
                resultData.Title = "Payment Successful! ✓";
                resultData.Message = $"Payment via {paymentMethod} processed successfully";
            }
            else if (allResponses.Any() && errors.Any())
            {
                resultData.Title = "Partial Success ⚠";
                resultData.Message = $"{allResponses.Count} payment(s) succeeded via {paymentMethod}, {errors.Count} failed";
            }
            else
            {
                resultData.Title = "Payment Failed ✗";
                resultData.Message = "All payment attempts failed";
            }

            _currentPaymentResult = resultData;
            ShowPaymentResultPopup(resultData);
        }
        private void ShowPaymentResultPopup(PaymentResultData resultData)
        {
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    if (PaymentResultPopup == null) return;

                    // Set success/failure styling
                    if (resultData.IsSuccess)
                    {
                        ResultIconLabel.Text = resultData.ErrorDetails == null ? "✓" : "⚠";
                        ResultIconLabel.TextColor = resultData.ErrorDetails == null ? Color.FromHex("#10B981") : Color.FromHex("#F59E0B");
                        ResultIconFrame.BackgroundColor = resultData.ErrorDetails == null ? Color.FromHex("#D1FAE5") : Color.FromHex("#FEF3C7");
                    }
                    else
                    {
                        ResultIconLabel.Text = "✗";
                        ResultIconLabel.TextColor = Color.FromHex("#EF4444");
                        ResultIconFrame.BackgroundColor = Color.FromHex("#FEE2E2");
                    }

                    // Set content
                    ResultTitleLabel.Text = resultData.Title;
                    ResultMessageLabel.Text = resultData.Message;

                    // Clear previous details
                    ResultDetailsContainer.Children.Clear();

                    if (resultData.IsSuccess && resultData.Responses?.Any() == true)
                    {
                        // Show success details
                        foreach (var response in resultData.Responses)
                        {
                            var detailFrame = new Xamarin.Forms.PancakeView.PancakeView
                            {
                                BackgroundColor = Color.FromHex("#F0F9FF"),
                                BorderColor = Color.FromHex("#BAE6FD"),
                                BorderThickness = 1,
                                CornerRadius = 10,
                                Padding = new Thickness(15),
                                Margin = new Thickness(0, 8)
                            };

                            var grid = new Grid
                            {
                                ColumnDefinitions = new ColumnDefinitionCollection
                                {
                                    new ColumnDefinition { Width = GridLength.Auto },
                                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                                },
                                RowSpacing = 5,
                                ColumnSpacing = 5
                            };

                            // Transaction Number
                            var refLabel = new Label
                            {
                                Text = "Ref:",
                                FontSize = 12,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromHex("#1E40AF")
                            };
                            Grid.SetRow(refLabel, 0);
                            Grid.SetColumn(refLabel, 0);

                            var refValue = new Label
                            {
                                Text = response.transactionNo ?? "N/A",
                                FontSize = 13,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromHex("#1A202C")
                            };
                            Grid.SetRow(refValue, 0);
                            Grid.SetColumn(refValue, 1);

                            // Amount
                            var amtLabel = new Label
                            {
                                Text = "Amount:",
                                FontSize = 12,
                                TextColor = Color.FromHex("#1E40AF")
                            };
                            Grid.SetRow(amtLabel, 1);
                            Grid.SetColumn(amtLabel, 0);

                            var amtValue = new Label
                            {
                                Text = $"₦{response.totalAmount:N2}",
                                FontSize = 14,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromHex("#10B981")
                            };
                            Grid.SetRow(amtValue, 1);
                            Grid.SetColumn(amtValue, 1);

                            grid.Children.Add(refLabel);
                            grid.Children.Add(refValue);
                            grid.Children.Add(amtLabel);
                            grid.Children.Add(amtValue);

                            // Add breakdown if available
                            if (response.breakdown?.Any() == true)
                            {
                                var breakdownStack = new StackLayout
                                {
                                    Spacing = 4,
                                    Margin = new Thickness(0, 8, 0, 0)
                                };

                                var breakdownHeader = new Label
                                {
                                    Text = "Services:",
                                    FontSize = 11,
                                    FontAttributes = FontAttributes.Bold,
                                    TextColor = Color.FromHex("#64748B")
                                };
                                breakdownStack.Children.Add(breakdownHeader);

                                foreach (var item in response.breakdown)
                                {
                                    var itemLabel = new Label
                                    {
                                        Text = $"• {item.serviceName} (Qty: {item.quantity}) - ₦{item.subTotal:N2}",
                                        FontSize = 11,
                                        TextColor = Color.FromHex("#475569")
                                    };
                                    breakdownStack.Children.Add(itemLabel);
                                }

                                Grid.SetRow(breakdownStack, 2);
                                Grid.SetColumn(breakdownStack, 0);
                                Grid.SetColumnSpan(breakdownStack, 2);
                                grid.Children.Add(breakdownStack);
                            }

                            detailFrame.Content = grid;
                            ResultDetailsContainer.Children.Add(detailFrame);
                        }

                        // Show total if multiple transactions
                        if (resultData.Responses.Count > 1)
                        {
                            var totalFrame = new Xamarin.Forms.PancakeView.PancakeView
                            {
                                BackgroundGradientStartColor = Color.FromHex("#004225"),
                                BackgroundGradientEndColor = Color.FromHex("#006B3C"),
                                BackgroundGradientAngle = 90,
                                CornerRadius = 10,
                                Padding = new Thickness(15),
                                Margin = new Thickness(0, 8)
                            };

                            var totalStack = new StackLayout
                            {
                                Orientation = StackOrientation.Horizontal,
                                HorizontalOptions = LayoutOptions.FillAndExpand
                            };

                            totalStack.Children.Add(new Label
                            {
                                Text = "Grand Total:",
                                FontSize = 15,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.White,
                                HorizontalOptions = LayoutOptions.StartAndExpand,
                                VerticalOptions = LayoutOptions.Center
                            });

                            totalStack.Children.Add(new Label
                            {
                                Text = $"₦{resultData.TotalAmount:N2}",
                                FontSize = 18,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.White,
                                HorizontalOptions = LayoutOptions.End,
                                VerticalOptions = LayoutOptions.Center
                            });

                            totalFrame.Content = totalStack;
                            ResultDetailsContainer.Children.Add(totalFrame);
                        }
                    }

                    // Show error details if any
                    if (!string.IsNullOrWhiteSpace(resultData.ErrorDetails))
                    {
                        var errorFrame = new Xamarin.Forms.PancakeView.PancakeView
                        {
                            BackgroundColor = Color.FromHex("#FEF2F2"),
                            BorderColor = Color.FromHex("#FCA5A5"),
                            BorderThickness = 1,
                            CornerRadius = 10,
                            Padding = new Thickness(15),
                            Margin = new Thickness(0, 8)
                        };

                        var errorStack = new StackLayout { Spacing = 5 };

                        errorStack.Children.Add(new Label
                        {
                            Text = "⚠ Error Details:",
                            FontSize = 12,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromHex("#DC2626")
                        });

                        errorStack.Children.Add(new Label
                        {
                            Text = resultData.ErrorDetails,
                            FontSize = 11,
                            TextColor = Color.FromHex("#7F1D1D"),
                            LineBreakMode = LineBreakMode.WordWrap
                        });

                        errorFrame.Content = errorStack;
                        ResultDetailsContainer.Children.Add(errorFrame);
                    }

                    // Show/hide print button based on success
                    ResultPrintButton.IsVisible = resultData.IsSuccess;
                    ResultGoBackButtonText.Text = resultData.IsSuccess ? "NEW TRANSACTION" : "TRY AGAIN";

                    // Show popup
                    PaymentResultPopup.IsVisible = true;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing result popup: {ex.Message}");
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Payment Status",
                        resultData.IsSuccess ? "Payment successful!" : "Payment failed. Please try again.",
                        "OK");
                });
            }
        }


        private void OnClosePaymentResult(object sender, EventArgs e)
        {
            try
            {
                if (PaymentResultPopup != null)
                {
                    PaymentResultPopup.IsVisible = false;
                }

                // Reset form if payment was successful
                if (_currentPaymentResult?.IsSuccess == true)
                {
                    ResetForm();
                }

                _currentPaymentResult = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error closing result popup: {ex.Message}");
            }
        }

        private ReceiptData BuildPaymentReceiptData(PaymentResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            var items = new List<ReceiptItem>();

            if (!string.IsNullOrWhiteSpace(PatientNo?.Text))
                items.Add(new ReceiptItem { Description = "Patient ID", SubText = PatientNo.Text.Trim() });

            // ── Payment method row ─────────────────────────────────────────────
            string methodLabel = _currentPaymentResult?.PaymentMethod ?? _selectedPaymentMethod ?? "N/A";
            items.Add(new ReceiptItem { Description = "Payment Method", SubText = methodLabel });
            // ──────────────────────────────────────────────────────────────────

            items.Add(new ReceiptItem { Description = "Services", SubText = string.Empty });

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

            return new ReceiptData
            {
                StoreName = App.RevenueServiceName ?? "YOBE STATE HOSPITALS MANAGEMENT BOARD",
                StorePhone = "Contact:  +234-810-046-6363",
                ReceiptBannerText = "PAYMENT RECEIPT",
                ReceiptNumber = response.transactionNo ?? "N/A",
                AgentName = LoginPage.Name,
                CollectionPoint = REVENUE_NAME,
                PrintDate = DateTime.Now,
                Items = items,
                TotalAmount = response.totalAmount,
                FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY",
                BarcodeLabel = string.IsNullOrWhiteSpace(response.transactionNo)
                    ? null
                    : $"https://yobe.osoftpay.net/singlecollections/verify?TransactId={Uri.EscapeDataString(response.transactionNo)}"
            };
        }

        private async Task<PaymentResponse> ProcessPaymentRequest(PaymentRequest request)
        {
            EnsureHttpClientInitialized();


            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            try
            {
                string url = $"{BASE_URL}/ProcessPayment";
                string jsonContent = JsonConvert.SerializeObject(request);
                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                Debug.WriteLine($"Payment Request: {jsonContent}");

                var response = await _httpClient.PostAsync(url, content);
                var json = await response.Content.ReadAsStringAsync();

                Debug.WriteLine($"Payment Response ({response.StatusCode}): {json}");

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Payment request failed with status {response.StatusCode}");
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    throw new InvalidOperationException("Empty response from payment server");
                }

                var paymentResponse = JsonConvert.DeserializeObject<PaymentResponse>(json);

                if (paymentResponse == null)
                {
                    throw new InvalidOperationException("Failed to parse payment response");
                }

                return paymentResponse;
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"Payment timeout: {ex.Message}");
                throw new HttpRequestException("Payment request timed out. Please try again.");
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Payment network error: {ex.Message}");
                throw;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Payment JSON error: {ex.Message}");
                throw new InvalidOperationException("Invalid response from payment server");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Payment error: {ex.Message}");
                throw;
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string searchText = e.NewTextValue?.Trim().ToLower() ?? string.Empty;

                Device.BeginInvokeOnMainThread(() =>
                {
                    _viewModel.DisplayedServices.Clear();

                    var query = _viewModel.AllServices.AsEnumerable();

                    // Apply department filter if selected
                    if (_viewModel.SelectedDepartment != null && !string.IsNullOrWhiteSpace(_viewModel.SelectedDepartment.name))
                    {
                        query = query.Where(s => s.DepartmentName == _viewModel.SelectedDepartment.name);
                    }

                    // Apply search filter
                    if (!string.IsNullOrWhiteSpace(searchText))
                    {
                        query = query.Where(s =>
                            (s.serviceName?.ToLower().Contains(searchText) ?? false) ||
                            (s.DepartmentName?.ToLower().Contains(searchText) ?? false)
                        );
                    }

                    var results = query.OrderBy(s => s.DepartmentName).ThenBy(s => s.serviceName);

                    foreach (var service in results)
                    {
                        _viewModel.DisplayedServices.Add(service);
                    }

                    _viewModel.StatusText = $"{_viewModel.DisplayedServices.Count} service(s) found";
                    _viewModel.UpdateCalculations();
                });
            }
            catch (Exception ex)
            {
                HandleError("Failed to search services", ex);
            }
        }


        private async void OnPrintReceipt(object sender, EventArgs e)
        {
            if (_currentPaymentResult == null || !_currentPaymentResult.IsSuccess)
            {
                await DisplayAlert("Error", "No successful transactions to print.", "OK");
                return;
            }

            int successCount = 0, failCount = 0;

            try
            {
                _viewModel.IsLoading = true;
                _viewModel.LoadingMessage = "Printing receipts…";

                using (var printerService = new BluetoothPrinterService(use80mm: false))
                {
                    foreach (var response in _currentPaymentResult.Responses)
                    {
                        try
                        {
                            var receiptData = BuildPaymentReceiptData(response);

                            await printerService.PrintReceiptAsync(
                                receipt: receiptData,
                                logoAssetName: "Logo.png",
                                watermarkText: "YOBE STATE HOSPITAL"
                            );

                            successCount++;

                            if (_currentPaymentResult.Responses.Count > 1)
                                await Task.Delay(2500);
                        }
                        catch (PrinterException pex)
                        {
                            failCount++;
                            Debug.WriteLine(
                                $"[OnPrintReceipt] PrinterException for {response.transactionNo}: {pex.Message}");
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            Debug.WriteLine(
                                $"[OnPrintReceipt] Error for {response.transactionNo}: {ex.Message}");
                        }
                    }
                }
            }
            catch (PrinterException pex)
            {
                await DisplayAlert("Printer Error", pex.Message, "OK");
                return;
            }
            catch (Exception ex)
            {
                HandleError("Failed to print receipt", ex);
                return;
            }
            finally
            {
                _viewModel.IsLoading = false;
            }

            if (successCount > 0)
            {
                await DisplayAlert("Print Status",
                    $"{successCount} receipt(s) printed successfully." +
                    (failCount > 0 ? $"\n{failCount} receipt(s) failed." : ""),
                    "OK");
            }
            else
            {
                await DisplayAlert("Print Failed",
                    "Could not print receipts.\n" +
                    "• Ensure the printer is paired, switched on and in range.\n" +
                    "• Tap Print again to retry.",
                    "OK");
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────
        //  PAYMENT METHOD SELECTION HANDLERS
        // ─────────────────────────────────────────────────────────

        private void OnSelectCash(object sender, EventArgs e) => SetPaymentMethod("Cash");
        private void OnSelectTransfer(object sender, EventArgs e) => SetPaymentMethod("Transfer");
        private void OnSelectCard(object sender, EventArgs e) => SetPaymentMethod("Card");

        private void SetPaymentMethod(string method)
        {
            _selectedPaymentMethod = method;

            Device.BeginInvokeOnMainThread(() =>
            {
                // Reset all cards to unselected state
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
                        SelectedMethodBadge.Text = "Cash selected";
                        ProcessButtonLabel.Text = "PROCESS PAYMENT";
                        break;
                    case "Pay by Transfer":
                        Select(TransferMethodCard, TransferMethodLabel);
                        SelectedMethodBadge.Text = "Pay by Transfer selected";
                        ProcessButtonLabel.Text = "PROCESS PAYMENT";
                        break;
                    case "Card":
                        Select(CardMethodCard, CardMethodLabel);
                        SelectedMethodBadge.Text = "Card Payment selected";
                        ProcessButtonLabel.Text = "CHARGE CARD";
                        break;
                }
            });
        }

        // ─────────────────────────────────────────────────────────
        //  CASHCONNECT CARD PAYMENT FLOW
        // ─────────────────────────────────────────────────────────

        private CancellationTokenSource _cardPaymentCts;

        private async Task InitiateCardPayment()
        {
            var selectedServices = _viewModel.AllServices?.Where(s => s.IsSelected).ToList();
            if (selectedServices == null || !selectedServices.Any()) return;

            decimal totalAmount = selectedServices.Sum(s => s.SubTotal);

            // Show card overlay
            Device.BeginInvokeOnMainThread(() =>
            {
                CardAmountLabel.Text = $"₦{totalAmount:N2}";
                CardStatusLabel.Text = "Initialising terminal…";
                CardReferenceStack.IsVisible = false;
                CardActivityIndicator.IsRunning = true;
                CardCancelButton.IsVisible = true;
                CardPaymentOverlay.IsVisible = true;

                // Hide payment sheet behind overlay
                if (PaymentSheet != null)
                    PaymentSheet.IsVisible = false;
            });

            _cardPaymentCts = new CancellationTokenSource();

            try
            {
                // ── Step 1: Initiate transaction on CashConnect ────────────────
                var initResult = await InitiateCashConnectTransaction(totalAmount, _cardPaymentCts.Token);

                if (initResult == null)
                {
                    await ShowCardError("Could not reach the CashConnect terminal. Please try again.");
                    return;
                }

                Device.BeginInvokeOnMainThread(() =>
                {
                    CardStatusLabel.Text = "🖳  Please tap, insert or swipe card on terminal…";
                    CardReferenceStack.IsVisible = true;
                    CardReferenceLabel.Text = initResult.Reference;
                });

                // ── Step 2: Poll for completion ────────────────────────────────
                var pollResult = await PollCashConnectTransaction(
                    initResult.Reference, totalAmount, _cardPaymentCts.Token);

                if (pollResult == null || !pollResult.IsApproved)
                {
                    await ShowCardError(pollResult?.Message ?? "Card transaction declined or timed out.");
                    return;
                }

                // ── Step 3: Card approved → post to hospital API ───────────────
                Device.BeginInvokeOnMainThread(() =>
                {
                    CardStatusLabel.Text = "Card approved ✓ — posting payment…";
                });

                await SubmitHospitalPaymentAfterCard(pollResult.Reference);
            }
            catch (OperationCanceledException)
            {
                await ShowCardError("Transaction cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Card] Unhandled: {ex.Message}");
                await ShowCardError("An unexpected error occurred. Please try again.");
            }
            finally
            {
                _cardPaymentCts?.Dispose();
                _cardPaymentCts = null;
            }
        }

        // ── CashConnect models ────────────────────────────────────────────────────

        private class CashConnectInitResponse
        {
            public string Reference { get; set; }
            public string Status { get; set; }
        }

        private class CashConnectPollResponse
        {
            public bool IsApproved { get; set; }
            public string Reference { get; set; }
            public string Message { get; set; }
            public string Rrn { get; set; }   // bank RRN / approval code
        }

        // ── CashConnect: Initiate ─────────────────────────────────────────────────

        private async Task<CashConnectInitResponse> InitiateCashConnectTransaction(
            decimal amount, CancellationToken ct)
        {
            try
            {
                EnsureHttpClientInitialized();

                var body = new
                {
                    merchantId = CASHCONNECT_MERCHANT_ID,
                    terminalId = CASHCONNECT_TERMINAL_ID,
                    amount = (long)(amount * 100),    // kobo/cents
                    currency = "NGN",
                    reference = $"YOBS-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}"
                };

                var json = JsonConvert.SerializeObject(body);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Remove("x-api-key");
                _httpClient.DefaultRequestHeaders.Add("x-api-key", CASHCONNECT_API_KEY);

                var response = await _httpClient.PostAsync(
                    $"{CASHCONNECT_BASE_URL}/transactions/initiate", content, ct);

                var responseJson = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[CashConnect Init] {responseJson}");

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[CashConnect Init] HTTP {response.StatusCode}");
                    return null;
                }

                return JsonConvert.DeserializeObject<CashConnectInitResponse>(responseJson);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CashConnect Init] Error: {ex.Message}");
                return null;
            }
        }

        // ── CashConnect: Poll for result ──────────────────────────────────────────

        private async Task<CashConnectPollResponse> PollCashConnectTransaction(
        string reference, decimal amount, CancellationToken ct)
        {
            const int maxAttempts = 24;   // 2 minutes (24 × 5s)
            const int delaySeconds = 5;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var response = await _httpClient.GetAsync(
                        $"{CASHCONNECT_BASE_URL}/transactions/status?reference={Uri.EscapeDataString(reference)}", ct);

                    var responseJson = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[CashConnect Poll #{attempt}] {responseJson}");

                    if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(responseJson))
                    {
                        // Define a simple model for the response (add this class somewhere)
                        var parsed = JsonConvert.DeserializeObject<CashConnectStatusResponse>(responseJson);

                        string status = parsed?.status?.ToLowerInvariant() ?? "";

                        if (status == "approved" || status == "success" || status == "completed")
                        {
                            return new CashConnectPollResponse
                            {
                                IsApproved = true,
                                Reference = reference,
                                Message = "Approved",
                                Rrn = parsed?.rrn ?? ""
                            };
                        }

                        if (status == "declined" || status == "failed")
                        {
                            return new CashConnectPollResponse
                            {
                                IsApproved = false,
                                Reference = reference,
                                Message = parsed?.message ?? "Card declined"
                            };
                        }

                        // Still pending
                        Device.BeginInvokeOnMainThread(() =>
                        {
                            int remaining = (maxAttempts - attempt) * delaySeconds;
                            CardStatusLabel.Text = $"Waiting for card... ({remaining}s remaining)\nPresent card on terminal";
                        });
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CashConnect Poll] Error on attempt {attempt}: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            }

            // Timed out
            return new CashConnectPollResponse
            {
                IsApproved = false,
                Reference = reference,
                Message = "Transaction timed out. No response from terminal."
            };
        }

        private async Task SubmitHospitalPaymentAfterCard(string cardReference)
        {
            var selectedServices = _viewModel.AllServices?.Where(s => s.IsSelected).ToList();
            if (selectedServices == null || !selectedServices.Any()) return;

            var allResponses = new List<PaymentResponse>();
            var errors = new List<string>();

            foreach (var deptGroup in selectedServices.GroupBy(s => s.DepartmentName))
            {
                try
                {
                    var paymentRequest = new PaymentRequest
                    {
                        revName = REVENUE_NAME,
                        department = deptGroup.Key,
                        email = LoginPage.ValidUserMail,
                        pin = PaymentPinEntry?.Text ?? "",
                        hospitalNo = PatientNo?.Text ?? "",
                        PaymentMethod = "Card",                      // ← always "Card"
                        services = deptGroup.Select(s => new PaymentServiceItem
                        {
                            serviceName = s.serviceName,
                            quantity = s.Quantity
                        }).ToList()
                    };

                    var response = await ProcessPaymentRequest(paymentRequest);

                    if (response != null && response.respondCode == "00")
                        allResponses.Add(response);
                    else
                        errors.Add($"{deptGroup.Key}: {response?.message ?? "Hospital API failed"}");
                }
                catch (Exception ex)
                {
                    errors.Add($"{deptGroup.Key}: {ex.Message}");
                }
            }

            Device.BeginInvokeOnMainThread(() =>
            {
                CardPaymentOverlay.IsVisible = false;
            });

            FinalisePaymentResult(allResponses, errors, "Card");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private void OnCancelCardPayment(object sender, EventArgs e)
        {
            _cardPaymentCts?.Cancel();
            Device.BeginInvokeOnMainThread(() =>
            {
                CardPaymentOverlay.IsVisible = false;
                // Restore payment sheet so user can try again
                if (PaymentSheet != null)
                    PaymentSheet.IsVisible = true;
            });
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
                CardCancelButton.IsVisible = true;
            });

            await Task.Delay(3500);

            Device.BeginInvokeOnMainThread(() =>
            {
                CardPaymentOverlay.IsVisible = false;
                // Reopen payment sheet so user can pick a different method
                if (PaymentSheet != null)
                    PaymentSheet.IsVisible = true;

                // Reset status frame colours for next attempt
                CardStatusFrame.BackgroundColor = Color.FromHex("#EFF6FF");
                CardStatusFrame.BorderColor = Color.FromHex("#BFDBFE");
                CardStatusLabel.TextColor = Color.FromHex("#1E40AF");
                CardActivityIndicator.IsRunning = true;
            });
        }
    }


}

