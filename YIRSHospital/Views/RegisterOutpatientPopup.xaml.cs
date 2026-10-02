using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.CommunityToolkit.UI.Views;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRSHospital.Models;
using YIRSHospital.Services;

namespace YIRSHospital.Views
{
    /// <summary>
    /// Popup for RegisterOutpatient. Hospital code and staff email are handed in
    /// by the caller (Dashboard.xaml.cs) - the agent never types them.
    /// Form, success sheet and failure sheet all live inside this one popup.
    /// </summary>
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class RegisterOutpatientPopup : Popup
    {
        private readonly string _hospitalCode;
        private readonly string _staffEmail;
        private CancellationTokenSource _cts;
        private string _registeredPatientNo;
        private bool _isBusy;

        public RegisterOutpatientPopup(string hospitalCode, string staffEmail)
        {
            InitializeComponent();

            _hospitalCode = hospitalCode;
            _staffEmail = staffEmail;

            // Fit small phones: up to 360 wide, never wider than the screen minus a margin.
            double screenWidth = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
            Size = new Size(Math.Min(360, screenWidth - 32), 520);
        }

        // ── Sheets ───────────────────────────────────────────────────────────

        private void ShowSheet(View sheet)
        {
            FormSheet.IsVisible = ReferenceEquals(sheet, FormSheet);
            SuccessSheet.IsVisible = ReferenceEquals(sheet, SuccessSheet);
            FailureSheet.IsVisible = ReferenceEquals(sheet, FailureSheet);
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            BusyIndicator.IsVisible = busy;
            BusyIndicator.IsRunning = busy;
            RegisterButton.IsEnabled = !busy;
            CancelButton.IsEnabled = !busy;
        }

        private void ShowValidation(string message)
        {
            ValidationLabel.Text = message;
            ValidationLabel.IsVisible = !string.IsNullOrEmpty(message);
        }

        // ── Register ─────────────────────────────────────────────────────────

        private async void OnRegisterClicked(object sender, EventArgs e)
        {
            if (_isBusy) return;

            string fullName = (FullNameEntry.Text ?? string.Empty).Trim();
            string phone = Regex.Replace(PhoneEntry.Text ?? string.Empty, @"[\s-]", string.Empty);
            string gender = GenderPicker.SelectedItem as string;

            if (fullName.Length < 3) { ShowValidation("Enter the patient's full name."); return; }
            if (!Regex.IsMatch(phone, @"^\+?\d{10,14}$")) { ShowValidation("Enter a valid phone number."); return; }
            if (string.IsNullOrWhiteSpace(gender)) { ShowValidation("Select the patient's gender."); return; }
            ShowValidation(null);

            if (Connectivity.NetworkAccess != NetworkAccess.Internet)
            {
                ShowFailure("No internet connection. Check your network and try again.");
                return;
            }

            SetBusy(true);
            try
            {
                _cts?.Cancel();
                _cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

                var result = await HospitalApiService.RegisterOutpatientAsync(new OutpatientRegistrationRequest
                {
                    FullName = fullName,
                    PhoneNumber = phone,
                    Gender = gender,
                    HospitalCode = _hospitalCode,   // passed in from the .cs caller
                    Email = _staffEmail             // passed in from the .cs caller
                }, _cts.Token);

                if (result.Success && result.Data != null && !string.IsNullOrWhiteSpace(result.Data.TempPatientNo))
                    ShowSuccess(result.Data);
                else
                    ShowFailure(result.ErrorMessage ?? "Registration could not be completed.");
            }
            catch (TaskCanceledException)
            {
                ShowFailure("The request timed out. Please check your connection and try again.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[RegisterOutpatient] " + ex);
                ShowFailure("Unexpected error: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ShowSuccess(OutpatientRegistrationResponse data)
        {
            _registeredPatientNo = data.TempPatientNo;

            PatientNoLabel.Text = data.TempPatientNo;
            SuccessMessageLabel.Text = data.Message;
            SuccessNameLabel.Text = string.IsNullOrWhiteSpace(data.FullName) ? "—" : data.FullName;
            SuccessPhoneLabel.Text = string.IsNullOrWhiteSpace(data.PhoneNumber) ? "—" : data.PhoneNumber;
            SuccessHospitalLabel.Text = string.IsNullOrWhiteSpace(data.HospitalName) ? "—" : data.HospitalName;
            SuccessExpiryLabel.Text = data.ExpiresAt.HasValue
                ? data.ExpiresAt.Value.ToLocalTime().ToString("dd MMM yyyy, h:mm tt")
                : "24 hours";

            ShowSheet(SuccessSheet);
        }

        private void ShowFailure(string message)
        {
            FailureMessageLabel.Text = message;
            ShowSheet(FailureSheet);
        }

        // ── Buttons ──────────────────────────────────────────────────────────

        private async void OnCopyPatientNoClicked(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_registeredPatientNo)) return;
                await Clipboard.SetTextAsync(_registeredPatientNo);
                Acr.UserDialogs.UserDialogs.Instance.Toast("Patient ID copied");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[RegisterOutpatient] copy failed: " + ex.Message);
            }
        }

        private void OnRegisterAnotherClicked(object sender, EventArgs e)
        {
            FullNameEntry.Text = string.Empty;
            PhoneEntry.Text = string.Empty;
            GenderPicker.SelectedIndex = -1;
            _registeredPatientNo = null;
            ShowValidation(null);
            ShowSheet(FormSheet);
        }

        private void OnTryAgainClicked(object sender, EventArgs e)
        {
            ShowSheet(FormSheet);   // keeps what the agent already typed
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _cts?.Cancel();
            Dismiss(null);
        }

        private void OnDoneClicked(object sender, EventArgs e)
        {
            Dismiss(_registeredPatientNo);
        }
    }
}