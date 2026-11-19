using System;
using System.Windows;
using System.Windows.Input;

namespace KioskApp.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            App.KeepFullScreen(this);
            Loaded += (_, _) => UsernameBox.Focus();

            // שעה ותאריך עברי בפינת המסך
            UpdateClock();
            var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            clock.Tick += (_, _) => UpdateClock();
            clock.Start();
            Closed += (_, _) => clock.Stop();
        }

        private void OnFieldKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OnLoginClick(sender, e);
        }

        private async void OnLoginClick(object sender, RoutedEventArgs e)
        {
            var username = UsernameBox.Text.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowError("יש להזין שם משתמש וסיסמה");
                return;
            }

            LoginButton.IsEnabled = false;
            LoginButton.Content = "מתחבר...";
            HideError();

            try
            {
                var user = await App.ApiClient.LoginAsync(username, password);
                var config = await App.ApiClient.GetConfigAsync();
                App.Session.Start(user, config);
                App.StartSessionServices(); // חיוב על הדפסה - לאורך כל ההתחברות

                var appPicker = new Views.AppPickerWindow();
                Application.Current.MainWindow = appPicker;
                appPicker.Show();
                Close();
            }
            catch (Exception ex)
            {
                App.LogError("כשל בהתחברות", ex);
                ShowError(ex.Message);
                PasswordBox.Clear();
                PasswordBox.Focus();
            }
            finally
            {
                LoginButton.IsEnabled = true;
                LoginButton.Content = "התחברות";
            }
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            ClockText.Text = now.ToString("HH:mm");
            try
            {
                var hebrew = new System.Globalization.CultureInfo("he-IL");
                hebrew.DateTimeFormat.Calendar = new System.Globalization.HebrewCalendar();
                DateText.Text = now.ToString("dddd · d MMMM", hebrew);
            }
            catch
            {
                DateText.Text = now.ToString("dd/MM/yyyy");
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }
    }
}
