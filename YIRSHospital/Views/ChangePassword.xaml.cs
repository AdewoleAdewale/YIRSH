using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace YIRSHospital.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ChangePassword : ContentPage
    {
        private bool isCurrentPasswordVisible = false;
        private bool isNewPasswordVisible = false;

        public ChangePassword()
        {
            InitializeComponent();

            // Set initial state
            BottomSheet.TranslationY = 800;
            Overlay.Opacity = 0;
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
            var sheetAnimation = BottomSheet.TranslateTo(0, 0, 400, Easing.SpringOut);

            await Task.WhenAll(overlayAnimation, sheetAnimation);

            // Animate content items with stagger effect
            await Task.WhenAll(
                CurrentPasswordContainer.FadeTo(1, 200).ContinueWith(_ => CurrentPasswordContainer.TranslateTo(0, 0, 200, Easing.CubicOut)),
                Task.Delay(100).ContinueWith(_ => NewPasswordContainer.FadeTo(1, 200)).Unwrap(),
                Task.Delay(100).ContinueWith(_ => NewPasswordContainer.TranslateTo(0, 0, 200, Easing.CubicOut)).Unwrap(),
                Task.Delay(200).ContinueWith(_ => UpdateButton.ScaleTo(1, 300, Easing.SpringOut)).Unwrap()
            );
        }

        private async Task AnimateBottomSheetOut()
        {
            // Scale down button
            var buttonAnimation = UpdateButton.ScaleTo(0.9, 150, Easing.CubicIn);

            // Fade out content
            var contentAnimation = Task.WhenAll(
                CurrentPasswordContainer.FadeTo(0, 150),
                NewPasswordContainer.FadeTo(0, 150)
            );

            await Task.WhenAll(buttonAnimation, contentAnimation);

            // Slide down bottom sheet
            var sheetAnimation = BottomSheet.TranslateTo(0, 800, 300, Easing.CubicIn);
            var overlayAnimation = Overlay.FadeTo(0, 250, Easing.CubicIn);

            await Task.WhenAll(sheetAnimation, overlayAnimation);
        }

        private async void OnOverlayTapped(object sender, EventArgs e)
        {
            await AnimateBottomSheetOut();
            await Navigation.PopModalAsync();
        }

        private void OnToggleCurrentPassword(object sender, EventArgs e)
        {
            isCurrentPasswordVisible = !isCurrentPasswordVisible;
            OldPasswordEntry.IsPassword = !isCurrentPasswordVisible;
            CurrentPasswordToggle.Text = isCurrentPasswordVisible ? "👁️" : "👁️";

            // Animate the toggle
            CurrentPasswordToggle.ScaleTo(1.2, 100).ContinueWith(_ =>
                CurrentPasswordToggle.ScaleTo(1, 100)
            );
        }

        private void OnToggleNewPassword(object sender, EventArgs e)
        {
            isNewPasswordVisible = !isNewPasswordVisible;
            ConfirmPassword.IsPassword = !isNewPasswordVisible;
            NewPasswordToggle.Text = isNewPasswordVisible ? "👁️" : "👁️";

            // Animate the toggle
            NewPasswordToggle.ScaleTo(1.2, 100).ContinueWith(_ =>
                NewPasswordToggle.ScaleTo(1, 100)
            );

            // Show password strength if typing new password
            if (!string.IsNullOrEmpty(ConfirmPassword.Text))
            {
                PasswordStrength.IsVisible = true;
                UpdatePasswordStrength(ConfirmPassword.Text);
            }
        }

        private void UpdatePasswordStrength(string password)
        {
            int strength = 0;

            if (password.Length >= 8) strength++;
            if (System.Text.RegularExpressions.Regex.IsMatch(password, @"[A-Z]")) strength++;
            if (System.Text.RegularExpressions.Regex.IsMatch(password, @"[a-z]")) strength++;
            if (System.Text.RegularExpressions.Regex.IsMatch(password, @"[0-9]")) strength++;
            if (System.Text.RegularExpressions.Regex.IsMatch(password, @"[!@#$%^&*(),.?""':{}|<>]")) strength++;

            // Normalize to 1-4 scale
            strength = Math.Min(4, (strength * 4) / 5);

            Color[] colors = { Color.FromHex("#E0E0E0"), Color.FromHex("#FF5252"), Color.FromHex("#FFC107"), Color.FromHex("#4CAF50"), Color.FromHex("#2E7D32") };

            Strength1.BackgroundColor = strength >= 1 ? colors[Math.Min(strength, 4)] : colors[0];
            Strength2.BackgroundColor = strength >= 2 ? colors[Math.Min(strength, 4)] : colors[0];
            Strength3.BackgroundColor = strength >= 3 ? colors[Math.Min(strength, 4)] : colors[0];
            Strength4.BackgroundColor = strength >= 4 ? colors[Math.Min(strength, 4)] : colors[0];
        }

        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
        {
            // Validate inputs
            if (string.IsNullOrEmpty(OldPasswordEntry.Text))
            {
                await AnimateFieldError(CurrentPasswordBorder);
                await DisplayAlert("VALIDATION", "Please enter your current password", "OKAY");
                return;
            }

            if (string.IsNullOrEmpty(ConfirmPassword.Text))
            {
                await AnimateFieldError(NewPasswordBorder);
                await DisplayAlert("VALIDATION", "Please enter a new password", "OKAY");
                return;
            }

            // Animate button press
            await UpdateButton.ScaleTo(0.95, 100, Easing.CubicOut);
            await UpdateButton.ScaleTo(1, 100, Easing.CubicOut);

            // Change password
            using (UserDialogs.Instance.Loading("Updating password...", null, null, true))
            {
                await Task.Delay(1000);

                string url = "https://yobe.osoftpay.net/api/TaskPayers/ChangePassword?UserName=" +
                           LoginPage.ValidUserMail + "&NewPassword=" + ConfirmPassword.Text;

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

                                if (result != null)
                                {
                                    if (result.status == "00")
                                    {
                                        // Success animation
                                        await AnimateSuccess();

                                        await DisplayAlert("SUCCESS", "Password changed successfully! Please login again.", "OKAY");

                                        await AnimateBottomSheetOut();
                                        Application.Current.MainPage = new NavigationPage(new LoginPage());
                                    }
                                    else
                                    {
                                        await AnimateFieldError(NewPasswordBorder);
                                        await DisplayAlert("ERROR", "Password change failed. Please check your current password.", "OKAY");
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
                    await DisplayAlert("ERROR", "Please check your internet connection", "OKAY");
                    exe.ToString();
                }
            }
        }

        private async Task AnimateFieldError(View field)
        {
            // Shake animation
            for (int i = 0; i < 3; i++)
            {
                await field.TranslateTo(-10, 0, 50);
                await field.TranslateTo(10, 0, 50);
            }
            await field.TranslateTo(0, 0, 50);

            // Flash red border
            if (field is Xamarin.Forms.PancakeView.PancakeView pancake)
            {
                var originalColor = pancake.BorderColor;
                pancake.BorderColor = Color.FromHex("#FF5252");
                await Task.Delay(500);
                pancake.BorderColor = originalColor;
            }
        }

        private async Task AnimateSuccess()
        {
            // Pulse animation
            await UpdateButton.ScaleTo(1.1, 200, Easing.CubicOut);
            await UpdateButton.ScaleTo(1, 200, Easing.CubicOut);

            // Change button color temporarily
            if (UpdateButton is Xamarin.Forms.PancakeView.PancakeView pancake)
            {
                var originalColor = pancake.BackgroundColor;
                pancake.BackgroundColor = Color.FromHex("#4CAF50");
                await Task.Delay(500);
                pancake.BackgroundColor = originalColor;
            }
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


}