using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KioskApp.Core;

namespace KioskApp.Views
{
    /// <summary>
    /// "פרטי החשבון" (מהלשונית): המשתמש המחובר משנה שם, טלפון וסיסמה. תעודת הזהות מוצגת ולא
    /// ניתנת לשינוי. כל שמירה דורשת את הסיסמה הנוכחית (ראו library-station-account-update).
    /// </summary>
    public class AccountDetailsWindow : Window
    {
        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        private readonly TextBox _name;
        private readonly TextBox _phone;
        private readonly PasswordBox _newPassword;
        private readonly PasswordBox _confirmPassword;
        private readonly PasswordBox _currentPassword;
        private readonly TextBlock _error;
        private readonly Button _save;

        public AccountDetailsWindow()
        {
            var user = App.Session.CurrentUser!;

            Title = "פרטי החשבון";
            FlowDirection = FlowDirection.RightToLeft;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            FontFamily = Res<FontFamily>("KioskBodyFont");
            Foreground = Res<Brush>("KioskTextBrush");

            var panel = new StackPanel { Width = 440 };
            panel.Children.Add(new TextBlock
            {
                Text = "פרטי החשבון",
                FontFamily = Res<FontFamily>("KioskHeadingFont"),
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            });

            // תעודת זהות - לתצוגה בלבד
            panel.Children.Add(Label("תעודת זהות"));
            panel.Children.Add(new Border
            {
                Background = Res<Brush>("KioskSoftBrush"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 14),
                Child = new TextBlock { Text = $"{user.Username}  (לא ניתנת לשינוי)", FontSize = 16 }
            });

            panel.Children.Add(Label("שם מלא"));
            _name = new TextBox { Style = Res<Style>("KioskTextBox"), Text = user.DisplayName ?? "", MaxLength = 60, Margin = new Thickness(0, 0, 0, 14) };
            panel.Children.Add(_name);

            panel.Children.Add(Label("טלפון"));
            _phone = new TextBox { Style = Res<Style>("KioskTextBox"), Text = user.Phone ?? "", MaxLength = 15, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 0, 0, 14) };
            panel.Children.Add(_phone);

            panel.Children.Add(Label("סיסמה חדשה (4-8 ספרות) - להשאיר ריק אם לא משנים"));
            _newPassword = new PasswordBox { Style = Res<Style>("KioskPasswordBox"), MaxLength = 8, Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(_newPassword);
            panel.Children.Add(Label("הסיסמה החדשה שוב"));
            _confirmPassword = new PasswordBox { Style = Res<Style>("KioskPasswordBox"), MaxLength = 8, Margin = new Thickness(0, 0, 0, 18) };
            panel.Children.Add(_confirmPassword);

            panel.Children.Add(new Border
            {
                BorderBrush = Res<Brush>("KioskBorderBrush"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 0, 0, 14)
            });
            panel.Children.Add(Label("הסיסמה הנוכחית (חובה לאישור השינויים)"));
            _currentPassword = new PasswordBox { Style = Res<Style>("KioskPasswordBox"), MaxLength = 20 };
            _currentPassword.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnSave(); };
            panel.Children.Add(_currentPassword);

            _error = new TextBlock
            {
                Foreground = Res<Brush>("KioskDangerBrush"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0),
                Visibility = Visibility.Collapsed
            };
            panel.Children.Add(_error);

            _save = new Button { Content = "שמירה", Style = Res<Style>("KioskPrimaryButton"), MinHeight = 48, MinWidth = 150, FontSize = 17, Margin = new Thickness(6, 0, 6, 0) };
            _save.Click += (_, _) => OnSave();
            var cancel = new Button { Content = "ביטול", Style = Res<Style>("KioskSecondaryButton"), MinHeight = 48, MinWidth = 130, FontSize = 17, Margin = new Thickness(6, 0, 6, 0), IsCancel = true };
            cancel.Click += (_, _) => Close();
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 0),
                Children = { _save, cancel }
            });

            Content = new Border
            {
                Background = Res<Brush>("KioskCardBrush"),
                BorderBrush = Res<Brush>("KioskTanBrush"),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(30, 24, 30, 24),
                Margin = new Thickness(24),
                Child = panel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 36,
                    ShadowDepth = 8,
                    Opacity = 0.22,
                    Color = Color.FromRgb(0x3E, 0x2F, 0x23)
                }
            };

            Loaded += (_, _) =>
            {
                var area = SystemParameters.WorkArea;
                Left = area.Left + (area.Width - ActualWidth) / 2;
                Top = area.Top + (area.Height - ActualHeight) / 2;
                _name.Focus();
            };
        }

        private static TextBlock Label(string text) => new()
        {
            Text = text,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };

        private async void OnSave()
        {
            _error.Visibility = Visibility.Collapsed;
            var name = _name.Text.Trim();
            var phone = _phone.Text.Trim();
            var newPassword = _newPassword.Password;

            if (name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2) { ShowError("יש להזין שם פרטי ושם משפחה"); return; }
            if (newPassword.Length > 0)
            {
                if (newPassword.Length < 4 || newPassword.Length > 8 || !newPassword.All(char.IsDigit)) { ShowError("הסיסמה החדשה צריכה להיות בין 4 ל-8 ספרות (ספרות בלבד)"); return; }
                if (newPassword != _confirmPassword.Password) { ShowError("הסיסמה החדשה והאימות שלה לא זהים"); return; }
            }
            if (_currentPassword.Password.Length == 0) { ShowError("יש להקליד את הסיסמה הנוכחית"); _currentPassword.Focus(); return; }

            _save.IsEnabled = false;
            _save.Content = "שומר...";
            try
            {
                var user = await App.ApiClient.UpdateAccountAsync(
                    App.Session.CurrentUser!.Id, _currentPassword.Password, name, phone,
                    newPassword.Length > 0 ? newPassword : null);
                App.Session.UpdateDetails(user.DisplayName, user.Phone);
                App.LogInfo("פרטי החשבון עודכנו" + (newPassword.Length > 0 ? " (כולל סיסמה)" : ""));
                await KioskMessageDialog.ShowTimedAsync(this, KioskMessageKind.Success, "הפרטים נשמרו",
                    newPassword.Length > 0 ? "הפרטים והסיסמה החדשה נשמרו." : "הפרטים נשמרו.", 2);
                Close();
            }
            catch (KioskAuthException)
            {
                ShowError("הסיסמה הנוכחית שגויה");
                _currentPassword.Clear();
                _currentPassword.Focus();
            }
            catch (Exception ex)
            {
                App.LogError("פרטי החשבון: השמירה נכשלה", ex);
                ShowError(ex is KioskApiException ? ex.Message : "לא ניתן לשמור כרגע (אין חיבור לשרת). אפשר לנסות שוב בעוד רגע.");
            }
            finally
            {
                _save.IsEnabled = true;
                _save.Content = "שמירה";
            }
        }

        private void ShowError(string message)
        {
            _error.Text = message;
            _error.Visibility = Visibility.Visible;
        }
    }
}
