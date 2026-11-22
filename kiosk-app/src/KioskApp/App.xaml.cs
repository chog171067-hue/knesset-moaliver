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

            StartScreenWatchdog();

            // בעמדה נעולה הקיוסק הוא ה"שולחן העבודה" של Windows: Alt+F4 על אחד מחלונות הקיוסק
            // היה סוגר את התוכנה כולה ומשאיר מסך שחור (נמצא בהפעלה הראשונה במחשב הספרייה).
            // לכן חוסמים אותו בכל חלונות הקיוסק. לפני הנעילה (לא Shell) - עובד כרגיל, לבדיקות.
            // תוכנות שהקיוסק פותח (אוצריא וכו') הן תהליכים נפרדים - אצלן Alt+F4 לא מושפע.
            if (IsRunningAsKioskShell())
            {
                EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent,
                    new System.Windows.Input.KeyEventHandler(BlockAltF4), handledEventsToo: true);
            }
        }

        // --- רשת ביטחון: הקיוסק לא נשאר סגור בעמדה נעולה ---
        private static readonly DateTime StartedAt = DateTime.Now;
        private static bool _intentionalExit;

        /// <summary>
        /// בעמדה נעולה אין שולחן עבודה: אם הקיוסק נסגר (בכל דרך שלא חסמנו, או קריסה) - נשאר
        /// מסך שחור, ורק כיבוי פיזי מחזיר אותו. לכן ביציאה לא מכוונת הקיוסק מפעיל את עצמו מחדש.
        /// לא מפעילים מחדש: בהתנתקות/כיבוי של Windows, ב"יציאה למחשב", ואם הקיוסק נסגר בתוך
        /// 30 שניות מההפעלה (כנראה תקלה בהפעלה עצמה - כדי לא להיכנס ללולאה).
        /// </summary>
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            _intentionalExit = true;
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (!_intentionalExit && IsRunningAsKioskShell() && DateTime.Now - StartedAt > TimeSpan.FromSeconds(30))
                {
                    var exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe))
                    {
                        LogInfo("הקיוסק נסגר שלא במכוון בעמדה נעולה - מפעיל מחדש");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                        {
                            UseShellExecute = true,
                            WorkingDirectory = AppContext.BaseDirectory
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("הפעלה מחדש של הקיוסק", ex);
            }
            base.OnExit(e);
        }

        private static void BlockAltF4(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.System && e.SystemKey == System.Windows.Input.Key.F4)
            {
                e.Handled = true;
                LogInfo("Alt+F4 נחסם (עמדה נעולה)");
            }
        }

        // --- שירותים שפועלים לאורך כל ההתחברות (לא רק כשתוכנה פתוחה) ---
        private static PrintMonitorService? _printMonitor;

        /// <summary>
        /// נקרא בהתחברות: חיוב על הדפסה פועל בכל מסך - גם מעורך הטקסט שנפתח ממסך הבחירה,
        /// וגם במעבר בין תוכנות (קודם הוא היה חלק מהסרגל העליון ונעצר בכל החלפת תוכנה).
        /// </summary>
        public static void StartSessionServices()
        {
            StopSessionServices();
            StartUsageReminder();
            // כל רבע שנייה: חלון ההדפסה של Windows מאושר, חלונות שמירה נשמרים לדיסק-און-קי,
            // וחלונות "מצב הדפסה" של הדרייבר נסגרים - בכל מסך (גם בעורך הטקסט)
            _dialogTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _dialogTimer.Tick += (_, _) =>
            {
                if (_inDialogTick) return;
                _inDialogTick = true;
                try
                {
                    FileDialogAssistant.Tick(WindowGuard.IsUserProcess);
                    WindowGuard.CloseDriverPopups();
                }
                catch (Exception ex)
                {
                    LogError("טיפול בחלונות הדפסה/שמירה", ex);
                }
                finally
                {
                    _inDialogTick = false;
                }
            };
            _dialogTimer.Start();
            _usageBilling = new UsageBillingService();
            _usageBilling.Start();
            _printMonitor = new PrintMonitorService(ApiClient, () => Session.CurrentUser?.Id ?? "");
            _printMonitor.PrintCharged += (_, e) => Session.UpdateBalance(e.NewBalance);
            _printMonitor.PrintRefunded += (_, e) =>
            {
                Session.UpdateBalance(e.NewBalance);
                ShowMessage("ההדפסה בוטלה והחיוב הוחזר ליתרה שלך.\n\nאם התקלה חוזרת - נא לפנות לגבאים.",
                    "ההדפסה בוטלה", MessageBoxImage.Information);
            };
            _printMonitor.PrintBlocked += (_, e) => ShowMessage(
                $"היתרה שלך ({e.Balance:0.00} ₪) אינה מספיקה לעלות ההדפסה ({e.Cost:0.00} ₪).\nההדפסה נחסמה. לחצו על \"הטענת כסף\" כדי להמשיך.",
                "יתרה לא מספיקה", MessageBoxImage.Warning);
            _printMonitor.AskCancelStuckPrint = reason =>
            {
                var dialog = new PrintProblemDialog(reason);
                var owner = ActiveWindow();
                if (owner != null && owner.IsVisible) dialog.Owner = owner;
                dialog.ShowDialog();
                return dialog.CancelRequested;
            };
            _printMonitor.MonitorError += (_, msg) => LogError("PrintMonitorService", new InvalidOperationException(msg));
            _printMonitor.Start();
        }

        /// <summary>נקרא בניתוק: עבודות הדפסה תקועות מבוטלות ומוחזרות, והמעקב נעצר.</summary>
        public static void StopSessionServices()
        {
            _printMonitor?.Dispose();
            _printMonitor = null;
            _usageReminder?.Stop();
            _usageReminder = null;
            _dialogTimer?.Stop();
            _dialogTimer = null;
            _usageBilling?.Dispose();
            _usageBilling = null;
        }

        private static UsageBillingService? _usageBilling;
        private static System.Windows.Threading.DispatcherTimer? _dialogTimer;
        private static bool _inDialogTick;

        /// <summary>לפני ניתוק / יציאה למחשב: חיוב על הדקות השלמות שעוד לא חויבו</summary>
        public static async Task ChargeUsedMinutesAsync()
        {
            try
            {
                if (_usageBilling != null) await _usageBilling.ChargeUsedMinutesAsync();
            }
            catch (Exception ex)
            {
                LogError("חיוב דקות לפני ניתוק", ex);
            }
        }
        private static bool _loggingOut;

        /// <summary>
        /// ניתוק מהחשבון - מכל מקום (הלשונית הצפה, ניתוק אוטומטי כשהיתרה נגמרת): סוגר את
        /// התוכנות, מנקה את מה שהמשתמש השאיר, ומחזיר למסך הכניסה. message - הודעה אחרי הניתוק.
        /// </summary>
        public static async Task LogoutAsync(string? message = null)
        {
            if (_loggingOut || !Session.IsActive) return;
            _loggingOut = true;
            try
            {
                LogInfo("ניתוק מהחשבון" + (message != null ? " (אוטומטי)" : ""));
                foreach (var overlay in System.Linq.Enumerable.OfType<OverlayWindow>(Current.Windows))
                {
                    overlay.StopGuard(); // שהשומר לא "יחזיר" חלונות של תוכנות שנסגרות
                }
                await ChargeUsedMinutesAsync();
                StopSessionServices();
                await ProcessLauncher.CloseAllTrackedAsync();
                await WindowGuard.CloseUserAppsAsync(); // גם חלונות שנפתחו בתהליכים אחרים (Launcher)
                // תהליכים של התוכנות המאושרות שנשארו ברקע בלי חלון (למשל באמצע טעינה) - נסגרים,
                // כדי שלא יישארו פתוחים למשתמש הבא ולא ימנעו הפעלה חדשה
                foreach (var allowed in Session.Config?.AllowedApps ?? new System.Collections.Generic.List<KioskApp.Core.Models.AllowedApp>())
                {
                    try
                    {
                        var n = WindowGuard.KillProcessesFrom(WindowGuard.AppFolderRoot(allowed.ExePath));
                        if (n > 0) LogInfo($"ניתוק: נסגרו {n} תהליכים שנשארו של {allowed.DisplayName}");
                    }
                    catch (Exception ex)
                    {
                        LogError($"ניתוק: כשל בסגירת תהליכים של {allowed.DisplayName}", ex);
                    }
                }
                WindowGuard.ClearAppSessionState();     // טקסט שתוכנות שמרו + נתוני הדפדפן (נדרים פלוס)
                WindowGuard.ClearUserFiles();           // קבצים שנשמרו בתיקיות של משתמש הקיוסק (רק במחשב נעול)
                TextEditorWindow.EndSession();          // הטקסט בעורך נמחק
                Session.End();
                OverlayWindow.ResetPosition();

                var login = new LoginWindow();
                Current.MainWindow = login;
                login.Show();
                foreach (var w in System.Linq.Enumerable.ToList(System.Linq.Enumerable.OfType<Window>(Current.Windows)))
                {
                    if (w != login) w.Close();
                }
                if (message != null)
                {
                    KioskMessageDialog.ShowMessage(login, KioskMessageKind.Info, "החשבון נותק", message);
                }
            }
            catch (Exception ex)
            {
                LogError("כשל בניתוק מהחשבון", ex);
            }
            finally
            {
                _loggingOut = false;
            }
        }

        // כל 10 דקות שימוש - תזכורת לשים לב אם אחרים ממתינים לעמדה
        private static readonly TimeSpan UsageReminderAfter = TimeSpan.FromMinutes(10);
        private static System.Windows.Threading.DispatcherTimer? _usageReminder;
        private static bool _reminderOpen;

        private static void StartUsageReminder()
        {
            _reminderOpen = false;
            _usageReminder = new System.Windows.Threading.DispatcherTimer { Interval = UsageReminderAfter };
            _usageReminder.Tick += (_, _) =>
            {
                if (!Session.IsActive || _reminderOpen) return;
                _reminderOpen = true;
                App.LogInfo("תזכורת 10 דקות שימוש");
                Views.KioskMessageDialog.ShowMessage(ActiveWindow(), Views.KioskMessageKind.Info, "תזכורת",
                    "בבקשה שים לב אם אין סביבך משתמשים אחרים הזקוקים לעמדה.", "המשך");
                _reminderOpen = false;
            };
            _usageReminder.Start();
        }

        /// <summary>החלון הפעיל של הקיוסק (לבעלות על הודעות), או החלון הראשי</summary>
        public static Window? ActiveWindow()
        {
            foreach (Window w in Current.Windows)
            {
                if (w.IsActive && w.IsVisible) return w;
            }
            return Current.MainWindow;
        }

        private static void ShowMessage(string text, string title, MessageBoxImage icon)
        {
            var owner = ActiveWindow();
            if (owner != null && owner.IsVisible)
                MessageBox.Show(owner, text, title, MessageBoxButton.OK, icon);
            else
                MessageBox.Show(text, title, MessageBoxButton.OK, icon);
        }

        /// <summary>
        /// "יציאה למחשב" (למשתמש קיוסק מורשה בלבד): מבקש ממשימה מתוזמנת (בהרשאות מערכת,
        /// שמותקנת ע"י Setup-KioskUser.ps1) לכבות את ההתחברות האוטומטית לחצי דקה, ומתנתק
        /// מ-Windows. כך מופיע מסך הכניסה הרגיל של Windows, ונכנסים לחשבון המנהל עם הסיסמה
        /// שלו - עם שולחן העבודה המלא. הפעלה מחדש של המחשב תמיד חוזרת לקיוסק.
        /// מחזיר הודעת שגיאה, או null אם ההתנתקות התחילה.
        /// </summary>
        public static string? SignOutToWindowsSignIn()
        {
            const string taskName = "KioskMaintenanceSignIn";
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/run /tn {taskName}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                p?.WaitForExit(10000);
                if (p == null || p.ExitCode != 0)
                {
                    LogInfo($"יציאה למחשב: הפעלת המשימה {taskName} נכשלה (קוד {p?.ExitCode})");
                    return "היציאה למחשב זמינה רק במחשב שננעל עם סקריפט הנעילה (Setup-KioskUser.ps1).";
                }
            }
            catch (Exception ex)
            {
                LogError("יציאה למחשב: כשל בהפעלת המשימה", ex);
                return "היציאה למחשב נכשלה: " + ex.Message;
            }

            LogInfo($"יציאה למחשב: {Session.CurrentUser?.Username} - מתנתק למסך הכניסה של Windows");
            TextEditorWindow.EndSession();
            StopSessionServices();
            Session.End();

            // נותנים למשימה רגע לכבות את ההתחברות האוטומטית לפני ההתנתקות
            System.Threading.Thread.Sleep(2000);
            _intentionalExit = true; // התנתקות מכוונת - לא להפעיל את הקיוסק מחדש
            if (!ExitWindowsEx(EwxLogoff | EwxForceIfHung, 0))
            {
                return "ההתנתקות מ-Windows נכשלה. אפשר להתנתק ידנית: Ctrl+Alt+Delete ← יציאה.";
            }
            return null;
        }

        /// <summary>
        /// האם הקיוסק רץ כ-Shell של המשתמש הנוכחי (כלומר: מחשב נעול, משתמש הקיוסק).
        /// פעולות הרסניות (כמו מחיקת קבצי משתמש) מותרות רק כשזה נכון.
        /// </summary>
        public static bool IsRunningAsKioskShell()
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

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);
        private const uint EwxLogoff = 0x00000000;
        private const uint EwxForceIfHung = 0x00000010;

        // --- שומר מסך: אף פעם לא להשאיר את המשתמש מול מסך שחור ---
        private static DateTime? _noScreenSince;
        private static DispatcherTimer? _screenWatchdog;

        /// <summary>
        /// כל 2 שניות: בודק שלפחות חלון אחד של הקיוסק גלוי ולא ממוזער (התחברות, בחירת תוכנה,
        /// סרגל עליון, דיאלוג). במחשב נעול אין שולחן עבודה - אם אף מסך של הקיוסק לא גלוי,
        /// המשתמש רואה מסך שחור בלי שום דרך להמשיך. זה קרה בפועל אחרי סגירת תוכנה עם דיאלוג
        /// "לשמור?". במקרה כזה מחזירים את המסך המתאים ורושמים ביומן מה נמצא, לאבחון.
        /// </summary>
        private static void StartScreenWatchdog()
        {
            _screenWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _screenWatchdog.Tick += (_, _) =>
            {
                try
                {
                    CheckScreen();
                }
                catch (Exception ex)
                {
                    LogError("שומר מסך", ex);
                }
            };
            _screenWatchdog.Start();
        }

        private static void CheckScreen()
        {

            var windows = new System.Collections.Generic.List<Window>();
            foreach (Window w in Current.Windows) windows.Add(w);

            var anyShown = windows.Exists(w => w.IsVisible && w.WindowState != WindowState.Minimized);
            if (anyShown)
            {
                _noScreenSince = null;
                return;
            }

            // מעברים בין מסכים לוקחים רגע - מתקנים רק אם המצב נמשך
            _noScreenSince ??= DateTime.Now;
            if (DateTime.Now - _noScreenSince.Value < TimeSpan.FromSeconds(3)) return;
            _noScreenSince = null;

            var states = string.Join("; ", windows.ConvertAll(w => $"{w.GetType().Name}(visible={w.IsVisible}, state={w.WindowState})"));
            LogInfo($"שומר מסך: אף מסך של הקיוסק לא גלוי - משחזר. חלונות: [{states}]");

            if (Session.IsActive)
            {
                var picker = windows.Find(w => w is AppPickerWindow) as AppPickerWindow;
                if (picker == null)
                {
                    picker = new AppPickerWindow();
                }
                foreach (var w in windows)
                {
                    if (w is OverlayWindow) w.Close(); // סרגל של תוכנה שכבר לא מוצגת
                }
                Current.MainWindow = picker;
                picker.ShowAgain();
            }
            else
            {
                // קודם מציגים את מסך ההתחברות ורק אחר כך סוגרים את השאר - אחרת סגירת
                // החלון האחרון הייתה מסיימת את האפליקציה כולה
                var login = new LoginWindow();
                Current.MainWindow = login;
                login.Show();
                foreach (var w in windows)
                {
                    if (w != login) w.Close();
                }
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
