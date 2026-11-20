using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace KioskApp.Views
{
    /// <summary>
    /// "להצטרפות ל'חבר קהילה'": דף נדרים פלוס להקמת הוראת קבע, בחלון מלא בתוך העמדה.
    /// זה לא דפדפן חופשי - מותר לגלוש רק לכתובות של נדרים פלוס (ושל הקישור עצמו);
    /// כל כתובת אחרת נחסמת ונרשמת ביומן. ביציאה נמחקים נתוני הגלישה (עוגיות וכו'),
    /// כדי שהמשתמש הבא לא ימצא את הפרטים של הקודם.
    /// האם ההצטרפות הצליחה - לא נקבע כאן אלא מול השרת (הוראת הקבע ברשימת המוסד).
    /// </summary>
    public class JoinCommunityWindow : Window
    {
        private static readonly string[] AllowedHostSuffixes = { "nedar.im", "matara.pro", "nedarimplus.co.il", "nedarim.org.il" };

        private readonly string _startUrl;
        private readonly string _startHost;
        private readonly WebView2 _webView = new();
        private readonly Button _doneButton;
        private bool _closing;

        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        public JoinCommunityWindow(string url)
        {
            _startUrl = url;
            _startHost = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : "";

            Title = "הצטרפות לחבר קהילה";
            FlowDirection = FlowDirection.RightToLeft;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            FontFamily = Res<FontFamily>("KioskBodyFont");
            Foreground = Res<Brush>("KioskTextBrush");
            Background = Res<Brush>("KioskGroundBrush");

            _doneButton = new Button
            {
                Content = "סיימתי - חזרה לפתיחת החשבון",
                Style = Res<Style>("KioskPrimaryButton"),
                MinHeight = 44,
                FontSize = 17,
                VerticalAlignment = VerticalAlignment.Center
            };
            _doneButton.Click += async (_, _) => await CloseAndClearAsync();

            var header = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_doneButton, Dock.Left);
            header.Children.Add(_doneButton);
            header.Children.Add(new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = "הצטרפות לחבר קהילה", FontSize = 22, FontWeight = FontWeights.Bold, FontFamily = Res<FontFamily>("KioskHeadingFont") },
                    new TextBlock
                    {
                        Text = "ממלאים את הטופס של נדרים פלוס להקמת הוראת קבע, ובסיום לוחצים \"סיימתי\".",
                        FontSize = 14,
                        Foreground = Res<Brush>("KioskMutedBrush"),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            });

            var headerBar = new Border
            {
                Background = Res<Brush>("KioskCardBrush"),
                BorderBrush = Res<Brush>("KioskBorderBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(24, 12, 24, 12),
                Child = header
            };

            var root = new DockPanel();
            DockPanel.SetDock(headerBar, Dock.Top);
            root.Children.Add(headerBar);
            // דף האינטרנט עצמו - בכיוון רגיל (הדף מנהל את הכיוון שלו)
            _webView.FlowDirection = FlowDirection.LeftToRight;
            root.Children.Add(_webView);
            Content = root;

            Loaded += async (_, _) => await InitAsync();
        }

        private async Task InitAsync()
        {
            try
            {
                // אותה תיקיית נתונים כמו חלון ההטענה (תיקיית הקיוסק עצמה היא קריאה בלבד)
                var webViewData = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KioskApp", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, webViewData);
                await _webView.EnsureCoreWebView2Async(env);

                var core = _webView.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.NavigationStarting += OnNavigationStarting;
                core.NewWindowRequested += OnNewWindowRequested;
                core.Navigate(_startUrl);
                App.LogInfo("הצטרפות לחבר קהילה: נפתח דף נדרים פלוס");
            }
            catch (Exception ex)
            {
                App.LogError("הצטרפות לחבר קהילה: כשל בפתיחת הדף", ex);
                MessageBox.Show(this, "לא ניתן לפתוח כרגע את דף ההצטרפות. אפשר להמשיך בפתיחת החשבון ולהצטרף בהמשך.",
                    "הצטרפות לחבר קהילה", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
            }
        }

        private bool IsAllowed(string url)
        {
            if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return true;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;
            var host = uri.Host.ToLowerInvariant();
            if (host == _startHost) return true;
            return AllowedHostSuffixes.Any(s => host == s || host.EndsWith("." + s));
        }

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (IsAllowed(e.Uri)) return;
            e.Cancel = true;
            App.LogInfo($"הצטרפות לחבר קהילה: נחסמה גלישה לכתובת שאינה של נדרים פלוס: {e.Uri}");
        }

        // קישורים שנפתחים בחלון חדש - נפתחים באותו חלון (אם מותרים), לא בחלון דפדפן נפרד
        private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (IsAllowed(e.Uri)) _webView.CoreWebView2?.Navigate(e.Uri);
            else App.LogInfo($"הצטרפות לחבר קהילה: נחסם חלון חדש לכתובת: {e.Uri}");
        }

        private async Task CloseAndClearAsync()
        {
            if (_closing) return;
            _closing = true;
            _doneButton.IsEnabled = false;
            try
            {
                if (_webView.CoreWebView2 != null)
                {
                    await _webView.CoreWebView2.Profile.ClearBrowsingDataAsync();
                }
            }
            catch (Exception ex)
            {
                App.LogError("הצטרפות לחבר קהילה: כשל במחיקת נתוני הגלישה", ex);
            }
            Close();
        }
    }
}
