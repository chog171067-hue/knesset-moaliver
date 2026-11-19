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
            Loaded += async (_, _) => await LoadForgotPasswordButtonAsync();
        }

        // הכפתור "שכחתי סיסמה" מוצג רק אם בדף הניהול הוגדר מספר לשחזור. תקלה כאן
        // (למשל אין אינטרנט) רק משאירה את הכפתור מוסתר - לא חוסמת את הכניסה.
        private async System.Threading.Tasks.Task LoadForgotPasswordButtonAsync()
        {
            try
            {
                var config = await App.ApiClient.GetConfigAsync();
                if (!string.IsNullOrWhiteSpace(config.PasswordResetPhone))
                {
                    ForgotPasswordButton.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                App.LogError("כשל בטעינת הגדרת שחזור הסיסמה", ex);
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

        private async void OnForgotPasswordClick(object sender, RoutedEventArgs e)
        {
            var username = UsernameBox.Text.Trim();
            if (string.IsNullOrEmpty(username))
            {
                ShowError("כדי לשחזר סיסמה, הקלידו קודם את תעודת הזהות בשדה \"שם משתמש\" ולחצו שוב על \"שכחתי סיסמה\"");
                UsernameBox.Focus();
                return;
            }

            ForgotPasswordButton.IsEnabled = false;
            HideError();
            try
            {
                var info = await App.ApiClient.GetPasswordResetInfoAsync(username);
                if (string.IsNullOrWhiteSpace(info.ResetPhone))
                {
                    ShowError("שחזור סיסמה בטלפון אינו זמין כרגע. יש לפנות לצוות הספרייה.");
                    return;
                }
                if (!info.Found)
                {
                    ShowError("לא נמצא חשבון פעיל עם תעודת הזהות שהוקלדה. יש לבדוק את המספר, או לפנות לצוות הספרייה.");
                    return;
                }

                ResetPhoneText.Text = info.ResetPhone;
                MaskedPhoneText.Text = info.MaskedPhone;
                ForgotInstructionsText.Text =
                    "במערכת הטלפונית תתבקשו להקיש את תעודת הזהות, ותשמעו סיסמה זמנית. " +
                    "היכנסו איתה כאן, ותתבקשו לבחור סיסמה חדשה. " +
                    "אם המספר הזה כבר לא שלכם, יש לפנות לצוות הספרייה.";

                LoginPanel.Visibility = Visibility.Collapsed;
                ForgotPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                App.LogError("כשל בטעינת פרטי שחזור הסיסמה", ex);
                ShowError(ex.Message);
            }
            finally
            {
                ForgotPasswordButton.IsEnabled = true;
            }
        }

        private void OnBackToLoginClick(object sender, RoutedEventArgs e)
        {
            ForgotPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;
            HideError();
            PasswordBox.Clear();
            PasswordBox.Focus();
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
            ForgotPanel.Visibility = Visibility.Collapsed;
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
