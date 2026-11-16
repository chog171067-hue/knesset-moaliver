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
            Loaded += (_, _) => UsernameBox.Focus();
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
