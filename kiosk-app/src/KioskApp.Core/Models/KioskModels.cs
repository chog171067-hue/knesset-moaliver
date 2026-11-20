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
    }

    public class SignupInitResult
    {
        public string Token { get; set; } = "";
        public string TransactionId { get; set; } = "";
        public decimal Amount { get; set; }
    }
}
