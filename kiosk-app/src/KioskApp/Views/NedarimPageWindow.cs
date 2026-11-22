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
    /// דף של נדרים פלוס בכרטיס בעיצוב הקיוסק - משמש ל"הצטרפות לחבר קהילה" (הוראת קבע)
    /// ול"תשלום בכרטיס שמור" (האזור האישי בנדרים). זה לא דפדפן חופשי - מותר לגלוש רק
    /// לכתובות של נדרים פלוס (ושל הקישור עצמו); כל כתובת אחרת נחסמת ונרשמת ביומן.
    /// ביציאה נמחקים נתוני הגלישה (עוגיות, התחברות לאזור האישי), כדי שהמשתמש הבא לא
    /// ימצא את עצמו מחובר לאזור האישי של הקודם.
    ///
    /// סגירה אוטומטית: בזמן שהחלון פתוח בודקים מול השרת (isDone) אם הפעולה הסתיימה -
    /// כל כמה שניות, מיד כשבדף מופיעה הודעת הצלחה, ומיד כשנדרים פלוס מעבירים לדף החזרה
    /// שלנו (returnUrlPrefix). ברגע שהסתיימה - החלון נסגר לבד (Completed = true),
    /// אופציונלית עם הודעת הצלחה. X בפינה סוגר בלי לסיים.
    /// </summary>
    public class NedarimPageWindow : Window
    {
        private static readonly string[] AllowedHostSuffixes = { "nedar.im", "matara.pro", "nedarimplus.co.il", "nedarim.org.il" };
        // מילים שמופיעות בדף אחרי שהוראת הקבע נקלטה - רק מקדימות בדיקה מול השרת, לא קובעות לבד
        private static readonly string[] SuccessHints = { "בהצלחה", "תודה", "נקלט", "התקבל" };

        private readonly string _startUrl;
        private readonly string _startHost;
        private readonly Func<Task<bool>> _isDone;
        private readonly string? _returnUrlPrefix;
        private readonly string? _successTitle;
        private readonly string? _successMessage;
        private readonly TimeSpan _firstCheckAfter;
        private readonly WebView2 _webView = new();
        private readonly DispatcherTimer _pollTimer;
        private readonly DispatcherTimer _pageTextTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        // מצב "כניסה לאזור האישי" (ראו TryOpenDonorLoginAsync): הדף מוצג רגיל (דף מוסתר
        // לא מסיים להציג את עצמו בדפדפן הפנימי), ושורת הודעה מעליו. כשמופיע "אתה מוכר: לחץ
        // כאן" - לוחצים בשביל המשתמש. לא הופיע - הדף נשאר פתוח לתשלום רגיל בכרטיס.
        private readonly bool _donorLoginOnly;
        private readonly DispatcherTimer _loginClickTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
        private readonly Border? _checkingPanel;
        private readonly TextBlock? _checkingText;
        private DateTime? _pageLoadedAt;
        // מסגרות פנימיות בדף (iframe) - גם בהן מחפשים את "אתה מוכר"
        private readonly System.Collections.Generic.List<CoreWebView2Frame> _frames = new();
        // אחרי שהדף נטען - כמה זמן לחכות ל"אתה מוכר" לפני שמחליטים שלא זוהה אזור אישי
        private static readonly TimeSpan DonorDetectWait = TimeSpan.FromSeconds(15);
        // אם הדף בכלל לא נטען תוך הזמן הזה - תקלת חיבור
        private static readonly TimeSpan PageLoadTimeout = TimeSpan.FromSeconds(30);

        /// <summary>מצב "כניסה לאזור האישי בלבד": הדף של נדרים פלוס לא נטען</summary>
        public bool PageLoadFailed { get; private set; }
        private readonly DateTime _openedAt = DateTime.Now;
        private bool _checking;
        private DateTime _lastCheck = DateTime.MinValue;
        private bool _closing;

        /// <summary>true אם הפעולה הסתיימה (לפי השרת) והחלון נסגר בגלל זה</summary>
        public bool Completed { get; private set; }

        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        /// <param name="isDone">בדיקה מול השרת: האם הפעולה הסתיימה</param>
        /// <param name="pollSeconds">כל כמה שניות לבדוק</param>
        /// <param name="firstCheckAfterSeconds">לא בודקים לפני שהמשתמש הספיק למלא את הטופס</param>
        /// <param name="returnUrlPrefix">דף החזרה (Redirect) של נדרים פלוס - מעבר אליו = בדיקה מיידית</param>
        /// <param name="successTitle">הודעת הצלחה קופצת לפני הסגירה (null = בלי)</param>
        public NedarimPageWindow(string title, string subtitle, string url, Func<Task<bool>> isDone,
            int pollSeconds, int firstCheckAfterSeconds, string? returnUrlPrefix = null,
            string? successTitle = null, string? successMessage = null, bool donorLoginOnly = false)
        {
            _donorLoginOnly = donorLoginOnly;
            _startUrl = url;
            _startHost = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : "";
            _isDone = isDone;
            _returnUrlPrefix = returnUrlPrefix;
            _successTitle = successTitle;
            _successMessage = successMessage;
            _firstCheckAfter = TimeSpan.FromSeconds(firstCheckAfterSeconds);
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(pollSeconds) };

            Title = title;
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
                Text = title,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                FontFamily = Res<FontFamily>("KioskHeadingFont"),
                TextWrapping = TextWrapping.Wrap
            });
            titles.Children.Add(new TextBlock
            {
                Text = subtitle,
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
            var pageArea = new DockPanel();
            if (_donorLoginOnly)
            {
                // שורת הודעה מעל הדף (לא מעליו בשכבה - הדפדפן הפנימי תמיד "מעל" כל דבר אחר)
                _checkingText = new TextBlock
                {
                    Text = "בודקים אם יש לך אזור אישי בנדרים פלוס... (כמה שניות)",
                    FontSize = 17,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Res<Brush>("KioskOnBrandBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                };
                _checkingPanel = new Border
                {
                    Background = Res<Brush>("KioskBrandBrush"),
                    CornerRadius = new CornerRadius(10, 10, 0, 0),
                    Padding = new Thickness(14, 10, 14, 10),
                    Child = _checkingText
                };
                DockPanel.SetDock(_checkingPanel, Dock.Top);
                pageArea.Children.Add(_checkingPanel);
            }
            pageArea.Children.Add(_webView);

            var pageFrame = new Border
            {
                BorderBrush = Res<Brush>("KioskOutlineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(3),
                Background = Brushes.White,
                Child = pageArea
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
                BorderBrush = Res<Brush>("KioskTanBrush"),
                BorderThickness = new Thickness(2),
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

            _pollTimer.Tick += async (_, _) => await CheckDoneAsync();
            _pageTextTimer.Tick += async (_, _) => await CheckPageTextAsync();
            Loaded += async (_, _) => await InitAsync();
            Closed += (_, _) => { _pollTimer.Stop(); _pageTextTimer.Stop(); _loginClickTimer.Stop(); };
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
                ToolTip = "סגירה",
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
                core.FrameCreated += (_, e) =>
                {
                    var frame = e.Frame;
                    _frames.Add(frame);
                    frame.Destroyed += (_, _) => _frames.Remove(frame);
                };
                core.Navigate(_startUrl);
                App.LogInfo($"{Title}: נפתח דף נדרים פלוס");
                _pollTimer.Start();
                _pageTextTimer.Start();
                if (_donorLoginOnly)
                {
                    core.NavigationCompleted += (_, e) =>
                    {
                        // מעבר בין דפים (למשל קישור מקוצר ← הדף עצמו): סופרים מהטעינה האחרונה
                        if (e.IsSuccess) _pageLoadedAt = DateTime.Now;
                        else App.LogInfo($"{Title}: טעינת הדף נכשלה ({e.WebErrorStatus})");
                    };
                    _loginClickTimer.Tick += async (_, _) => await TryOpenDonorLoginAsync();
                    _loginClickTimer.Start();
                }
            }
            catch (Exception ex)
            {
                App.LogError($"{Title}: כשל בפתיחת הדף", ex);
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "לא ניתן לפתוח את הדף של נדרים פלוס",
                    "אפשר לנסות שוב בעוד רגע.");
                Close();
            }
        }

        /// <summary>
        /// הדף של נדרים פלוס מזהה לפי הת"ז שמילאנו מראש מי שיש לו אזור אישי, ומציג קישור
        /// "אתה מוכר: לחץ כאן" שפותח את חלון הסיסמה. כשהקישור מופיע (בדף או במסגרת פנימית) - לוחצים
        /// עליו בשביל המשתמש. אם הדף נטען והקישור
        /// לא הופיע תוך 15 שניות - הדף נשאר פתוח לתשלום רגיל בכרטיס, ושורת ההודעה מסבירה.
        /// </summary>
        private async Task TryOpenDonorLoginAsync()
        {
            if (_closing || _webView.CoreWebView2 == null) return;

            const string script =
                "(function(){" +
                "if(window.__kioskLoginClicked)return 'done';" +
                "var best=null;" +
                "document.querySelectorAll('a,span,div,label,p,button,b,strong').forEach(function(e){" +
                " var t=(e.innerText||e.textContent||'').replace(/\\s+/g,' ').trim();" +
                " if(t.indexOf('מוכר')<0||t.indexOf('לחץ')<0||t.length>60)return;" +
                // רק משהו שמוצג בפועל (לא תבנית מוסתרת בדף)
                " var r=e.getBoundingClientRect();if(r.width===0&&r.height===0)return;" +
                " if(!best||t.length<(best.innerText||'').length)best=e;});" +
                "if(!best)return 'wait';" +
                "var link=null;best.querySelectorAll('a,span,button,u').forEach(function(x){" +
                " if(!link&&(x.innerText||x.textContent||'').indexOf('לחץ')>=0)link=x;});" +
                "(link||best).click();window.__kioskLoginClicked=true;return 'clicked';" +
                "})()";

            string? result = null;
            try
            {
                result = JsonSerializer.Deserialize<string>(await _webView.CoreWebView2.ExecuteScriptAsync(script));
                // גם במסגרות פנימיות של הדף
                foreach (var frame in _frames.ToArray())
                {
                    if (result == "clicked" || result == "done") break;
                    try { result = JsonSerializer.Deserialize<string>(await frame.ExecuteScriptAsync(script)); }
                    catch { /* מסגרת שנסגרה / בטעינה */ }
                }
            }
            catch
            {
                // הדף עדיין נטען - ננסה שוב
            }

            if (result == "clicked" || result == "done")
            {
                _loginClickTimer.Stop();
                App.LogInfo($"{Title}: נמצא אזור אישי - נפתח חלון הכניסה של נדרים");
                if (_checkingText != null)
                {
                    _checkingText.Text = "זוהה אזור אישי - מקלידים את הסיסמה של נדרים פלוס, בוחרים כרטיס שמור ומשלמים";
                }
                return;
            }

            if (_pageLoadedAt is DateTime loaded && DateTime.Now - loaded > DonorDetectWait)
            {
                // לא זוהה אזור אישי. לא סוגרים: הדף נשאר פתוח, ואפשר לשלם בו בכרטיס (סכום
                // וקטגוריה נעולים, התשלום נקלט לפי האסימון כמו בכרטיס שמור).
                _loginClickTimer.Stop();
                await LogPageForDiagnosisAsync();
                if (_checkingText != null)
                {
                    _checkingText.Text = "לא זוהה אזור אישי בנדרים פלוס לתעודת הזהות הזו. אפשר לשלם כאן בכרטיס אשראי, או לסגור ב-X.";
                }
                return;
            }

            if (_pageLoadedAt == null && DateTime.Now - _openedAt > PageLoadTimeout)
            {
                _loginClickTimer.Stop();
                PageLoadFailed = true;
                App.LogInfo($"{Title}: הדף של נדרים פלוס לא נטען");
                await CloseAndClearAsync();
            }
        }

        // ליומן הקיוסק: כתובת הדף ותחילת הטקסט שבו - כדי להבין למה "אתה מוכר" לא נמצא
        private async Task LogPageForDiagnosisAsync()
        {
            try
            {
                var core = _webView.CoreWebView2;
                var text = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(
                    "(document.body?document.body.innerText:'').replace(/\\s+/g,' ').substring(0,400)")) ?? "";
                App.LogInfo($"{Title}: לא זוהה אזור אישי. כתובת: {core.Source} | מסגרות: {_frames.Count} | טקסט: {text}");
            }
            catch (Exception ex)
            {
                App.LogError($"{Title}: כשל ברישום הדף ליומן", ex);
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
                // מילים כמו "תודה" עשויות להופיע בדף גם לפני התשלום - לכן לכל היותר בדיקה אחת ל-10 שניות
                if (SuccessHints.Any(h => text.Contains(h)) && DateTime.Now - _lastCheck > TimeSpan.FromSeconds(10))
                {
                    await CheckDoneAsync(force: true);
                }
            }
            catch
            {
                // דף בטעינה וכו' - ננסה בפעם הבאה
            }
        }

        private async Task CheckDoneAsync(bool force = false)
        {
            if (_checking || _closing) return;
            if (!force && DateTime.Now - _openedAt < _firstCheckAfter) return;
            // אחרי חצי שעה (החלון נשכח פתוח) - מפסיקים לבדוק
            if (DateTime.Now - _openedAt > TimeSpan.FromMinutes(30)) { _pollTimer.Stop(); _pageTextTimer.Stop(); return; }

            _checking = true;
            _lastCheck = DateTime.Now;
            try
            {
                if (!await _isDone()) return;
                Completed = true;
                _pollTimer.Stop();
                _pageTextTimer.Stop();
                App.LogInfo($"{Title}: הפעולה הסתיימה - סוגר את הדף");
                if (_successTitle != null)
                {
                    await KioskMessageDialog.ShowTimedAsync(this, KioskMessageKind.Success, _successTitle, _successMessage ?? "", 2.5);
                }
                await CloseAndClearAsync();
            }
            catch (Exception ex)
            {
                App.LogError($"{Title}: כשל בבדיקה מול השרת", ex);
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

        private async void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            // נדרים פלוס מעבירים לדף החזרה שלנו בסוף תשלום מוצלח - לא מציגים אותו, רק בודקים מיד
            if (_returnUrlPrefix != null && e.Uri.StartsWith(_returnUrlPrefix, StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                await CheckDoneAsync(force: true);
                return;
            }
            if (IsAllowed(e.Uri)) return;
            e.Cancel = true;
            App.LogInfo($"{Title}: נחסמה גלישה לכתובת שאינה של נדרים פלוס: {e.Uri}");
        }

        // קישורים שנפתחים בחלון חדש - נפתחים באותו חלון (אם מותרים), לא בחלון דפדפן נפרד
        private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (IsAllowed(e.Uri)) _webView.CoreWebView2?.Navigate(e.Uri);
            else App.LogInfo($"{Title}: נחסם חלון חדש לכתובת: {e.Uri}");
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
                App.LogError($"{Title}: כשל במחיקת נתוני הגלישה", ex);
            }
            Close();
        }
    }
}
