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
        }

        /// <summary>נקרא ע"י AppPickerWindow מיד אחרי יצירת ה-Overlay, עם התהליך שהופעל.</summary>
        public async void Initialize(Process process, AppPickerWindow ownerPicker)
        {
            _process = process;
            _ownerPicker = ownerPicker;
            process.EnableRaisingEvents = true;
            process.Exited += OnLaunchedProcessExited;

            var screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            var screenHeight = (int)SystemParameters.PrimaryScreenHeight;
            await ProcessLauncher.PositionBelowOverlayAsync(process, (int)Height, screenWidth, screenHeight);
        }

        private void OnLaunchedProcessExited(object? sender, EventArgs e)
        {
            // התוכנה נסגרה מיוזמת המשתמש (לא דרך כפתור "החלפת תוכנה") - חוזרים למסך הבחירה
            Dispatcher.Invoke(ReturnToPicker);
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
            if (_process != null)
            {
                _process.Exited -= OnLaunchedProcessExited; // סגירה יזומה - לא רוצים שגם OnLaunchedProcessExited יופעל
                await ProcessLauncher.CloseGracefullyAsync(_process);
            }
            ReturnToPicker();
        }

        private async void OnLogoutClick(object sender, RoutedEventArgs e)
{
    if (_process != null)
    {
        _process.Exited -= OnLaunchedProcessExited;
    }

    // רשת ביטחון: סוגר את כל התהליכים שהופעלו ב-session הזה, לא רק את זה
    // שה-Overlay עוקב אחריו - ראו ההערה המפורטת ב-ProcessLauncher.CloseAllTrackedAsync
    await ProcessLauncher.CloseAllTrackedAsync();

    App.Session.End();

            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();

            _ownerPicker?.Close();
            Close();
        }

        private void ReturnToPicker()
        {
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
            _printMonitor?.Dispose();
        }
    }
}
