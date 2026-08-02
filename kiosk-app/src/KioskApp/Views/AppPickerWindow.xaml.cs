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
            Loaded += (_, _) => PopulateApps();
        }

        private void PopulateApps()
        {
            WelcomeText.Text = $"שלום, {App.Session.CurrentUser?.Username}!";
            AppsPanel.Children.Clear();

            var apps = App.Session.Config?.AllowedApps ?? new System.Collections.Generic.List<AllowedApp>();
            if (apps.Count == 0)
            {
                AppsPanel.Children.Add(new TextBlock
                {
                    Text = "לא הוגדרו עדיין תוכנות מאושרות. יש לפנות לצוות הספרייה.",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 16
                });
                return;
            }

            foreach (var app in SortApps(apps))
            {
                var button = new Button
                {
                    Content = app.DisplayName,
                    Width = 220,
                    Height = 140,
                    Margin = new Thickness(15),
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Background = System.Windows.Media.Brushes.White,
                    Foreground = (System.Windows.Media.Brush)FindResource("KioskPrimaryBrush"),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = app
                };
                button.Click += OnAppButtonClick;
                AppsPanel.Children.Add(button);
            }
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

        private void OnLogoutClick(object sender, RoutedEventArgs e)
        {
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
        }
    }
}
