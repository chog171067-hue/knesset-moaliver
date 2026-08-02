using System;
using KioskApp.Core.Models;

namespace KioskApp.Core
{
    /// <summary>
    /// מצב המשתמש המחובר כרגע לעמדה - אובייקט משותף יחיד שכל חלונות ה-WPF (Overlay,
    /// מסך בחירת תוכנה, ניטור הדפסה) קוראים/מעדכנים ממנו, כדי שלא יהיו כמה "מקורות אמת"
    /// שונים ליתרה/זמן החיבור בתוך אותה עמדה. אין כאן קריאות רשת - זה state מקומי בזיכרון,
    /// שמסונכרן מול השרת ע"י הקוד הקורא (KioskApiClient) בכל פעולה.
    /// </summary>
    public class KioskSession
    {
        public KioskUser? CurrentUser { get; private set; }
        public KioskConfig? Config { get; private set; }
        public DateTimeOffset LoginTime { get; private set; }

        public bool IsActive => CurrentUser != null;
        public TimeSpan Elapsed => IsActive ? DateTimeOffset.Now - LoginTime : TimeSpan.Zero;

        public event EventHandler<decimal>? BalanceChanged;
        public event EventHandler? SessionEnded;

        public void Start(KioskUser user, KioskConfig config)
        {
            CurrentUser = user;
            Config = config;
            LoginTime = DateTimeOffset.Now;
        }

        public void UpdateBalance(decimal newBalance)
        {
            if (CurrentUser == null) return;
            CurrentUser.Balance = newBalance;
            BalanceChanged?.Invoke(this, newBalance);
        }

        public void End()
        {
            CurrentUser = null;
            Config = null;
            SessionEnded?.Invoke(this, EventArgs.Empty);
        }
    }
}
