using System;
using System.Collections.Generic;

namespace KioskApp.Core.Models
{
    // כל המחלקות כאן משקפות בדיוק את צורת ה-JSON שמחזירות netlify/functions/library-*.js
    // בריפו של האתר - ראו שם את ה-body שנשלח בכל תשובה. אין כאן שום לוגיקה, רק מבנה נתונים.

    public class KioskUser
    {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";
        public string? DisplayName { get; set; }
        public decimal Balance { get; set; }
        public bool IsActive { get; set; }
        /// <summary>מורשה לצאת מהקיוסק לשולחן העבודה של Windows (נקבע בדף הניהול בלבד)</summary>
        public bool CanExitKiosk { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    /// <summary>פרטי החשבון ללשונית הצפה (library-station-account-info)</summary>
    public class AccountInfo
    {
        public bool IsCommunityMember { get; set; }
        public decimal Balance { get; set; }
        public List<AccountTransaction> Transactions { get; set; } = new List<AccountTransaction>();
    }

    /// <summary>שורה ב"היסטוריית תשלומים". Type: topup / signup = זיכוי, charge = חיוב</summary>
    public class AccountTransaction
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Type { get; set; } = "";
        public decimal Amount { get; set; }
        public string Description { get; set; } = "";
        public bool IsCharge => Type == "charge";
    }

    public class AllowedApp
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ExePath { get; set; } = "";
        public string IconPath { get; set; } = "";
        public int SortOrder { get; set; }
    }

    public class KioskConfig
    {
        public decimal PricePerPage { get; set; }
        public List<AllowedApp> AllowedApps { get; set; } = new List<AllowedApp>();
        /// <summary>להציג את עורך הטקסט המובנה (נקבע בדף הניהול)</summary>
        public bool TextEditorEnabled { get; set; }
        /// <summary>מספר הטלפון לשחזור סיסמה (ימות המשיח). ריק = לא מציגים "שכחתי סיסמה"</summary>
        public string PasswordResetPhone { get; set; } = "";
        /// <summary>תעריף שימוש לדקה (לא לחברי קהילה). 0 = בלי חיוב על זמן</summary>
        public decimal UsagePricePerMinute { get; set; }
    }

    /// <summary>הודעה בשיחה עם הגבאי (from: user / admin)</summary>
    public class GabaiMessage
    {
        public string From { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTimeOffset Timestamp { get; set; }
        public bool FromAdmin => From == "admin";
    }

    public class GabaiThread
    {
        public List<GabaiMessage> Messages { get; set; } = new List<GabaiMessage>();
        public bool HasUnread { get; set; }
    }

    /// <summary>תשובת library-station-reset-info - למסך "שכחתי סיסמה"</summary>
    public class PasswordResetInfo
    {
        /// <summary>המספר שאליו מתקשרים (ימות המשיח)</summary>
        public string ResetPhone { get; set; } = "";
        /// <summary>האם נמצא חשבון פעיל עם תעודת הזהות שהוקלדה</summary>
        public bool Found { get; set; }
        /// <summary>הטלפון הרשום בחשבון, מוסתר (למשל 050-•••••67). הטלפון המלא לא נשלח לעמדה.</summary>
        public string MaskedPhone { get; set; } = "";
    }

    public class TransactionRecord
    {
        public string Id { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Type { get; set; } = ""; // "topup" | "charge"
        public string? Source { get; set; }    // "manual" | "nedarim" | "print"
        public decimal Amount { get; set; }
        public string? Description { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public int? Pages { get; set; }
    }

    public class ChargeResult
    {
        public decimal Balance { get; set; }
        public decimal Cost { get; set; }
        public TransactionRecord? Transaction { get; set; }
    }

    public class TopupInitResult
    {
        public string Token { get; set; } = "";
        public string TransactionId { get; set; } = "";
    }

    public class TopupStatusResult
    {
        // "pending" | "processing" | "confirmed" | "failed"
        public string Status { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
    }

    /// <summary>הפרטים שהמשתמש מילא במסך "פתיחת חשבון"</summary>
    public class SignupDetails
    {
        public string Tz { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class SignupCheckResult
    {
        /// <summary>מופיע ברשימת חברי הקהילה (לתגית "חבר קהילה")</summary>
        public bool IsCommunityMember { get; set; }
        /// <summary>סכום ההטענה הראשונה לפתיחת החשבון</summary>
        public decimal SignupAmount { get; set; }
        /// <summary>דף נדרים פלוס להצטרפות כחבר קהילה (הוראת קבע). ריק = בלי כפתור הצטרפות</summary>
        public string CommunityJoinUrl { get; set; } = "";
    }

    public class SignupInitResult
    {
        public string Token { get; set; } = "";
        public string TransactionId { get; set; } = "";
        public decimal Amount { get; set; }
    }

    /// <summary>תשלום בדף הרגיל של נדרים פלוס (האזור האישי, כרטיס שמור)</summary>
    public class OnlinePaymentResult
    {
        public string Token { get; set; } = "";
        public string Url { get; set; } = "";
        public decimal Amount { get; set; }
    }
}
