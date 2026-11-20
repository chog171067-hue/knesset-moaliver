using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace KioskApp.Views
{
    /// <summary>
    /// "להצטרפות ל'חבר קהילה'": דף נדרים פלוס להקמת הוראת קבע, בכרטיס בעיצוב הקיוסק.
    /// זה לא דפדפן חופשי - מותר לגלוש רק לכתובות של נדרים פלוס (ושל הקישור עצמו);
    /// כל כתובת אחרת נחסמת ונרשמת ביומן. ביציאה נמחקים נתוני הגלישה (עוגיות וכו'),
    /// כדי שהמשתמש הבא לא ימצא את הפרטים של הקודם.
    ///
    /// סגירה אוטומטית: בזמן שהחלון פתוח בודקים מול השרת (checkJoined) אם הוקמה הוראת קבע
    /// חדשה - כל כמה שניות, ומיד כשבדף מופיעה הודעת הצלחה. ברגע שנמצאה - הודעת הצלחה
    /// קופצת והחלון נסגר לבד (Joined = true). X בפינה סוגר בלי להצטרף.
    /// </summary>
    public class JoinCommunityWindow : Window
    {
        private static readonly string[] AllowedHostSuffixes = { "nedar.im", "matara.pro", "nedarimplus.co.il", "nedarim.org.il" };
        // מילים שמופיעות בדף אחרי שהוראת הקבע נקלטה - רק מקדימות בדיקה מול השרת, לא קובעות לבד
        private static readonly string[] SuccessHints = { "בהצלחה", "תודה", "נקלט", "התקבל" };

        private readonly string _startUrl;
        private readonly string _startHost;
        private readonly Func<Task<bool>> _checkJoined;
        private readonly WebView2 _webView = new();
        private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(12) };
        private readonly DispatcherTimer _pageTextTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly DateTime _openedAt = DateTime.Now;
        private bool _checking;
        private bool _closing;

        /// <summary>true אם נמצאה הוראת קבע חדשה והחלון נסגר בגללה</summary>
        public bool Joined { get; private set; }

        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        public JoinCommunityWindow(string url, Func<Task<bool>> checkJoined)
        {
            _startUrl = url;
            _startHost = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : "";
            _checkJoined = checkJoined;

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

            // ===== כותרת: טקסט מימין, X בפינה השמאלית =====
            var header = new Grid { Margin = new Thickness(4, 0, 0, 16) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            titles.Children.Add(new TextBlock
            {
                Text = "הצטרפות לחבר קהילה",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                FontFamily = Res<FontFamily>("KioskHeadingFont"),
                TextWrapping = TextWrapping.Wrap
            });
            titles.Children.Add(new TextBlock
            {
                Text = "ממלאים את הטופס להקמת הוראת קבע. בסיום ההצטרפות החלון ייסגר לבד ותחזרו לפתיחת החשבון.",
                FontSize = 15,
                Foreground = Res<Brush>("KioskMutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetColumn(titles, 0);
            header.Children.Add(titles);

            var closeButton = MakeCloseButton();
            Grid.SetColumn(closeButton, 1);
            header.Children.Add(closeButton);

            // ===== הדף של נדרים פלוס, במסגרת =====
            _webView.FlowDirection = FlowDirection.LeftToRight; // הדף מנהל את הכיוון שלו
            var pageFrame = new Border
            {
                BorderBrush = Res<Brush>("KioskOutlineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(3),
                Background = Brushes.White,
                Child = _webView
            };

            var cardContent = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            cardContent.Children.Add(header);
            cardContent.Children.Add(pageFrame);

            // כרטיס שמנת במרכז המסך. (חלון שקוף עם רקע מוחשך לא אפשרי כאן: הדפדפן הפנימי
            // לא מוצג בחלון שקוף - לכן רקע המסך הוא צבע הרקע של הקיוסק)
            Content = new Border
            {
                Background = Res<Brush>("KioskCardBrush"),
                BorderBrush = Res<Brush>("KioskBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(28, 22, 28, 26),
                Margin = new Thickness(40, 30, 40, 30),
                MaxWidth = 1000,
                Child = cardContent,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 40,
                    ShadowDepth = 8,
                    Opacity = 0.14,
                    Color = Color.FromRgb(0x5B, 0x46, 0x36)
                }
            };

            _pollTimer.Tick += async (_, _) => await CheckJoinedAsync();
            _pageTextTimer.Tick += async (_, _) => await CheckPageTextAsync();
            Loaded += async (_, _) => await InitAsync();
            Closed += (_, _) => { _pollTimer.Stop(); _pageTextTimer.Stop(); };
        }

        private Button MakeCloseButton()
        {
            var x = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M6,6 L18,18 M18,6 L6,18"),
                Style = Res<Style>("KioskIcon"),
                Width = 16,
                Height = 16,
                StrokeThickness = 2.2,
                Stroke = Res<Brush>("KioskSecondaryTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var bd = new FrameworkElementFactory(typeof(Border), "Bd");
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(22));
            bd.SetValue(Border.BackgroundProperty, Res<Brush>("KioskSoftBrush"));
            bd.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            var template = new ControlTemplate(typeof(Button)) { VisualTree = bd };
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Res<Brush>("KioskBorderBrush"), "Bd"));
            template.Triggers.Add(hover);

            var button = new Button
            {
                Width = 44,
                Height = 44,
                Content = x,
                Template = template,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "סגירה בלי להצטרף",
                VerticalAlignment = VerticalAlignment.Top
            };
            button.Click += async (_, _) => await CloseAndClearAsync();
            return button;
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
                _pollTimer.Start();
                _pageTextTimer.Start();
            }
            catch (Exception ex)
            {
                App.LogError("הצטרפות לחבר קהילה: כשל בפתיחת הדף", ex);
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "לא ניתן לפתוח את דף ההצטרפות",
                    "אפשר להמשיך בפתיחת החשבון ולהצטרף בהמשך.");
                Close();
            }
        }

        // הודעת הצלחה בדף (גם בתוך מסגרות פנימיות של הדף) - מקדימה את הבדיקה מול השרת
        private async Task CheckPageTextAsync()
        {
            if (_closing || _webView.CoreWebView2 == null) return;
            try
            {
                var json = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "(function(){var t=document.body?document.body.innerText:'';" +
                    "for(var i=0;i<window.frames.length;i++){try{t+=' '+window.frames[i].document.body.innerText}catch(e){}}" +
                    "return t;})()");
                var text = JsonSerializer.Deserialize<string>(json) ?? "";
                if (SuccessHints.Any(h => text.Contains(h))) await CheckJoinedAsync();
            }
            catch
            {
                // דף בטעינה וכו' - ננסה בפעם הבאה
            }
        }

        private async Task CheckJoinedAsync()
        {
            if (_checking || _closing) return;
            // ההוראה לא תופיע לפני שהמשתמש הספיק למלא את הטופס - חוסכים בדיקות מיותרות
            if (DateTime.Now - _openedAt < TimeSpan.FromSeconds(15)) return;
            // אחרי חצי שעה (החלון נשכח פתוח) - מפסיקים לבדוק
            if (DateTime.Now - _openedAt > TimeSpan.FromMinutes(30)) { _pollTimer.Stop(); _pageTextTimer.Stop(); return; }

            _checking = true;
            try
            {
                if (!await _checkJoined()) return;
                Joined = true;
                _pollTimer.Stop();
                _pageTextTimer.Stop();
                App.LogInfo("הצטרפות לחבר קהילה: הוראת הקבע נמצאה - סוגר את הדף");
                await KioskMessageDialog.ShowTimedAsync(this, KioskMessageKind.Success,
                    "ההצטרפות נקלטה!", "החשבון יוגדר כחבר קהילה. חוזרים לפתיחת החשבון...", 2.5);
                await CloseAndClearAsync();
            }
            catch (Exception ex)
            {
                App.LogError("הצטרפות לחבר קהילה: כשל בבדיקה מול השרת", ex);
            }
            finally
            {
                _checking = false;
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
            _pollTimer.Stop();
            _pageTextTimer.Stop();
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
