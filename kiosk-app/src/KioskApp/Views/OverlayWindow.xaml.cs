using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using KioskApp.Services;

namespace KioskApp.Views
{
    public partial class OverlayWindow : Window
    {
        private Process? _process;
        private AppPickerWindow? _ownerPicker;
        private DispatcherTimer? _clockTimer;
        private PrintMonitorService? _printMonitor;
        private DispatcherTimer? _guardTimer;
        private DateTime _launchedAt = DateTime.Now;
        private bool _launchedProcessExited;
        private bool _returningToPicker;

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
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;

            UsernameText.Text = App.Session.CurrentUser?.Username ?? "";
            UpdateBalanceText(App.Session.CurrentUser?.Balance ?? 0);
            App.Session.BalanceChanged += OnSessionBalanceChanged;

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => TimerText.Text = $"זמן חיבור: {App.Session.Elapsed:hh\\:mm\\:ss}";
            _clockTimer.Start();

            _printMonitor = new PrintMonitorService(App.ApiClient, () => App.Session.CurrentUser?.Id ?? "");
            _printMonitor.PrintCharged += OnPrintCharged;
            _printMonitor.PrintBlocked += OnPrintBlocked;
            _printMonitor.MonitorError += (_, msg) => App.LogError("PrintMonitorService", new InvalidOperationException(msg));
            _printMonitor.Start();

            _guardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _guardTimer.Tick += OnGuardTick;
            _guardTimer.Start();
        }

        /// <summary>
        /// כל חצי שנייה: מחזירים את חלונות התוכנות למסך מלא מתחת לסרגל, וכשהתוכנה
        /// נסגרה לגמרי (אין לה יותר אף חלון) - חוזרים למסך בחירת התוכנה.
        /// </summary>
        private void OnGuardTick(object? sender, EventArgs e)
        {
            try
            {
                WindowGuard.Enforce(OverlayHeightInPixels());

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
        }

        /// <summary>
        /// גובה הסרגל בפיקסלים פיזיים. WPF עובד ביחידות "לוגיות", ובמחשב שבו התצוגה
        /// מוגדלת (125%/150%) הן שונות מהפיקסלים ש-Windows משתמש בהם למיקום חלונות.
        /// </summary>
        private int OverlayHeightInPixels()
        {
            var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            return (int)Math.Ceiling(ActualHeight * scale);
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

        private void OnSessionBalanceChanged(object? sender, decimal newBalance) => UpdateBalanceText(newBalance);

        private void UpdateBalanceText(decimal balance) => BalanceText.Text = $"יתרה: {balance:0.00} ₪";

        private void OnPrintCharged(object? sender, PrintChargedEventArgs e)
        {
            App.Session.UpdateBalance(e.NewBalance);
        }

        private void OnPrintBlocked(object? sender, PrintBlockedEventArgs e)
        {
            MessageBox.Show(
                $"היתרה שלך ({e.Balance:0.00} ₪) אינה מספיקה לעלות ההדפסה ({e.Cost:0.00} ₪).\nההדפסה נחסמה. לחצו על \"הטענת כסף\" כדי להמשיך.",
                "יתרה לא מספיקה", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    App.Session.End();

            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();

            _ownerPicker?.Close();
            Close();
        }

        private void ReturnToPicker()
        {
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
            _printMonitor?.Dispose();
        }
    }
}
