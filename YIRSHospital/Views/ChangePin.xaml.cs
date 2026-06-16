using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.PancakeView;
using Xamarin.Forms.Xaml;

namespace YIRSHospital.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ChangePin : ContentPage
    {
        private bool isCurrentPinVisible = false;
        private bool isNewPinVisible = false;
        private PancakeView[] currentDots;
        private PancakeView[] newDots;

        public ChangePin()
        {
            InitializeComponent();

            // Initialize dot arrays
            currentDots = new[] { CurrentDot1, CurrentDot2, CurrentDot3, CurrentDot4, CurrentDot5, CurrentDot6 };
            newDots = new[] { NewDot1, NewDot2, NewDot3, NewDot4, NewDot5, NewDot6 };

            // Set initial state
            BottomSheet.TranslationY = 800;
            Overlay.Opacity = 0;
            CurrentPinContainer.Opacity = 0;
            NewPinContainer.Opacity = 0;
            UpdateButton.Scale = 0.8;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await AnimateBottomSheetIn();
        }

        private async Task AnimateBottomSheetIn()
        {
            // Animate overlay
            var overlayAnimation = Overlay.FadeTo(0.5, 300, Easing.CubicOut);

            // Animate bottom sheet with spring effect
            var sheetAnimation = BottomSheet.TranslateTo(0, 0, 450, Easing.SpringOut);

            await Task.WhenAll(overlayAnimation, sheetAnimation);

            // Staggered content animation
            await CurrentPinContainer.FadeTo(1, 250, Easing.CubicOut);
            await Task.Delay(100);
            await NewPinContainer.FadeTo(1, 250, Easing.CubicOut);
            await Task.Delay(100);
            await UpdateButton.ScaleTo(1, 300, Easing.SpringOut);
        }

        private async Task AnimateBottomSheetOut()
        {
            // Fade out content with stagger
            var tasks = new[]
            {
                UpdateButton.ScaleTo(0.8, 150, Easing.CubicIn),
                NewPinContainer.FadeTo(0, 150, Easing.CubicIn),
                Task.Delay(50).ContinueWith(_ => CurrentPinContainer.FadeTo(0, 150, Easing.CubicIn)).Unwrap()
            };

            await Task.WhenAll(tasks);

            // Slide down bottom sheet
            var sheetAnimation = BottomSheet.TranslateTo(0, 800, 300, Easing.CubicIn);
            var overlayAnimation = Overlay.FadeTo(0, 250, Easing.CubicIn);

            await Task.WhenAll(sheetAnimation, overlayAnimation);
        }

        private void OnOldPinTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePinDots(e.NewTextValue, currentDots, CurrentPinBorder);
        }

        private void OnNewPinTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePinDots(e.NewTextValue, newDots, NewPinBorder);
            UpdatePinStrength(e.NewTextValue);
        }

        private async void UpdatePinDots(string pin, PancakeView[] dots, PancakeView border)
        {
            if (pin == null) pin = "";

            Color activeColor = Color.FromHex("#004225");
            Color inactiveColor = Color.FromHex("#E0E0E0");

            for (int i = 0; i < dots.Length; i++)
            {
                if (i < pin.Length)
                {
                    // Animate dot filling
                    dots[i].BackgroundColor = activeColor;
                    await dots[i].ScaleTo(1.3, 100, Easing.CubicOut);
                    await dots[i].ScaleTo(1, 100, Easing.CubicOut);
                }
                else
                {
                    dots[i].BackgroundColor = inactiveColor;
                    dots[i].Scale = 1;
                }
            }

            // Animate border on complete PIN
            if (pin.Length >= 4)
            {
                border.BorderColor = Color.FromHex("#4CAF50");
                await border.ScaleTo(1.02, 100, Easing.CubicOut);
                await border.ScaleTo(1, 100, Easing.CubicOut);
            }
            else
            {
                border.BorderColor = Color.FromHex("#E0E0E0");
            }
        }

        private void UpdatePinStrength(string pin)
        {
            if (string.IsNullOrEmpty(pin))
            {
                PinFeedback.IsVisible = false;
                return;
            }

            PinFeedback.IsVisible = true;

            // Check for weak patterns
            bool hasSequence = HasSequentialDigits(pin);
            bool hasRepeating = HasRepeatingDigits(pin);

            Color weakColor = Color.FromHex("#FF5252");
            Color mediumColor = Color.FromHex("#FFC107");
            Color strongColor = Color.FromHex("#4CAF50");
            Color inactiveColor = Color.FromHex("#E0E0E0");

            if (pin.Length < 4)
            {
                FeedbackLabel.Text = "Too short - minimum 4 digits";
                FeedbackLabel.TextColor = weakColor;
                PinStrength1.BackgroundColor = weakColor;
                PinStrength2.BackgroundColor = inactiveColor;
                PinStrength3.BackgroundColor = inactiveColor;
            }
            else if (hasSequence || hasRepeating)
            {
                FeedbackLabel.Text = "Weak - avoid sequences or repeating digits";
                FeedbackLabel.TextColor = weakColor;
                PinStrength1.BackgroundColor = weakColor;
                PinStrength2.BackgroundColor = weakColor;
                PinStrength3.BackgroundColor = inactiveColor;
            }

            else
            {
                FeedbackLabel.Text = "Strong - excellent security!";
                FeedbackLabel.TextColor = strongColor;
                PinStrength1.BackgroundColor = strongColor;
                PinStrength2.BackgroundColor = strongColor;
                PinStrength3.BackgroundColor = strongColor;
            }
        }

        private bool HasSequentialDigits(string pin)
        {
            for (int i = 0; i < pin.Length - 2; i++)
            {
                if (char.IsDigit(pin[i]) && char.IsDigit(pin[i + 1]) && char.IsDigit(pin[i + 2]))
                {
                    int d1 = pin[i] - '0';
                    int d2 = pin[i + 1] - '0';
                    int d3 = pin[i + 2] - '0';

                    if ((d2 == d1 + 1 && d3 == d2 + 1) || (d2 == d1 - 1 && d3 == d2 - 1))
                        return true;
                }
            }
            return false;
        }

        private bool HasRepeatingDigits(string pin)
        {
            if (pin.Length < 3) return false;

            var groups = pin.GroupBy(c => c);
            return groups.Any(g => g.Count() >= 3);
        }

        private async void OnToggleCurrentPin(object sender, EventArgs e)
        {
            isCurrentPinVisible = !isCurrentPinVisible;
            OldPINEntry.IsPassword = !isCurrentPinVisible;

            // Animate toggle with bounce
            await CurrentPinToggle.ScaleTo(1.3, 100, Easing.CubicOut);
            await CurrentPinToggle.ScaleTo(1, 100, Easing.CubicOut);
        }

        private async void OnToggleNewPin(object sender, EventArgs e)
        {
            isNewPinVisible = !isNewPinVisible;
            ConfirmPIN.IsPassword = !isNewPinVisible;

            // Animate toggle with bounce
            await NewPinToggle.ScaleTo(1.3, 100, Easing.CubicOut);
            await NewPinToggle.ScaleTo(1, 100, Easing.CubicOut);
        }

        private async void OnOverlayTapped(object sender, EventArgs e)
        {
            await AnimateBottomSheetOut();
            await Navigation.PopModalAsync();
        }

        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
        {
            // Validate inputs
            if (string.IsNullOrEmpty(OldPINEntry.Text) || OldPINEntry.Text.Length < 4)
            {
                await AnimateFieldError(CurrentPinBorder);
                await DisplayAlert("VALIDATION", "Please enter your current PIN (minimum 4 digits)", "OKAY");
                return;
            }

            if (string.IsNullOrEmpty(ConfirmPIN.Text) || ConfirmPIN.Text.Length < 4)
            {
                await AnimateFieldError(NewPinBorder);
                await DisplayAlert("VALIDATION", "Please enter a new PIN (minimum 4 digits)", "OKAY");
                return;
            }

            if (OldPINEntry.Text == ConfirmPIN.Text)
            {
                await AnimateFieldError(NewPinBorder);
                await DisplayAlert("VALIDATION", "New PIN must be different from current PIN", "OKAY");
                return;
            }

            // Animate button press
            await UpdateButton.ScaleTo(0.95, 100, Easing.CubicOut);
            await UpdateButton.ScaleTo(1, 100, Easing.CubicOut);

            // Show loading state
            UpdateButtonLabel.IsVisible = false;
            UpdateLoader.IsVisible = true;
            UpdateLoader.IsRunning = true;

            await Task.Delay(500);

            // Validate old PIN first
            if (OldPINEntry.Text != LoginPage.Pin)
            {
                UpdateButtonLabel.IsVisible = true;
                UpdateLoader.IsVisible = false;
                UpdateLoader.IsRunning = false;

                await AnimateFieldError(CurrentPinBorder);
                await DisplayAlert("ERROR", "Current PIN is incorrect. Please try again.", "OKAY");
                return;
            }

            // Change PIN
            string url = "https://yobe.osoftpay.net/api/TaskPayers/ChangePin?UserName=" +
                       LoginPage.ValidUserMail + "&NewPin=" + ConfirmPIN.Text;

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

                using (HttpClient client = new HttpClient(handler))
                {
                    using (HttpResponseMessage response = await client.GetAsync(url))
                    {
                        using (HttpContent content = response.Content)
                        {
                            var json = await content.ReadAsStringAsync();
                            InterfacePass result = JsonConvert.DeserializeObject<InterfacePass>(json);

                            UpdateButtonLabel.IsVisible = true;
                            UpdateLoader.IsVisible = false;
                            UpdateLoader.IsRunning = false;

                            if (result != null)
                            {
                                if (result.status == "00")
                                {
                                    // Success animation
                                    await AnimateSuccess();

                                    await DisplayAlert("SUCCESS", "PIN changed successfully! Please login again.", "OKAY");

                                    await AnimateBottomSheetOut();
                                    Application.Current.MainPage = new NavigationPage(new LoginPage());
                                }
                                else
                                {
                                    await AnimateFieldError(NewPinBorder);
                                    await DisplayAlert("ERROR", "PIN change failed. Please try again.", "OKAY");
                                }
                            }
                            else
                            {
                                await DisplayAlert("ERROR", "Connection failed. Please try again.", "OKAY");
                            }
                        }
                    }
                }
            }
            catch (Exception exe)
            {
                UpdateButtonLabel.IsVisible = true;
                UpdateLoader.IsVisible = false;
                UpdateLoader.IsRunning = false;

                await DisplayAlert("ERROR", "Please check your internet connection", "OKAY");
                exe.ToString();
            }
        }

        private async Task AnimateFieldError(PancakeView field)
        {
            // Shake animation
            uint duration = 50;
            for (int i = 0; i < 4; i++)
            {
                await field.TranslateTo(-12, 0, duration);
                await field.TranslateTo(12, 0, duration);
            }
            await field.TranslateTo(0, 0, duration);

            // Flash red border
            var originalColor = field.BorderColor;
            field.BorderColor = Color.FromHex("#FF5252");
            field.BorderThickness = 2;
            await Task.Delay(600);
            field.BorderColor = originalColor;
            field.BorderThickness = 2;
        }

        private async Task AnimateSuccess()
        {
            // Multiple pulse animation
            for (int i = 0; i < 2; i++)
            {
                await UpdateButton.ScaleTo(1.08, 150, Easing.CubicOut);
                await UpdateButton.ScaleTo(1, 150, Easing.CubicOut);
            }

            // Change button to success color
            UpdateButton.BackgroundGradientStartColor = Color.FromHex("#66BB6A");
            UpdateButton.BackgroundGradientEndColor = Color.FromHex("#4CAF50");
            UpdateButtonLabel.Text = "✓ Success!";

            await Task.Delay(800);
        }

        // Handle Android back button
        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                await AnimateBottomSheetOut();
                await Navigation.PopModalAsync();
            });
            return true;
        }
    }

    internal class InterfacePass
    {

        public string status { get; set; }
        public string message { get; set; }
        public details agentdetails { get; set; }

    }

    internal class details
    {

        public string agent { get; set; }
        public string password { get; set; }
        public string accountPin { get; set; }
        public string super_Agent { get; set; }
    }


}