using System;
using System.Windows;
using System.Windows.Input;
using KioskApp.Core;
using KioskApp.Core.Models;

namespace KioskApp.Views
{
    public partial class LoginWindow : Window
    {
        // שם המשתמש והסיסמה הזמנית - נשמרים בזיכרון רק בזמן מסך "בחירת סיסמה חדשה"
        private string _pendingUsername = "";
        private string _pendingTempPassword = "";

        public LoginWindow()
        {
            InitializeComponent();
            App.KeepFullScreen(this);
            Loaded += (_, _) => UsernameBox.Focus();

            var resetPhone = App.Config?.PasswordResetPhone?.Trim();
            if (!string.IsNullOrEmpty(resetPhone))
            {
                ForgotPasswordHint.Text = $"שכחתם סיסמה? התקשרו מהטלפון הרשום בחשבון למספר {resetPhone} וקבלו סיסמה זמנית";
                ForgotPasswordHint.Visibility = Visibility.Visible;
            }
        }

        private void OnFieldKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OnLoginClick(sender, e);
        }

        private void OnChangeFieldKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OnChangePasswordClick(sender, e);
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
                await StartSessionAsync(user);
            }
            catch (PasswordChangeRequiredException)
            {
                // נכנסו עם סיסמה זמנית מהטלפון: אסור להמשיך לפני בחירת סיסמה חדשה
                ShowChangePasswordPanel(username, password);
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

        private async void OnChangePasswordClick(object sender, RoutedEventArgs e)
        {
            var newPassword = NewPasswordBox.Password;
            var confirm = ConfirmPasswordBox.Password;

            if (newPassword.Length < 4)
            {
                ShowError("הסיסמה החדשה חייבת להכיל לפחות 4 תווים");
                return;
            }
            if (newPassword != confirm)
            {
                ShowError("שתי הסיסמאות שהוקלדו אינן זהות");
                ConfirmPasswordBox.Clear();
                ConfirmPasswordBox.Focus();
                return;
            }

            ChangePasswordButton.IsEnabled = false;
            ChangePasswordButton.Content = "שומר...";
            HideError();

            KioskUser user;
            try
            {
                user = await App.ApiClient.ChangePasswordAsync(_pendingUsername, _pendingTempPassword, newPassword);
                App.LogInfo($"סיסמה הוחלפה אחרי כניסה עם סיסמה זמנית: {_pendingUsername}");
            }
            catch (KioskAuthException ex)
            {
                // הסיסמה הזמנית פגה בינתיים או בוטלה - חוזרים למסך הכניסה
                App.LogError("כשל בהחלפת סיסמה", ex);
                ShowLoginPanel();
                ShowError(ex.Message);
                return;
            }
            catch (Exception ex)
            {
                App.LogError("כשל בהחלפת סיסמה", ex);
                ShowError(ex.Message);
                return;
            }
            finally
            {
                ChangePasswordButton.IsEnabled = true;
                ChangePasswordButton.Content = "שמירה וכניסה";
            }

            // הסיסמה כבר הוחלפה בשרת. אם פתיחת הקיוסק נכשלה (למשל תקלת רשת) - חוזרים
            // למסך הכניסה, כי הסיסמה הזמנית כבר לא בתוקף והכניסה הבאה היא עם החדשה.
            ClearPending();
            try
            {
                await StartSessionAsync(user);
            }
            catch (Exception ex)
            {
                App.LogError("כשל בפתיחת הקיוסק אחרי החלפת סיסמה", ex);
                ShowLoginPanel();
                ShowError("הסיסמה החדשה נשמרה. נא להתחבר שוב עם הסיסמה החדשה.");
            }
        }

        private void OnCancelChangeClick(object sender, RoutedEventArgs e)
        {
            ShowLoginPanel();
        }

        private async System.Threading.Tasks.Task StartSessionAsync(KioskUser user)
        {
            var config = await App.ApiClient.GetConfigAsync();
            App.Session.Start(user, config);
            App.StartSessionServices(); // חיוב על הדפסה - לאורך כל ההתחברות

            var appPicker = new Views.AppPickerWindow();
            Application.Current.MainWindow = appPicker;
            appPicker.Show();
            Close();
        }

        private void ShowChangePasswordPanel(string username, string tempPassword)
        {
            _pendingUsername = username;
            _pendingTempPassword = tempPassword;
            HideError();
            LoginPanel.Visibility = Visibility.Collapsed;
            ChangePasswordPanel.Visibility = Visibility.Visible;
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            NewPasswordBox.Focus();
        }

        private void ShowLoginPanel()
        {
            ClearPending();
            HideError();
            ChangePasswordPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            PasswordBox.Clear();
            PasswordBox.Focus();
        }

        private void ClearPending()
        {
            _pendingUsername = "";
            _pendingTempPassword = "";
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
