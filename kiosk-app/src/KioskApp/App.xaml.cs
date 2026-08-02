using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using KioskApp.Core;
using KioskApp.Services;
using KioskApp.Views;

namespace KioskApp
{
    /// <summary>
    /// נקודת הכניסה. חשוב: כשהתהליך הזה מוגדר כ-Shell של Windows (ראו kiosk-app/setup),
    /// יציאה בלתי מבוקרת מהתהליך = ניתוק המשתמש מה-Session כולו. לכן כל שגיאה שלא
    /// טופלה מטופלת כאן בצורה מרכזית (רישום ליומן + הודעה) במקום לתת ל-WPF להפיל
    /// את התהליך בשקט.
    /// </summary>
    public partial class App : Application
    {
        public static AppConfig Config { get; private set; } = null!;
        public static KioskApiClient ApiClient { get; private set; } = null!;
        public static KioskSession Session { get; private set; } = null!;

        private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "kiosk-app.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            try
            {
                Config = AppConfig.Load();
                ApiClient = new KioskApiClient(Config.ApiBaseUrl, Config.StationApiKey);
                Session = new KioskSession();
            }
            catch (Exception ex)
            {
                LogError("כשל בטעינת הגדרות ההפעלה", ex);
                MessageBox.Show(
                    $"שגיאה קריטית בהפעלת הקיוסק:\n\n{ex.Message}\n\nיש לתקן את קובץ ההגדרות ולהפעיל מחדש.",
                    "שגיאת הפעלה", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var loginWindow = new LoginWindow();
            MainWindow = loginWindow;
            loginWindow.Show();
        }

        public static void LogError(string context, Exception ex)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}\n";
                File.AppendAllText(LogPath, line);
            }
            catch
            {
                // אם אפילו הרישום ליומן נכשל (למשל דיסק מלא) - אין מה לעשות עוד, לפחות לא ליפול על זה
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogError("שגיאה לא מטופלת ב-UI thread", e.Exception);
            MessageBox.Show(
                $"אירעה שגיאה בלתי צפויה:\n\n{e.Exception.Message}\n\nהאפליקציה תמשיך לפעול.",
                "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true; // קריטי: בלי זה, WPF מפיל את כל התהליך = ניתוק המשתמש מה-Shell
        }

        private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogError("שגיאה לא מטופלת מחוץ ל-UI thread (קטלנית)", ex);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogError("Task שנכשל בלי שאיש חיכה לתוצאה שלו", e.Exception);
            e.SetObserved();
        }
    }
}
