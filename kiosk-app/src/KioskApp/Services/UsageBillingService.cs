using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using KioskApp.Views;

namespace KioskApp.Services
{
    /// <summary>
    /// חיוב על זמן שימוש בעמדה (לא לחברי קהילה):
    /// - כל 10 דקות מתחילת החיבור - חיוב על 10 הדקות שעברו (רק אחרי שעברו בפועל).
    ///   העלות לפי התעריף לדקה מדף הניהול (ברירת מחדל 0.20 ₪ לדקה = 2 ₪ ל-10 דקות).
    /// - בתחילת כל 10 דקות: אם היתרה לא מספיקה ל-10 דקות נוספות - חלון "יתרה נמוכה" עם
    ///   מספר הדקות שנותרו וכפתור "להטענה". לא הוטען - בסוף הדקות האלה חיוב עליהן וניתוק.
    /// - בכניסה בלי יתרה בכלל - "אין יתרה מספיקה בארנק" + "לטעינה" (או ניתוק).
    /// השרת בודק שוב שהמשתמש אינו חבר קהילה ולא מוריד את היתרה מתחת ל-0.
    /// </summary>
    public sealed class UsageBillingService : IDisposable
    {
        private const int BlockMinutes = 10;

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
        private DateTime _blockStart = DateTime.Now;
        private bool _warnedThisBlock;
        private bool _busy;
        private bool _checkedAtLogin;
        private bool _disposed;

        public void Start()
        {
            _timer.Tick += async (_, _) => await TickAsync();
            _timer.Start();
            // בדיקה ראשונה אחרי שמסך בחירת התוכנה כבר מוצג
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () => await TickAsync()), DispatcherPriority.ApplicationIdle);
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Stop();
        }

        private static decimal Price => App.Session.Config?.UsagePricePerMinute ?? 0;

        /// <summary>חיוב חל רק כשיש תעריף, ורק אחרי שנבדק שהמשתמש אינו חבר קהילה</summary>
        private static bool Applies => Price > 0 && App.Session.IsCommunityMember == false;

        private async Task TickAsync()
        {
            if (_busy || _disposed || !App.Session.IsActive || !Applies) return;
            var user = App.Session.CurrentUser!;
            _busy = true;
            try
            {
                // כניסה בלי יתרה לדקת שימוש אחת לפחות
                if (!_checkedAtLogin)
                {
                    _checkedAtLogin = true;
                    if (AffordableMinutes(user.Balance) == 0)
                    {
                        if (!AskForTopupAtLogin()) { await LogoutAsync(null); return; }
                        _blockStart = DateTime.Now; // החיוב מתחיל מרגע שיש יתרה
                    }
                }

                // עברו 10 דקות - חיוב עליהן
                if (DateTime.Now - _blockStart >= TimeSpan.FromMinutes(BlockMinutes))
                {
                    var balance = await App.ApiClient.ChargeUsageAsync(user.Id, BlockMinutes);
                    App.Session.UpdateBalance(balance);
                    App.LogInfo($"חיוב זמן שימוש: {BlockMinutes} דקות, יתרה {balance:0.00}");
                    _blockStart = _blockStart.AddMinutes(BlockMinutes);
                    _warnedThisBlock = false;
                }
                if (_disposed || !App.Session.IsActive) return;

                var affordable = AffordableMinutes(App.Session.CurrentUser!.Balance);
                if (affordable >= BlockMinutes)
                {
                    _warnedThisBlock = false; // הוטען בינתיים - הכול תקין
                    return;
                }

                var usedInBlock = (int)Math.Floor((DateTime.Now - _blockStart).TotalMinutes);
                if (usedInBlock >= affordable)
                {
                    // נגמר הזמן שהיתרה מספיקה לו: חיוב על הדקות האלה וניתוק
                    if (affordable > 0)
                    {
                        var balance = await App.ApiClient.ChargeUsageAsync(user.Id, affordable);
                        App.Session.UpdateBalance(balance);
                        App.LogInfo($"חיוב זמן שימוש לפני ניתוק: {affordable} דקות, יתרה {balance:0.00}");
                    }
                    await LogoutAsync("היתרה בארנק נגמרה, ולכן החשבון נותק.\nאפשר להטעין ולהיכנס שוב.");
                    return;
                }

                if (!_warnedThisBlock)
                {
                    _warnedThisBlock = true;
                    var minutesLeft = affordable - usedInBlock;
                    var topup = KioskMessageDialog.ShowChoice(App.ActiveWindow(), KioskMessageKind.Info, "יתרה נמוכה",
                        $"היתרה בארנק ({App.Session.CurrentUser!.Balance:0.00} ₪) אינה מספיקה ל-{BlockMinutes} דקות נוספות.\n" +
                        $"החשבון יתנתק בעוד {minutesLeft} {(minutesLeft == 1 ? "דקה" : "דקות")}.",
                        "להטענה", "המשך");
                    if (topup) OpenTopup();
                }
            }
            catch (Exception ex)
            {
                // תקלת רשת וכו' - ננסה שוב בסבב הבא
                App.LogError("חיוב זמן שימוש", ex);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// בניתוק (כפתור, יציאה למחשב): חיוב על הדקות השלמות שעברו מאז החיוב האחרון -
        /// אחרת אפשר היה להתנתק רגע לפני סוף 10 הדקות ולא לשלם אף פעם. חלק של דקה לא נחשב.
        /// </summary>
        public async Task ChargeUsedMinutesAsync()
        {
            // סבב חיוב שרץ ברגע זה - מחכים שיסתיים, שלא יהיה חיוב כפול
            for (var i = 0; i < 50 && _busy; i++) await Task.Delay(100);
            if (_disposed || !_checkedAtLogin || !App.Session.IsActive || !Applies) return;
            _disposed = true; // מכאן - בלי חיובים נוספים בחיבור הזה
            _timer.Stop();

            var minutes = (int)Math.Floor((DateTime.Now - _blockStart).TotalMinutes);
            if (minutes > BlockMinutes) minutes = BlockMinutes;
            if (minutes < 1) return;
            var user = App.Session.CurrentUser!;
            try
            {
                var balance = await App.ApiClient.ChargeUsageAsync(user.Id, minutes);
                App.Session.UpdateBalance(balance);
                App.LogInfo($"חיוב זמן שימוש בניתוק: {minutes} דקות, יתרה {balance:0.00}");
            }
            catch (Exception ex)
            {
                App.LogError("חיוב זמן שימוש בניתוק", ex);
            }
        }

        private static int AffordableMinutes(decimal balance)
        {
            var price = Price;
            return price <= 0 ? int.MaxValue : (int)Math.Floor(balance / price);
        }

        /// <summary>
        /// "אין יתרה מספיקה בארנק" בכניסה: "לטעינה" פותח את חלון ההטענה, ואחריו בודקים שוב.
        /// מחזיר false אם המשתמש בחר לא להטעין (ואז מתנתקים).
        /// </summary>
        private static bool AskForTopupAtLogin()
        {
            while (App.Session.IsActive)
            {
                var topup = KioskMessageDialog.ShowChoice(App.ActiveWindow(), KioskMessageKind.Info,
                    "אין יתרה מספיקה בארנק",
                    $"השימוש בעמדה עולה {Price * BlockMinutes:0.##} ₪ ל-{BlockMinutes} דקות (לחברי הקהילה - בחינם).\n" +
                    "כדי להמשיך יש להטעין את הארנק.",
                    "לטעינה", "ניתוק");
                if (!topup) return false;
                OpenTopup();
                if (AffordableMinutes(App.Session.CurrentUser?.Balance ?? 0) > 0) return true;
            }
            return false;
        }

        private static void OpenTopup()
        {
            var owner = App.ActiveWindow();
            var window = new TopupWindow();
            if (owner != null && owner.IsVisible) window.Owner = owner;
            window.ShowDialog();
        }

        private async Task LogoutAsync(string? message)
        {
            Dispose();
            await App.LogoutAsync(message);
        }
    }
}
