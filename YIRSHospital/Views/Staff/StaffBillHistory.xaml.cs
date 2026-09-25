using Acr.UserDialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRSHospital.Models;
using YIRSHospital.Services;

namespace YIRSHospital.Views.Staff
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class StaffBillHistory : ContentPage
    {
        private ObservableCollection<StaffBillGroup> _billsList = new ObservableCollection<StaffBillGroup>();
        private bool _isLoaded = false;
        public StaffBillHistory()
        {
            InitializeComponent();
            BillsCollectionView.ItemsSource = _billsList;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (!_isLoaded)
            {
                await FetchHistoryDataAsync();
            }
        }

        private async void OnRefreshTriggered(object sender, EventArgs e)
        {
            await FetchHistoryDataAsync();
            HistoryRefreshView.IsRefreshing = false;
        }

        private async Task FetchHistoryDataAsync()
        {
            try
            {
                var email = LoginPage.ValidUserMail;
                if (string.IsNullOrWhiteSpace(email))
                {
                    await DisplayAlert("Session Notice", "Agent email not found. Please log in again.", "OK");
                    return;
                }

                UserDialogs.Instance.ShowLoading("Loading history...");

                var code = HospitalContext.Code ?? "DEFAULT";
                var result = await HospitalApiService.GetStaffBillHistoryAsync(email, code);

                UserDialogs.Instance.HideLoading();

                if (result.Success && result.Data?.Code == "00")
                {
                    UpdateSummaryUI(result.Data.Summary);

                    _billsList.Clear();
                    if (result.Data.BillGroups != null)
                    {
                        foreach (var bill in result.Data.BillGroups)
                        {
                            _billsList.Add(bill);
                        }
                    }
                    _isLoaded = true;
                }
                else
                {
                    await DisplayAlert("Notice", result.Data?.Message ?? result.ErrorMessage ?? "Could not load history.", "OK");
                    ClearUI();
                }
            }
            catch (Exception ex)
            {
                UserDialogs.Instance.HideLoading();
                System.Diagnostics.Debug.WriteLine($"[StaffHistoryPage] Critical Error: {ex}");
                await DisplayAlert("Error", "An unexpected error occurred while fetching your history. Please try again.", "OK");
                ClearUI();
            }
        }

        private void UpdateSummaryUI(StaffBillSummary summary)
        {
            if (summary == null) return;

            Device.BeginInvokeOnMainThread(() =>
            {
                TotalBillsLabel.Text = summary.TotalBills.ToString();
                TotalPendingLabel.Text = summary.TotalPending.ToString();
                TotalPaidLabel.Text = summary.TotalPaid.ToString();
                GrandTotalLabel.Text = $"₦{summary.GrandTotal:N2}";
            });
        }

        private void ClearUI()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                TotalBillsLabel.Text = "0";
                TotalPendingLabel.Text = "0";
                TotalPaidLabel.Text = "0";
                GrandTotalLabel.Text = "₦0.00";
                _billsList.Clear();
            });
        }
    }
}