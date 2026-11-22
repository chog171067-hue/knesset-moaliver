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

        // זמן חסד אחרי הפעלה: תוכנות עם "Launcher" סוגרות את התהליך שהפעלנו ופותחות
        // את החלון האמיתי רק אחרי כמה שניות - לא חוזרים למסך הבחירה לפני שזה קרה.
        private static readonly TimeSpan LaunchGracePeriod = TimeSpan.FromSeconds(8);

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

            UpdateTimerText();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => UpdateTimerText();
            _clockTimer.Start();

            // כשהלשונית נפתחת/נסגרת גודלה משתנה - שלא תחרוג מהמסך
            SizeChanged += (_, _) => ClampToScreen();
            _ = LoadAccountInfoAsync();


            FileDialogAssistant.UserMessage += OnFileDialogMessage;

            _guardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _guardTimer.Tick += OnGuardTick;
            _guardTimer.Start();
        }

        // מיקום הלשונית אחרי גרירה - נשמר עד הניתוק (גם במעבר בין תוכנות)
        private static Point? _lastPosition;

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
                WindowGuard.Enforce(0); // מסך מלא - הלשונית צפה מעל התוכנה
                // חלונות "שמירה"/"בחירת תיקייה" - נשמרים אוטומטית לדיסק-און-קי
                FileDialogAssistant.Tick(WindowGuard.IsUserProcess);

                if (_launchedProcessExited
                    && DateTime.Now - _launchedAt > LaunchGracePeriod
                    && WindowGuard.FindUserWindows().Count == 0)
                {
                    ReturnToPicker();
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

        /// <summary>נקרא ע"י AppPickerWindow מיד אחרי יצירת ה-Overlay, עם התהליך שהופעל.</summary>
        public void Initialize(Process process, AppPickerWindow ownerPicker)
        {
            _process = process;
            _ownerPicker = ownerPicker;
            _launchedAt = DateTime.Now;
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

        private async void OnSwitchAppClick(object sender, RoutedEventArgs e)
        {
            _guardTimer?.Stop(); // סוגרים בכוונה - שהשומר לא "יחזיר" חלונות בזמן הסגירה
            if (_process != null)
            {
                _process.Exited -= OnLaunchedProcessExited; // סגירה יזומה - לא רוצים שגם OnLaunchedProcessExited יופעל
                await ProcessLauncher.CloseGracefullyAsync(_process);
            }
            await WindowGuard.CloseUserAppsAsync(); // גם חלונות שנפתחו בתהליכים אחרים (Launcher)
            ReturnToPicker();
        }

        private async void OnLogoutClick(object sender, RoutedEventArgs e)
{
    _guardTimer?.Stop(); // סוגרים בכוונה - שהשומר לא "יחזיר" חלונות בזמן הסגירה
    if (_process != null)
    {
        _process.Exited -= OnLaunchedProcessExited;
    }

    // רשת ביטחון: סוגר את כל התהליכים שהופעלו ב-session הזה, לא רק את זה
    // שה-Overlay עוקב אחריו - ראו ההערה המפורטת ב-ProcessLauncher.CloseAllTrackedAsync
    await ProcessLauncher.CloseAllTrackedAsync();
    await WindowGuard.CloseUserAppsAsync(); // גם חלונות שנפתחו בתהליכים אחרים (Launcher)
    WindowGuard.ClearAppSessionState();     // טקסט שתוכנות שמרו אוטומטית - לא יוצג למשתמש הבא
    WindowGuard.ClearUserFiles();           // קבצים שנשמרו בתיקיות של משתמש הקיוסק (רק במחשב נעול)
    TextEditorWindow.EndSession();          // הטקסט בעורך נמחק
    App.StopSessionServices();              // הדפסות תקועות מבוטלות ומוחזרות

    App.Session.End();
    ResetPosition();

            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();

            _ownerPicker?.Close();
            Close();
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
            _clockTimer?.Stop();
            _guardTimer?.Stop();
            FileDialogAssistant.UserMessage -= OnFileDialogMessage;
        }
    }
}
