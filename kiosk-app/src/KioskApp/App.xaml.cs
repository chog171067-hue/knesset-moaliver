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

        public App()
        {
            // נרשם כמה שיותר מוקדם (לפני טעינת XAML/הגדרות), כדי שגם קריסה מוקדמת תירשם ליומן.
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // שורת הפעלה קבועה: אם היא לא מופיעה ב-kiosk-app.log - הקוד שלנו בכלל לא רץ
            // (הבעיה ברמת Windows/.NET: קבצים חסרים, חסימת אבטחה וכו'), ולא בקוד ה-C#.
            LogInfo($"הפעלה: גרסה {typeof(App).Assembly.GetName().Version}, תיקייה {AppContext.BaseDirectory}, משתמש {Environment.UserName}");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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

            // "קו הבסיס" של שומר החלונות נלקח פעם אחת, בעליית הקיוסק - ולא בכל התחברות:
            // כל תוכנה שנפתחה מאז (גם ע"י משתמש קודם שלא נסגרה) נחשבת תוכנת משתמש,
            // נשמרת במסך מלא ונסגרת בניתוק. רק מה שרץ לפני הקיוסק עצמו לא נוגעים בו.
            WindowGuard.CaptureBaseline();

            var loginWindow = new LoginWindow();
            MainWindow = loginWindow;
            loginWindow.Show();
        }

        /// <summary>
        /// "יציאה למחשב" (למשתמש מורשה בלבד): מסתיר את כל חלונות הקיוסק, מפעיל את שולחן
        /// העבודה של Windows (explorer.exe), ומציג כפתור קטן "חזרה לקיוסק" בפינת המסך.
        /// הקיוסק עצמו ממשיך לרוץ ברקע - הוא ה-Shell של משתמש הקיוסק, ויציאה ממנו הייתה
        /// משאירה מסך שחור.
        /// </summary>
        public static void ExitToDesktop()
        {
            LogInfo($"יציאה למחשב: {Session.CurrentUser?.Username}");
            Session.End();

            foreach (Window w in Current.Windows)
            {
                w.Hide();
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogError("כשל בהפעלת שולחן העבודה", ex);
            }

            var returnWindow = new ReturnToKioskWindow();
            returnWindow.Show();
        }

        /// <summary>חזרה מ"יציאה למחשב" למסך ההתחברות של הקיוסק.</summary>
        public static void ReturnToKiosk()
        {
            LogInfo("חזרה לקיוסק משולחן העבודה");

            // כשהקיוסק הוא ה-Shell (מחשב נעול), סוגרים את שולחן העבודה שהופעל ביציאה.
            // במחשב שלא ננעל (בדיקות) - לא נוגעים ב-Explorer, הוא שולחן העבודה הרגיל של המנהל.
            if (IsRunningAsShell())
            {
                var mySession = System.Diagnostics.Process.GetCurrentProcess().SessionId;
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        if (p.SessionId == mySession) p.Kill();
                    }
                    catch (Exception ex)
                    {
                        LogError("כשל בסגירת שולחן העבודה", ex);
                    }
                }
            }

            foreach (Window w in Current.Windows)
            {
                if (w is not ReturnToKioskWindow) w.Close();
            }

            var login = new LoginWindow();
            Current.MainWindow = login;
            login.Show();
        }

        private static bool IsRunningAsShell()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Winlogon");
                var shell = key?.GetValue("Shell") as string;
                return !string.IsNullOrEmpty(shell)
                       && shell.Contains("KioskApp.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// מסכי הקיוסק (התחברות, בחירת תוכנה) לעולם לא נשארים ממוזערים: במחשב נעול אין
        /// שורת משימות, ומסך ממוזער הופך לפס קטן בפינה שאי אפשר להחזיר - המשתמש נתקע
        /// מול מסך שחור. כל מזעור (למשל בעקבות דיאלוג "לשמור?" של תוכנה שנסגרת) מתוקן מיד.
        /// </summary>
        public static void KeepFullScreen(Window window)
        {
            window.StateChanged += (_, _) =>
            {
                if (window.WindowState != WindowState.Minimized) return;
                window.Dispatcher.BeginInvoke(new Action(() => BringToFront(window)));
            };
            window.IsVisibleChanged += (_, _) =>
            {
                if (window.IsVisible) window.Dispatcher.BeginInvoke(new Action(() => BringToFront(window)));
            };
        }

        public static void BringToFront(Window window)
        {
            if (!window.IsVisible) return;
            window.WindowState = WindowState.Maximized;
            window.Topmost = false; // "ניעור" של Topmost מחזיר את החלון לראש הערימה גם אם חלון אחר תפס אותו
            window.Topmost = true;
            window.Activate();
        }

        public static void LogError(string context, Exception ex) => WriteLogLine($"{context}: {ex}");

        public static void LogInfo(string message) => WriteLogLine(message);

        private static void WriteLogLine(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
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
