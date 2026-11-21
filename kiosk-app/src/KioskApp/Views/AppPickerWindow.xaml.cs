using System;
using System.Windows;
using System.Windows.Controls;
using KioskApp.Core.Models;
using KioskApp.Services;

namespace KioskApp.Views
{
    public partial class AppPickerWindow : Window
    {
        public AppPickerWindow()
        {
            InitializeComponent();
            App.KeepFullScreen(this);
            Loaded += (_, _) => PopulateApps();
            App.Session.BalanceChanged += OnBalanceChanged;
            Closed += (_, _) => App.Session.BalanceChanged -= OnBalanceChanged;
        }

        private void PopulateApps()
        {
            var user = App.Session.CurrentUser;
            WelcomeText.Text = string.IsNullOrWhiteSpace(user?.DisplayName)
                ? $"שלום, {user?.Username}" : $"שלום, {user!.DisplayName}";
            UpdateBalance(user?.Balance ?? 0);
            var price = App.Session.Config?.PricePerPage ?? 0;
            FooterText.Text = $"הדפסה: {price:0.00} ₪ לעמוד · לעזרה פנו לצוות הספרייה";

            // רק למשתמשים שסומנו בדף הניהול כ"מורשה יציאה"
            ExitToDesktopButton.Visibility = user?.CanExitKiosk == true
                ? Visibility.Visible : Visibility.Collapsed;
            AppsPanel.Children.Clear();

            var apps = App.Session.Config?.AllowedApps ?? new System.Collections.Generic.List<AllowedApp>();
            var editorEnabled = App.Session.Config?.TextEditorEnabled == true;
            if (apps.Count == 0 && !editorEnabled)
            {
                AppsPanel.Children.Add(new TextBlock
                {
                    Text = "לא הוגדרו עדיין תוכנות מאושרות. יש לפנות לצוות הספרייה.",
                    Foreground = (System.Windows.Media.Brush)FindResource("KioskMutedBrush"),
                    FontSize = 18
                });
                return;
            }

            foreach (var app in SortApps(apps))
            {
                var button = MakeTile(app.DisplayName, "IconBook");
                button.Tag = app;
                button.Click += OnAppButtonClick;
                AppsPanel.Children.Add(button);
            }

            if (editorEnabled)
            {
                var editorButton = MakeTile("עורך טקסט", "IconPen", "כתיבה ושמירה לדיסק-און-קי");
                editorButton.Click += (_, _) => TextEditorWindow.ShowEditor("חזרה לבחירת תוכנה");
                AppsPanel.Children.Add(editorButton);
            }
        }

        /// <summary>אריח בעיצוב הקיוסק: סמל בריבוע רך, שם, ושורת הסבר אופציונלית</summary>
        private Button MakeTile(string title, string iconKey, string? subtitle = null)
        {
            var icon = new System.Windows.Shapes.Path
            {
                Data = (System.Windows.Media.Geometry)FindResource(iconKey),
                Style = (Style)FindResource("KioskIcon"),
                Width = 38,
                Height = 38,
                Stroke = (System.Windows.Media.Brush)FindResource("KioskAccentBrush"),
                StrokeThickness = 1.6
            };
            var iconBox = new Border
            {
                Width = 72,
                Height = 72,
                CornerRadius = new CornerRadius(18),
                Background = (System.Windows.Media.Brush)FindResource("KioskSoftBrush"),
                Child = icon,
                Margin = new Thickness(0, 0, 0, 14)
            };
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;

            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(iconBox);
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            if (subtitle != null)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    FontSize = 14,
                    Foreground = (System.Windows.Media.Brush)FindResource("KioskMutedBrush"),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }

            return new Button { Style = (Style)FindResource("KioskTileButton"), Content = panel };
        }

        private void UpdateBalance(decimal balance) => BalanceText.Text = $"יתרה: {balance:0.00} ₪";

        private void OnBalanceChanged(object? sender, decimal balance) => Dispatcher.Invoke(() => UpdateBalance(balance));

        private void OnTopupClick(object sender, RoutedEventArgs e)
        {
            var topup = new TopupWindow { Owner = this };
            topup.ShowDialog();
        }

        private static System.Collections.Generic.List<AllowedApp> SortApps(System.Collections.Generic.List<AllowedApp> apps)
        {
            var copy = new System.Collections.Generic.List<AllowedApp>(apps);
            copy.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            return copy;
        }

        private async void OnAppButtonClick(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var app = (AllowedApp)button.Tag;

            button.IsEnabled = false;
            try
            {
                var process = ProcessLauncher.Launch(app.ExePath);

                var overlay = new OverlayWindow();
                Application.Current.MainWindow = overlay;
                overlay.Initialize(process, this);
                overlay.Show();

                Hide(); // לא Close: אם המשתמש יבחר "החלפת תוכנה" מה-Overlay, נחזור לחלון הזה
            }
            catch (Exception ex)
            {
                App.LogError("כשל בהפעלת תוכנה מהעמדה", ex);
                MessageBox.Show(ex.Message, "שגיאה בהפעלת התוכנה", MessageBoxButton.OK, MessageBoxImage.Error);
                button.IsEnabled = true;
            }
        }

        private void OnExitToDesktopClick(object sender, RoutedEventArgs e)
        {
            if (App.Session.CurrentUser?.CanExitKiosk != true) return;

            var answer = MessageBox.Show(
                "לצאת מהקיוסק למסך הכניסה של Windows?\n\nשם בוחרים את חשבון המנהל ומקלידים את הסיסמה שלו.\nכדי לחזור לקיוסק: להפעיל את המחשב מחדש (או להתנתק מ-Windows).",
                "יציאה למחשב", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            var error = App.SignOutToWindowsSignIn();
            if (error != null)
            {
                MessageBox.Show(error, "יציאה למחשב", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnLogoutClick(object sender, RoutedEventArgs e)
        {
            WindowGuard.ClearKioskBrowserData(); // התחברות לאזור האישי בנדרים פלוס - לא עוברת למשתמש הבא
            TextEditorWindow.EndSession();
            App.StopSessionServices();
            App.Session.End();
            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();
            Close();
        }

        /// <summary>נקרא מה-Overlay כשהמשתמש בוחר "החלפת תוכנה" - חוזרים למסך הזה בלי להתנתק.</summary>
        public void ShowAgain()
        {
            PopulateApps();
            Show();
            Application.Current.MainWindow = this;
            App.BringToFront(this);
        }
    }
}
