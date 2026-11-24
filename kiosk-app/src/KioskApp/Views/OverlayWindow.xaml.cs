using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using KioskApp.Services;

namespace KioskApp.Views
{
    public partial class OverlayWindow : Window
    {
        private Process? _process;
        private AppPickerWindow? _ownerPicker;
        private DispatcherTimer? _clockTimer;
        private DispatcherTimer? _guardTimer;
        private DateTime _launchedAt = DateTime.Now;
        private bool _launchedProcessExited;
        private bool _returningToPicker;
        private bool _inGuardTick;
        // מצב "מסך בחירת התוכנה": אין תוכנה פתוחה - בלי שומר חלונות ובלי "חזרה לבחירת התוכנות"
        private bool _pickerMode;
        private DispatcherTimer? _gabaiTimer;

        // זמן חסד אחרי הפעלה: תוכנות עם "Launcher" סוגרות את התהליך שהפעלנו ופותחות
        // את החלון האמיתי רק אחרי כמה שניות - לא חוזרים למסך הבחירה לפני שזה קרה.
        private static readonly TimeSpan LaunchGracePeriod = TimeSpan.FromSeconds(8);
        // תוכנה שעדיין "טוענת" (יש תהליך שרץ מהתיקייה שלה, אבל עוד אין חלון רגיל - למשל
        // אוצר החכמה, שמציג קודם מסך פתיחה ורק אחר כך את התוכנה): מחכים לה עד הזמן הזה
        private static readonly TimeSpan MaxWaitForAppWindow = TimeSpan.FromMinutes(5);
        private string? _appFolder;
        private DateTime? _noWindowSince;
        private DateTime _lastProcessCheck = DateTime.MinValue;
        private bool _appStillRunning;
        // התוכנה כבר הציגה חלון (אחרי זה - חלון שנעלם = המשתמש סגר אותה)
        private bool _sawAppWindow;
        // בדקה הראשונה אחרי הפעלה תוכנה עוד יכולה "להחליף" חלונות (מסך פתיחה ← התוכנה) - מחכים בזהירות
        private static readonly TimeSpan LoadingPeriod = TimeSpan.FromSeconds(60);

        public OverlayWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += (_, _) => Cleanup();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // הלשונית מתחילה בצד שמאל של המסך (או איפה שהמשתמש גרר אותה בפעם הקודמת)
            var area = SystemParameters.WorkArea;
            Left = _lastPosition?.X ?? area.Left + 8;
            Top = _lastPosition?.Y ?? area.Top + 90;
            ClampToScreen();

            var user = App.Session.CurrentUser;
            UsernameText.Text = string.IsNullOrWhiteSpace(user?.DisplayName) ? user?.Username ?? "" : user!.DisplayName!;
            UpdateBalanceText(App.Session.CurrentUser?.Balance ?? 0);
            TextEditorButton.Visibility = App.Session.Config?.TextEditorEnabled == true
                ? Visibility.Visible : Visibility.Collapsed;
            MemberTag.Visibility = App.Session.IsCommunityMember == true ? Visibility.Visible : Visibility.Collapsed;
            App.Session.BalanceChanged += OnSessionBalanceChanged;
            PrintArea.Changed += UpdatePrintAreaBadge;
            UpdatePrintAreaBadge();

            UpdateTimerText();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => UpdateTimerText();
            _clockTimer.Start();

            // כשהלשונית נפתחת/נסגרת גודלה משתנה - שלא תחרוג מהמסך
            SizeChanged += (_, _) => ClampToScreen();
            _ = LoadAccountInfoAsync();

            SwitchAppButton.Visibility = _pickerMode ? Visibility.Collapsed : Visibility.Visible;
            // גם בתוך תוכנה: היציאה מנתקת מ-Windows, וזה סוגר ממילא את כל התוכנות
            ExitToDesktopButton.Visibility = user?.CanExitKiosk == true ? Visibility.Visible : Visibility.Collapsed;

            // תשובה חדשה מהגבאי - בדיקה עכשיו ואחת לדקה
            _ = CheckGabaiUnreadAsync();
            _gabaiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _gabaiTimer.Tick += async (_, _) => await CheckGabaiUnreadAsync();
            _gabaiTimer.Start();

            if (_pickerMode) return; // מסך בחירת התוכנה - אין תוכנה לשמור עליה


            FileDialogAssistant.UserMessage += OnFileDialogMessage;

            _guardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _guardTimer.Tick += OnGuardTick;
            _guardTimer.Start();
        }

        // מיקום הלשונית אחרי גרירה - נשמר עד הניתוק (גם במעבר בין תוכנות)
        private static Point? _lastPosition;

        /// <summary>
        /// מעלה את הלשונית הצפה מעל כל החלונות (למשל מעל עורך הטקסט, שגם הוא "תמיד למעלה"),
        /// בלי לקחת את הפוקוס מהחלון הפעיל.
        /// </summary>
        public static void RaiseAllTabs()
        {
            foreach (var tab in System.Linq.Enumerable.OfType<OverlayWindow>(Application.Current.Windows))
            {
                if (!tab.IsVisible) continue;
                var handle = new System.Windows.Interop.WindowInteropHelper(tab).Handle;
                if (handle == IntPtr.Zero) continue;
                SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
            }
        }

        private static readonly IntPtr HwndTopmost = new(-1);
        private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010, SwpShowWindow = 0x0040;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        /// <summary>מאפס את מיקום הלשונית (בניתוק) - המשתמש הבא מתחיל בצד שמאל</summary>
        public static void ResetPosition() => _lastPosition = null;

        private void UpdateTimerText()
        {
            var t = App.Session.Elapsed;
            TimerText.Text = $"מחובר: {(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        }

        // חבר קהילה + יתרה עדכנית מהשרת. תקלה כאן רק משאירה את מה שכבר מוצג.
        private async System.Threading.Tasks.Task LoadAccountInfoAsync()
        {
            var userId = App.Session.CurrentUser?.Id;
            if (string.IsNullOrEmpty(userId)) return;
            try
            {
                var info = await App.ApiClient.GetAccountInfoAsync(userId);
                if (App.Session.CurrentUser?.Id != userId) return; // בינתיים התנתקו
                App.Session.IsCommunityMember = info.IsCommunityMember;
                MemberTag.Visibility = info.IsCommunityMember ? Visibility.Visible : Visibility.Collapsed;
                App.Session.UpdateBalance(info.Balance);
            }
            catch (Exception ex)
            {
                App.LogError("הלשונית: כשל בטעינת פרטי החשבון", ex);
            }
        }

        private void OnToggleClick(object sender, RoutedEventArgs e)
        {
            var open = ExpandedPanel.Visibility != Visibility.Visible;
            ExpandedPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ChevronRotate.Angle = open ? 180 : 0;
            ToggleButton.ToolTip = open ? "סגירת הלשונית" : "פתיחת הלשונית";
            if (open) _ = LoadAccountInfoAsync();
        }

        // גרירת הלשונית בפס העליון (לא בלחיצה על החץ)
        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && IsInside(d, ToggleButton)) return;
            try { DragMove(); } catch (InvalidOperationException) { /* העכבר כבר שוחרר */ }
            ClampToScreen();
            _lastPosition = new Point(Left, Top);
        }

        private static bool IsInside(DependencyObject? d, DependencyObject container)
        {
            while (d != null)
            {
                if (d == container) return true;
                d = System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private void ClampToScreen()
        {
            var area = SystemParameters.WorkArea;
            var w = ActualWidth > 0 ? ActualWidth : 320;
            var h = ActualHeight > 0 ? ActualHeight : 90;
            Left = Math.Max(area.Left, Math.Min(Left, area.Right - w));
            Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - h));
        }

        private void OnHistoryClick(object sender, RoutedEventArgs e)
        {
            new PaymentHistoryWindow { Owner = this }.ShowDialog();
        }

        /// <summary>
        /// כל חצי שנייה: מחזירים את חלונות התוכנות למסך מלא (הלשונית צפה מעליהן), וכשהתוכנה
        /// נסגרה לגמרי (אין לה יותר אף חלון) - חוזרים למסך בחירת התוכנה.
        /// </summary>
        private void OnGuardTick(object? sender, EventArgs e)
        {
            if (_inGuardTick) return; // הודעה למשתמש פתוחה (לולאה מקוננת) - לא נכנסים שוב
            _inGuardTick = true;
            try
            {
                WindowGuard.Enforce(0, _appFolder); // מסך מלא (רק חלונות התוכנה) - הלשונית צפה מעליה
                // חלונות "שמירה" / "הדפסה" / דרייבר המדפסת - בטיימר המהיר של App (StartSessionServices)

                // התוכנה מוזערה - שואלים: לצאת למסך בחירת התוכנות, או לחזור אליה
                if (!_minimizePromptOpen)
                {
                    var minimized = WindowGuard.FindMinimizedAppWindow(_appFolder);
                    if (minimized != IntPtr.Zero)
                    {
                        _minimizePromptOpen = true;
                        Dispatcher.BeginInvoke(new Action(() => AskLeaveAfterMinimize(minimized)));
                    }
                }

                var hasWindow = WindowGuard.FindUserWindows().Count > 0 || WindowGuard.AnyVisibleWindowFrom(_appFolder);
                if (hasWindow)
                {
                    _sawAppWindow = true;
                    _noWindowSince = null;
                }
                else if ((_launchedProcessExited || _sawAppWindow) && DateTime.Now - _launchedAt > LaunchGracePeriod)
                {
                    _noWindowSince ??= DateTime.Now;
                    var waited = DateTime.Now - _noWindowSince.Value;

                    // התוכנה כבר הייתה פתוחה (לא בטעינה) והחלון שלה נעלם - המשתמש סגר אותה: חוזרים מיד
                    if (_sawAppWindow && DateTime.Now - _launchedAt > LoadingPeriod && waited > TimeSpan.FromSeconds(0.7))
                    {
                        App.LogInfo($"התוכנה נסגרה ({_appFolder})");
                        ReturnToPickerAndCleanUp();
                        return;
                    }

                    // בזמן טעינה: האם עדיין רץ תהליך מהתיקייה של התוכנה (= היא עוד נטענת)
                    if (DateTime.Now - _lastProcessCheck > TimeSpan.FromSeconds(1))
                    {
                        _lastProcessCheck = DateTime.Now;
                        _appStillRunning = WindowGuard.AnyProcessRunningFrom(_appFolder);
                    }
                    if (!_appStillRunning || waited > MaxWaitForAppWindow)
                    {
                        App.LogInfo(_appStillRunning
                            ? $"התוכנה לא הציגה חלון תוך {MaxWaitForAppWindow.TotalMinutes:0} דקות ({_appFolder})"
                            : $"התוכנה נסגרה - אין חלון ואין תהליך פעיל ({_appFolder})");
                        ReturnToPickerAndCleanUp();
                    }
                }
                else
                {
                    _noWindowSince = null;
                }
            }
            catch (Exception ex)
            {
                App.LogError("OverlayWindow.OnGuardTick", ex);
            }
            finally
            {
                _inGuardTick = false;
            }
        }

        /// <summary>הלשונית במסך בחירת התוכנה (בלי תוכנה פתוחה)</summary>
        public void InitializeForPicker(AppPickerWindow ownerPicker)
        {
            _ownerPicker = ownerPicker;
            _pickerMode = true;
        }

        /// <summary>עצירת שומר החלונות (לפני ניתוק - שלא "יחזיר" חלונות של תוכנות שנסגרות)</summary>
        public void StopGuard() => _guardTimer?.Stop();

        private async System.Threading.Tasks.Task CheckGabaiUnreadAsync()
        {
            var userId = App.Session.CurrentUser?.Id;
            if (string.IsNullOrEmpty(userId)) return;
            try
            {
                var thread = await App.ApiClient.GetGabaiThreadAsync(userId, markRead: false);
                SetGabaiUnread(thread.HasUnread);
            }
            catch (Exception ex)
            {
                App.LogError("הלשונית: כשל בבדיקת הודעות מהגבאי", ex);
            }
        }

        private void SetGabaiUnread(bool unread)
        {
            var v = unread ? Visibility.Visible : Visibility.Collapsed;
            GabaiUnreadBadge.Visibility = v;
            HeaderUnreadDot.Visibility = v;
        }

        private void OnGabaiClick(object sender, RoutedEventArgs e)
        {
            new GabaiChatWindow { Owner = this }.ShowDialog();
            SetGabaiUnread(false); // החלון מסמן את התשובות כנקראו
        }

        private async void OnExitToDesktopClick(object sender, RoutedEventArgs e)
        {
            if (App.Session.CurrentUser?.CanExitKiosk != true) return;
            var go = KioskMessageDialog.ShowChoice(this, KioskMessageKind.Info, "יציאה למחשב",
                "לצאת מהקיוסק למסך הכניסה של Windows?\nשם בוחרים את חשבון המנהל ומקלידים את הסיסמה שלו.\n" +
                "כדי לחזור לקיוסק: להפעיל את המחשב מחדש (או להתנתק מ-Windows).",
                "יציאה", "ביטול");
            if (!go) return;
            _guardTimer?.Stop(); // בזמן ההתנתקות מ-Windows - לא "מחזירים" חלונות של תוכנות שנסגרות
            await App.ChargeUsedMinutesAsync(); // הדקות שעוד לא חויבו
            var error = App.SignOutToWindowsSignIn();
            if (error != null)
            {
                _guardTimer?.Start();
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "יציאה למחשב", error);
            }
        }

        /// <summary>נקרא ע"י AppPickerWindow מיד אחרי יצירת ה-Overlay, עם התהליך שהופעל.</summary>
        public void Initialize(Process process, AppPickerWindow ownerPicker)
        {
            _process = process;
            _ownerPicker = ownerPicker;
            _launchedAt = DateTime.Now;
            try { _appFolder = WindowGuard.AppFolderRoot(process.StartInfo.FileName); } catch { _appFolder = null; }
            App.LogInfo($"הופעלה תוכנה: {process.StartInfo.FileName}");
            // אחרי 20 שניות: לאבחון - אילו חלונות פתחה התוכנה והאם הקיוסק מזהה אותם
            var diag = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            diag.Tick += (_, _) =>
            {
                diag.Stop();
                try { App.LogInfo($"חלונות התוכנה ({_appFolder}):{WindowGuard.DescribeAppWindows(_appFolder)}"); }
                catch (Exception ex) { App.LogError("אבחון חלונות התוכנה", ex); }
            };
            diag.Start();
            process.EnableRaisingEvents = true;
            process.Exited += OnLaunchedProcessExited;
            // מיקום החלון ונעילתו מתבצעים ע"י OnGuardTick (WindowGuard)
        }

        private void OnLaunchedProcessExited(object? sender, EventArgs e)
        {
            // התהליך שהפעלנו נסגר. לא חוזרים מיד למסך הבחירה: ייתכן שזה רק "Launcher"
            // שפתח את החלון האמיתי בתהליך אחר. OnGuardTick יחזיר למסך הבחירה רק כשבאמת
            // לא נשאר אף חלון של תוכנה פתוח.
            _launchedProcessExited = true;
        }

        private void OnFileDialogMessage(string message)
        {
            // אחרי שהטיפול בחלון הסתיים - לא מתוך סבב השומר
            Dispatcher.BeginInvoke(new Action(() =>
                MessageBox.Show(this, message, "שמירת קובץ", MessageBoxButton.OK, MessageBoxImage.Information)));
        }

        private void OnSessionBalanceChanged(object? sender, decimal newBalance) => UpdateBalanceText(newBalance);

        private void UpdateBalanceText(decimal balance) => BalanceText.Text = $"{balance:0.00} ₪";

        private void OnTextEditorClick(object sender, RoutedEventArgs e)
        {
            // נפתח מעל התוכנה בלי לסגור אותה - אפשר להעתיק ממנה ולהדביק בעורך הלוך ושוב
            TextEditorWindow.ShowEditor("חזרה לתוכנה");
        }

        private void OnTopupClick(object sender, RoutedEventArgs e)
        {
            var topupWindow = new TopupWindow { Owner = this };
            topupWindow.ShowDialog();
        }

        private bool _minimizePromptOpen;

        /// <summary>
        /// התוכנה מוזערה: "הינך עומד לצאת מהתוכנה למסך בחירת התוכנות" - יציאה (סוגרים את התוכנה)
        /// או ביטול (התוכנה חוזרת למסך מלא).
        /// </summary>
        private async void AskLeaveAfterMinimize(IntPtr minimized)
        {
            try
            {
                if (_returningToPicker || !IsVisible) return;
                App.LogInfo("התוכנה מוזערה - שואלים אם לצאת למסך בחירת התוכנות");
                var leave = KioskMessageDialog.ShowChoice(this, KioskMessageKind.Info, "יציאה מהתוכנה",
                    "הינך עומד לצאת מהתוכנה למסך בחירת התוכנות.", "יציאה", "ביטול");
                if (leave)
                {
                    await CloseAppAndReturnAsync();
                    return;
                }
                WindowGuard.RestoreWindow(minimized);
                RaiseAllTabs();
            }
            finally
            {
                _minimizePromptOpen = false;
            }
        }

        private async void OnSwitchAppClick(object sender, RoutedEventArgs e) => await CloseAppAndReturnAsync();

        /// <summary>
        /// יציאה יזומה (הלשונית / חלון המזעור): מסך הבחירה מוצג **מיד**, והתוכנה נסגרת ברקע
        /// (סגירה מסודרת, ואז סגירה בכוח של מה שנשאר) - בלי שהמשתמש יחכה לה.
        /// </summary>
        private async System.Threading.Tasks.Task CloseAppAndReturnAsync()
        {
            _guardTimer?.Stop(); // סוגרים בכוונה - שהשומר לא "יחזיר" חלונות בזמן הסגירה
            var process = _process;
            var folder = _appFolder;
            var closedAt = DateTime.Now;
            if (process != null) process.Exited -= OnLaunchedProcessExited;
            ReturnToPicker();
            try
            {
                if (process != null) await ProcessLauncher.CloseGracefullyAsync(process, TimeSpan.FromSeconds(2));
                await WindowGuard.CloseUserAppsAsync(); // גם חלונות שנפתחו בתהליכים אחרים (Launcher)
                // תהליכים של התוכנה שנשארו בלי חלון (למשל אוצר החכמה) - שלא יחסמו הפעלה חוזרת
                await System.Threading.Tasks.Task.Run(() => WindowGuard.KillProcessesFrom(folder, closedAt));
            }
            catch (Exception ex)
            {
                App.LogError("סגירת התוכנה ברקע", ex);
            }
        }

        /// <summary>התוכנה נסגרה (X): חוזרים מיד, ותהליכים שנשארו ממנה נסגרים ברקע</summary>
        private void ReturnToPickerAndCleanUp()
        {
            var folder = _appFolder;
            var closedAt = DateTime.Now;
            ReturnToPicker();
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try { WindowGuard.KillProcessesFrom(folder, closedAt); }
                catch (Exception ex) { App.LogError("סגירת תהליכים שנשארו מהתוכנה", ex); }
            });
        }

        private void OnPrintAreaClick(object sender, RoutedEventArgs e)
        {
            new PrintAreaWindow { Owner = this }.ShowDialog();
        }

        private void UpdatePrintAreaBadge()
        {
            var count = PrintArea.Count;
            PrintAreaCount.Text = count.ToString();
            PrintAreaBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void OnLogoutClick(object sender, RoutedEventArgs e)
        {
            // יש הדפסות שלא הודפסו - אזהרה לפני שהן נמחקות
            var waiting = PrintArea.Count;
            if (waiting > 0)
            {
                var logout = KioskMessageDialog.ShowChoice(this, KioskMessageKind.Info, "יש הדפסות באזור ההדפסות",
                    $"יש לך {waiting} {(waiting == 1 ? "הדפסה" : "הדפסות")} שעוד לא {(waiting == 1 ? "הודפסה" : "הודפסו")}. בניתוק {(waiting == 1 ? "היא תימחק" : "הן יימחקו")}.",
                    "ניתוק ומחיקה", "מעבר לאזור ההדפסות");
                if (!logout)
                {
                    new PrintAreaWindow { Owner = this }.ShowDialog();
                    return;
                }
            }
            LogoutButton.IsEnabled = false;
            _guardTimer?.Stop(); // סוגרים בכוונה - שהשומר לא "יחזיר" חלונות בזמן הסגירה
            if (_process != null)
            {
                _process.Exited -= OnLaunchedProcessExited;
            }
            await App.LogoutAsync();
        }

        private void ReturnToPicker()
        {
            App.LogInfo("חזרה למסך בחירת התוכנה");
            if (_returningToPicker) return;
            _returningToPicker = true;
            _guardTimer?.Stop();

            if (_ownerPicker != null)
            {
                Application.Current.MainWindow = _ownerPicker;
                _ownerPicker.ShowAgain();
            }
            Close();
        }

        private void Cleanup()
        {
            App.Session.BalanceChanged -= OnSessionBalanceChanged;
            PrintArea.Changed -= UpdatePrintAreaBadge;
            _clockTimer?.Stop();
            _guardTimer?.Stop();
            _gabaiTimer?.Stop();
            FileDialogAssistant.UserMessage -= OnFileDialogMessage;
        }
    }
}
