using Acr.UserDialogs;
using Android.Bluetooth;
using Java.Util;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRSHospital.Services;

namespace YIRSHospital.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class RegisterPatient : ContentPage
    {
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS = 2000;

        public RegisterPatient()
        {
            try
            {
                InitializeComponent();
                InitializeSSL();

            }
            catch (Exception ex)
            {
                HandleCriticalError("Initialization Error", ex);
            }
        }



        /// <summary>
        /// Initializes SSL/TLS settings
        /// </summary>
        private void InitializeSSL()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                ServicePointManager.ServerCertificateValidationCallback = ValidateServerCertificate;
                ServicePointManager.DefaultConnectionLimit = 10;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SSL Initialization Error: {ex.Message}");
            }
        }

        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            System.Diagnostics.Debug.WriteLine($"Certificate error: {sslPolicyErrors}");
            return true;
        }

        /// <summary>
        /// Validates all required fields
        /// </summary>
        private (bool isValid, string errorMessage) ValidateForm()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(FullName?.Text))
                    return (false, "Full Name is required");

                if (FullName.Text.Trim().Length < 3)
                    return (false, "Full Name must be at least 3 characters");

                if (string.IsNullOrWhiteSpace(PatientNo?.Text))
                    return (false, "Patient Number is required");

                if (string.IsNullOrWhiteSpace(PhoneNumber?.Text))
                    return (false, "Phone Number is required");

                if (!System.Text.RegularExpressions.Regex.IsMatch(PhoneNumber.Text, @"^\d{10,11}$"))
                    return (false, "Phone number must be 10-11 digits only");

                if (string.IsNullOrWhiteSpace(Address?.Text))
                    return (false, "Address is required");

                if (Address.Text.Trim().Length < 3)
                    return (false, "Address must be at least 10 characters");

                if (GenderPicker.SelectedIndex < 0)
                    return (false, "Please select Gender");

                if (string.IsNullOrWhiteSpace(Age?.Text))
                    return (false, "Age is required");

                if (!int.TryParse(Age.Text, out int age) || age < 1 || age > 150)
                    return (false, "Please enter a valid age between 1 and 150");

                if (MaritalStatusPicker.SelectedIndex < 0)
                    return (false, "Please select Marital Status");



                return (true, string.Empty);
            }
            catch (NullReferenceException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Null reference in validation: {ex.Message}");
                return (false, "Form initialization error. Please restart the page.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Validation error: {ex.Message}");
                return (false, "Error validating form. Please try again.");
            }
        }



        /// <summary>
        /// Main registration button handler
        /// </summary>
        private async void TapGestureRecognizer_Tapped(object sender, EventArgs e)
        {
            IDisposable loadingDialog = null;

            try
            {
                // Validate form
                var validation = ValidateForm();
                if (!validation.isValid)
                {
                    await ShowErrorPopup("Validation Error", validation.errorMessage);
                    return;
                }

                loadingDialog = UserDialogs.Instance.Loading("Registering patient...\nPlease wait", null, null, true, MaskType.Black);
                await Task.Delay(500);

                // Prepare registration data
                var registrationData = new PatientRegistrationObject
                {
                    FullName = FullName.Text?.Trim(),
                    PatentNo = PatientNo.Text?.Trim(),
                    PhoneNumber = PhoneNumber.Text?.Trim(),
                    Address = Address.Text?.Trim(),
                    Gender = GenderPicker.SelectedItem?.ToString(),
                    Age = Age.Text?.Trim(),
                    MaritalStatus = MaritalStatusPicker.SelectedItem?.ToString(),

                };

                // Submit registration
                var response = await SubmitRegistration(registrationData);

                loadingDialog?.Dispose();
                loadingDialog = null;

                if (response != null && response.Code == "00")
                {
                    await ShowSuccessPopup(response);
                }
                else
                {
                    string errorMsg = response?.Message ?? "Registration failed. Please try again.";
                    await ShowErrorPopup("Registration Failed", errorMsg);
                }
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Network error: {ex.Message}");
                await ShowErrorPopup("Network Error", "Unable to connect to server. Please check your internet connection and try again.");
            }
            catch (TaskCanceledException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Timeout error: {ex.Message}");
                await ShowErrorPopup("Connection Timeout", "The request took too long. Please check your internet connection and try again.");
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"JSON error: {ex.Message}");
                await ShowErrorPopup("Data Error", "Invalid response from server. Please contact support.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unexpected error: {ex.Message}\n{ex.StackTrace}");
                await ShowErrorPopup("Unexpected Error", "An error occurred during registration. Please try again or contact support.");
            }
            finally
            {
                loadingDialog?.Dispose();
            }
        }

        /// <summary>
        /// Submits registration to API with retry logic
        /// </summary>
        private async Task<PatientRegistrationResponseObject> SubmitRegistration(PatientRegistrationObject data)
        {
            int attempt = 0;
            Exception lastException = null;

            while (attempt < MAX_RETRY_ATTEMPTS)
            {
                try
                {
                    attempt++;
                    System.Diagnostics.Debug.WriteLine($"Registration attempt {attempt} of {MAX_RETRY_ATTEMPTS}");

                    string url = "https://yobe.osoftpay.net/api/Agents/RegisterPatient";

                    using (var handler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
                        SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls11
                    })
                    using (var client = new HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(45);

                        var formData = new List<KeyValuePair<string, string>>
                        {
                            new KeyValuePair<string, string>("FullName", data.FullName ?? ""),
                            new KeyValuePair<string, string>("PatentNo", data.PatentNo ?? ""),
                            new KeyValuePair<string, string>("PhoneNumber", data.PhoneNumber ?? ""),
                            new KeyValuePair<string, string>("Address", data.Address ?? ""),
                            new KeyValuePair<string, string>("Gender", data.Gender ?? ""),
                            new KeyValuePair<string, string>("Age", data.Age ?? ""),
                            new KeyValuePair<string, string>("MaritalStatus", data.MaritalStatus ?? ""),
                            new KeyValuePair<string, string>("AgentName", LoginPage.Name ?? ""),

                        };

                        var content = new FormUrlEncodedContent(formData);
                        var response = await client.PostAsync(url, content);

                        if (response.IsSuccessStatusCode)
                        {
                            var resultString = await response.Content.ReadAsStringAsync();
                            System.Diagnostics.Debug.WriteLine($"API Response: {resultString}");

                            if (string.IsNullOrWhiteSpace(resultString))
                            {
                                throw new Exception("Empty response from server");
                            }

                            return JsonConvert.DeserializeObject<PatientRegistrationResponseObject>(resultString);
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            System.Diagnostics.Debug.WriteLine($"API error {response.StatusCode}: {errorContent}");

                            if (response.StatusCode == HttpStatusCode.BadRequest)
                            {
                                throw new Exception("Invalid data submitted. Please check all fields.");
                            }
                            else if (response.StatusCode == HttpStatusCode.Unauthorized)
                            {
                                throw new Exception("Authentication failed. Please log in again.");
                            }
                            else if (response.StatusCode == HttpStatusCode.InternalServerError)
                            {
                                throw new Exception("Server error. Please try again later.");
                            }
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine($"Network error on attempt {attempt}: {ex.Message}");

                    if (attempt < MAX_RETRY_ATTEMPTS)
                    {
                        await Task.Delay(RETRY_DELAY_MS);
                        continue;
                    }
                }
                catch (TaskCanceledException ex)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine($"Timeout on attempt {attempt}: {ex.Message}");

                    if (attempt < MAX_RETRY_ATTEMPTS)
                    {
                        await Task.Delay(RETRY_DELAY_MS);
                        continue;
                    }
                }
                catch (JsonException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"JSON parsing error: {ex.Message}");
                    throw new JsonException("Invalid server response format", ex);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Unexpected error: {ex.Message}");
                    throw;
                }
            }

            throw new Exception($"Failed after {MAX_RETRY_ATTEMPTS} attempts. " + (lastException?.Message ?? "Unknown error"));
        }

        /// <summary>
        /// Shows custom success popup with patient details
        /// </summary>
        private async Task ShowSuccessPopup(PatientRegistrationResponseObject response)
        {
            try
            {

                var popup = new Frame
                {
                    BackgroundColor = Color.White,
                    CornerRadius = 20,
                    HasShadow = true,
                    Padding = 0,
                    WidthRequest = 450,
                    HeightRequest = 600
                };

                var mainStack = new StackLayout
                {
                    Spacing = 0
                };

                // Success Header
                var headerFrame = new Frame
                {
                    BackgroundColor = Color.FromHex("#4CAF50"),
                    CornerRadius = 20,
                    Padding = new Thickness(20, 25),
                    HasShadow = false
                };

                var headerStack = new StackLayout
                {
                    Spacing = 10,
                    HorizontalOptions = LayoutOptions.Center
                };

                headerStack.Children.Add(new Label
                {
                    Text = "✓",
                    FontSize = 50,
                    TextColor = Color.White,
                    HorizontalOptions = LayoutOptions.Center,
                    FontAttributes = FontAttributes.Bold
                });

                headerStack.Children.Add(new Label
                {
                    Text = "Registration Successful!",
                    FontSize = 20,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.White,
                    HorizontalOptions = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center
                });

                headerFrame.Content = headerStack;
                mainStack.Children.Add(headerFrame);

                // Patient Details
                var detailsStack = new StackLayout
                {
                    Spacing = 15,
                    Padding = new Thickness(25, 20)
                };

                detailsStack.Children.Add(CreateDetailRow("Patient ID:", response.PatientId));
                detailsStack.Children.Add(CreateDetailRow("Patient No:", response.Patient?.PatentNo));
                detailsStack.Children.Add(CreateDetailRow("Name:", response.Patient?.FullName));
                detailsStack.Children.Add(CreateDetailRow("Phone:", response.Patient?.PhoneNumber));
                detailsStack.Children.Add(CreateDetailRow("Gender:", response.Patient?.Gender));
                detailsStack.Children.Add(CreateDetailRow("Age:", response.Patient?.Age));

                mainStack.Children.Add(detailsStack);

                // Buttons
                var buttonStack = new StackLayout
                {
                    Spacing = 10,
                    Padding = new Thickness(25, 0, 25, 20)
                };

                var printButton = new Button
                {
                    Text = "Print Receipt",
                    BackgroundColor = Color.FromHex("#2196F3"),
                    TextColor = Color.White,
                    CornerRadius = 10,
                    HeightRequest = 45,
                    FontAttributes = FontAttributes.Bold
                };

                var newButton = new Button
                {
                    Text = "Register Another",
                    BackgroundColor = Color.FromHex("#FF9800"),
                    TextColor = Color.White,
                    CornerRadius = 10,
                    HeightRequest = 45,
                    FontAttributes = FontAttributes.Bold
                };

                var closeButton = new Button
                {
                    Text = "Close",
                    BackgroundColor = Color.FromHex("#9E9E9E"),
                    TextColor = Color.White,
                    CornerRadius = 10,
                    HeightRequest = 45
                };

                buttonStack.Children.Add(printButton);
                buttonStack.Children.Add(newButton);
                buttonStack.Children.Add(closeButton);

                mainStack.Children.Add(buttonStack);
                popup.Content = mainStack;

                // Create overlay
                var overlay = new AbsoluteLayout
                {
                    BackgroundColor = Color.FromRgba(0, 0, 0, 0.7)
                };

                AbsoluteLayout.SetLayoutBounds(popup, new Rectangle(0.5, 0.5, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
                AbsoluteLayout.SetLayoutFlags(popup, AbsoluteLayoutFlags.PositionProportional);

                overlay.Children.Add(popup);

                // Show popup
                var existingContent = Content;
                Content = overlay;

                bool shouldClose = false;
                bool shouldPrint = false;
                bool shouldRegisterNew = false;

                printButton.Clicked += (s, e) =>
                {
                    shouldPrint = true;
                    shouldClose = true;
                };

                newButton.Clicked += (s, e) =>
                {
                    shouldRegisterNew = true;
                    shouldClose = true;
                };

                closeButton.Clicked += (s, e) =>
                {
                    shouldClose = true;
                };

                // Wait for user action
                while (!shouldClose)
                {
                    await Task.Delay(100);
                }

                // Restore original content
                Content = existingContent;

                // Handle actions
                if (shouldPrint)
                {
                    await PrintReceipt(response);
                }

                if (shouldRegisterNew)
                {
                    ClearForm();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing success popup: {ex.Message}");
                // Fallback to simple alert
                await DisplayAlert("Success", $"Patient registered successfully!\nPatient ID: {response?.PatientId}", "OK");
            }
        }

        /// <summary>
        /// Shows custom error popup
        /// </summary>
        private async Task ShowErrorPopup(string title, string message)
        {
            try
            {
                var popup = new Frame
                {
                    BackgroundColor = Color.White,
                    CornerRadius = 20,
                    HasShadow = true,
                    Padding = 0,
                    WidthRequest = 450,
                    HeightRequest = 600
                };

                var mainStack = new StackLayout
                {
                    Spacing = 0
                };

                // Error Header
                var headerFrame = new Frame
                {
                    BackgroundColor = Color.FromHex("#F44336"),
                    CornerRadius = 20,
                    Padding = new Thickness(20, 25),
                    HasShadow = false
                };

                var headerStack = new StackLayout
                {
                    Spacing = 10,
                    HorizontalOptions = LayoutOptions.Center
                };

                headerStack.Children.Add(new Label
                {
                    Text = "✕",
                    FontSize = 50,
                    TextColor = Color.White,
                    HorizontalOptions = LayoutOptions.Center,
                    FontAttributes = FontAttributes.Bold
                });

                headerStack.Children.Add(new Label
                {
                    Text = title,
                    FontSize = 20,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.White,
                    HorizontalOptions = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center
                });

                headerFrame.Content = headerStack;
                mainStack.Children.Add(headerFrame);

                // Error Message
                var messageFrame = new Frame
                {
                    BackgroundColor = Color.FromHex("#FFEBEE"),
                    CornerRadius = 10,
                    Padding = new Thickness(20),
                    Margin = new Thickness(25, 20),
                    HasShadow = false
                };

                messageFrame.Content = new Label
                {
                    Text = message,
                    FontSize = 14,
                    TextColor = Color.FromHex("#C62828"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.WordWrap
                };

                mainStack.Children.Add(messageFrame);

                // Tips
                var tipsStack = new StackLayout
                {
                    Padding = new Thickness(25, 0),
                    Spacing = 5
                };

                tipsStack.Children.Add(new Label
                {
                    Text = "💡 Troubleshooting Tips:",
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromHex("#666666")
                });

                tipsStack.Children.Add(new Label
                {
                    Text = "• Check your internet connection\n• Verify all required fields\n• Contact support if issue persists",
                    FontSize = 11,
                    TextColor = Color.FromHex("#999999"),
                    LineBreakMode = LineBreakMode.WordWrap
                });

                mainStack.Children.Add(tipsStack);

                // Close Button
                var buttonStack = new StackLayout
                {
                    Padding = new Thickness(25, 20, 25, 20)
                };

                var closeButton = new Button
                {
                    Text = "OK, I Understand",
                    BackgroundColor = Color.FromHex("#F44336"),
                    TextColor = Color.White,
                    CornerRadius = 10,
                    HeightRequest = 45,
                    FontAttributes = FontAttributes.Bold
                };

                buttonStack.Children.Add(closeButton);
                mainStack.Children.Add(buttonStack);

                popup.Content = mainStack;

                // Create overlay
                var overlay = new AbsoluteLayout
                {
                    BackgroundColor = Color.FromRgba(0, 0, 0, 0.7)
                };

                AbsoluteLayout.SetLayoutBounds(popup, new Rectangle(0.5, 0.5, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
                AbsoluteLayout.SetLayoutFlags(popup, AbsoluteLayoutFlags.PositionProportional);

                overlay.Children.Add(popup);

                // Show popup
                var existingContent = Content;
                Content = overlay;

                bool shouldClose = false;

                closeButton.Clicked += (s, e) =>
                {
                    shouldClose = true;
                };

                // Wait for user to close
                while (!shouldClose)
                {
                    await Task.Delay(100);
                }

                // Restore original content
                Content = existingContent;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing error popup: {ex.Message}");
                // Fallback to simple alert
                await DisplayAlert(title, message, "OK");
            }
        }

        private StackLayout CreateDetailRow(string label, string value)
        {
            var stack = new StackLayout
            {
                Orientation = StackOrientation.Horizontal,
                Spacing = 10
            };

            stack.Children.Add(new Label
            {
                Text = label,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromHex("#666666"),
                WidthRequest = 100
            });

            stack.Children.Add(new Label
            {
                Text = value ?? "N/A",
                FontSize = 13,
                TextColor = Color.FromHex("#212121"),
                HorizontalOptions = LayoutOptions.FillAndExpand,
                LineBreakMode = LineBreakMode.TailTruncation
            });

            return stack;
        }

        /// <summary>
        /// Prints receipt via Bluetooth
        /// </summary>

        /// <summary>
        /// Generates formatted text for printing
        /// </summary>

        /// <summary>
        /// Sends data to Bluetooth printer
        /// </summary>
        private async Task CallPrinter(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                LogError(new ArgumentException("Print input is empty"), "Cannot print empty content");
                return;
            }

            try
            {
#pragma warning disable CS0618
                using (BluetoothAdapter bluetoothAdapter = BluetoothAdapter.DefaultAdapter)
#pragma warning restore CS0618
                {
                    if (bluetoothAdapter == null)
                    {
                        await Device.InvokeOnMainThreadAsync(async () =>
                        {
                            await DisplayAlert("Printer Error", "No Bluetooth adapter found on this device", "Ok");
                        });
                        return;
                    }

                    if (!bluetoothAdapter.IsEnabled)
                    {
                        await Device.InvokeOnMainThreadAsync(async () =>
                        {
                            bool enableBt = await DisplayAlert("Bluetooth Disabled",
                                "Bluetooth is required for printing. Would you like to enable it?",
                                "Yes", "No");

                            if (enableBt)
                            {
                                // Note: Actual Bluetooth enabling requires platform-specific implementation
                                await DisplayAlert("Enable Bluetooth", "Please enable Bluetooth in your device settings", "Ok");
                            }
                        });
                        return;
                    }

                    string[] printerNames = {
                        "MPT-II", "printer001", "RPP02N", "RPP210", "InnerPrinter",
                        "b906", "ANDROID BT", "FP8800", "IposPrinter", "CS10",
                        "Q2i", "Internal Bluetooth Printer", "Bluetooth Printer"
                    };

                    BluetoothDevice device = null;

                    try
                    {
                        device = (from bd in bluetoothAdapter.BondedDevices
                                  where bd != null && !string.IsNullOrEmpty(bd.Name) && printerNames.Contains(bd.Name)
                                  select bd).FirstOrDefault();
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, "Error accessing bonded devices");
                    }

                    if (device == null)
                    {
                        await Device.InvokeOnMainThreadAsync(async () =>
                        {
                            await DisplayAlert("Printer Not Found",
                                "No paired Bluetooth printer found. Please pair your printer in Bluetooth settings:\n\n" +
                                "Settings → Bluetooth → Pair new device", "Ok");
                        });
                        return;
                    }

                    await PrintWithRetry(device, input, MAX_RETRY_ATTEMPTS);
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "Printer error");
                await Device.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Printer Error",
                        "Failed to print receipt. Your transaction was successful and the receipt is saved in Payment History.", "Ok");
                });
            }
        }

        private async Task PrintWithRetry(BluetoothDevice device, string input, int attemptsRemaining)
        {
            if (device == null || string.IsNullOrWhiteSpace(input))
            {
                LogError(new ArgumentException("Invalid print parameters"), "Cannot print with null device or empty input");
                return;
            }

            BluetoothSocket socket = null;

            try
            {
                socket = device.CreateRfcommSocketToServiceRecord(
                    UUID.FromString("00001101-0000-1000-8000-00805f9b34fb"));

                if (socket == null)
                {
                    throw new Exception("Failed to create Bluetooth socket");
                }

                await socket.ConnectAsync();

                if (socket.IsConnected)
                {
                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(input);
                    await Task.Delay(2000); // Give printer time to initialize

                    if (socket.OutputStream != null)
                    {
                        await socket.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        await socket.OutputStream.FlushAsync();
                        await Task.Delay(1000); // Wait for print to complete

                        LogError(null, "Print successful");
                    }
                    else
                    {
                        throw new Exception("Printer output stream is null");
                    }

                    socket.Close();
                    socket.Dispose();
                    return;
                }
                else
                {
                    throw new Exception("Failed to connect to printer");
                }
            }
            catch (Exception ex)
            {
                LogError(ex, $"Print attempt failed (attempts remaining: {attemptsRemaining - 1})");

                try
                {
                    socket?.Close();
                    socket?.Dispose();
                }
                catch (Exception disposeEx)
                {
                    LogError(disposeEx, "Error disposing socket");
                }
            }

            // Retry logic
            if (attemptsRemaining > 1)
            {
                bool retry = await Device.InvokeOnMainThreadAsync(async () =>
                {
                    return await DisplayAlert(
                        $"Printer Connection ({attemptsRemaining - 1} attempts remaining)",
                        "Failed to connect to printer. Please ensure:\n" +
                        "• Printer is turned on\n" +
                        "• Printer has paper\n" +
                        "• Printer is within Bluetooth range\n\n" +
                        "Would you like to try again?",
                        "Retry",
                        "Cancel");
                });

                if (retry)
                {
                    await Task.Delay(2000); // Wait before retry
                    await PrintWithRetry(device, input, attemptsRemaining - 1);
                }
                else
                {
                    await Device.InvokeOnMainThreadAsync(async () =>
                    {
                        await DisplayAlert("Print Cancelled",
                            "Receipt printing cancelled. Your transaction is saved in Payment History.", "Ok");
                    });
                }
            }
            else
            {
                await Device.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Print Failed",
                        "Could not print receipt after multiple attempts. Your transaction was successful and is saved in Payment History.", "Ok");
                });
            }
        }

        private async Task PrintReceipt(PatientRegistrationResponseObject response)
        {
            IDisposable loadingDialog = null;
            try
            {
                if (response?.Patient == null)
                {
                    await ShowErrorPopup("Print Error", "No patient data to print.");
                    return;
                }

                loadingDialog = UserDialogs.Instance.Loading(
                    "Connecting to printer…", null, null, true, MaskType.Black);

                // ── Build ReceiptData ──────────────────────────────────────────
                var receipt = new ReceiptData
                {
                    // Header
                    StoreName = App.RevenueServiceName ?? "YOBE STATE HOSPITALS MANAGEMENT BOARD",
                    StorePhone = "Contact: 234-810-046-6363",
                    ReceiptBannerText = "PATIENT REGISTRATION",

                    // Metadata
                    ReceiptNumber = response.PatientId ?? "N/A",
                    AgentName = LoginPage.Name,
                    CollectionPoint = LoginPage.CollectionPoint,
                    PrintDate = DateTime.Now,

                    // No monetary totals for registration
                    TotalAmount = 0m,
                    AmountPaid = 0m,

                    // Footer
                    FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY",


                    Items = new List<ReceiptItem>
            {
                new ReceiptItem { Description = "Patient ID", SubText = response.PatientId ?? "N/A" },
                new ReceiptItem { Description = "Patient No", SubText = response.Patient.PatentNo ?? "N/A" },
                new ReceiptItem { Description = "Full Name",  SubText = response.Patient.FullName ?? "N/A" },
                new ReceiptItem { Description = "Phone",      SubText = response.Patient.PhoneNumber ?? "N/A" },
                new ReceiptItem { Description = "Gender",     SubText = response.Patient.Gender ?? "N/A" },
                new ReceiptItem { Description = "Age",        SubText = response.Patient.Age ?? "N/A" },
                new ReceiptItem { Description = "Marital",    SubText = response.Patient.MaritalStatus ?? "N/A" },
            }
                };

                // ── Send to printer ────────────────────────────────────────────
                using (var printerService = new BluetoothPrinterService(use80mm: false))
                {
                    await printerService.PrintReceiptAsync(
                        receipt: receipt,
                        logoAssetName: "Logo.png",
                        watermarkText: "YOBE STATE HOSPITAL"
                    );
                }

                loadingDialog?.Dispose();
                loadingDialog = null;

                await DisplayAlert("Printed", "Patient receipt printed successfully.", "OK");
            }
            catch (PrinterException pex)
            {
                loadingDialog?.Dispose();
                loadingDialog = null;
                System.Diagnostics.Debug.WriteLine($"[PrintReceipt] PrinterException: {pex.Message}");

                bool retry = await DisplayAlert(
                    "Printer Error",
                    pex.Message + "\n\nWould you like to retry?",
                    "Retry", "Cancel");

                if (retry) await PrintReceipt(response);
            }
            catch (Exception ex)
            {
                loadingDialog?.Dispose();
                loadingDialog = null;
                System.Diagnostics.Debug.WriteLine($"[PrintReceipt] Error: {ex.Message}");
                await ShowErrorPopup("Print Error",
                    "Unable to print receipt. " +
                    "Check the Bluetooth printer is paired, switched on, and in range.");
            }
            finally
            {
                loadingDialog?.Dispose();
            }
        }
        private void LogError(Exception ex, string message)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {message}");
                if (ex != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Exception: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                    }
                }
            }
            catch
            {
                // Fail silently if logging fails
            }
        }


        /// <summary>
        /// Clears all form fields
        /// </summary>
        private void ClearForm()
        {

            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    FullName.Text = string.Empty;
                    PatientNo.Text = string.Empty;
                    PhoneNumber.Text = string.Empty;
                    Address.Text = string.Empty;
                    GenderPicker.SelectedIndex = -1;
                    Age.Text = string.Empty;
                    MaritalStatusPicker.SelectedIndex = -1;

                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing form: {ex.Message}");
            }
        }

        private async Task HandleError(string title, Exception ex)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"{title}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                string userMessage = "An error occurred. Please try again.";

                if (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    userMessage = "Network error. Please check your internet connection and try again.";
                }
                else if (ex is JsonException)
                {
                    userMessage = "Invalid data received. Please contact support.";
                }

                await ShowErrorPopup("Registration Failed", userMessage);
            }
            catch (Exception displayEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error displaying error: {displayEx.Message}");
            }
        }

        private void HandleCriticalError(string title, Exception ex)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"CRITICAL - {title}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                Device.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert(title, "A critical error occurred. Please restart the application.", "OK");
                });
            }
            catch (Exception displayEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error displaying critical error: {displayEx.Message}");
            }
        }

        protected override void OnDisappearing()
        {
            try
            {
                base.OnDisappearing();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnDisappearing: {ex.Message}");
            }
        }
    }

    #region Data Models




    #endregion
}