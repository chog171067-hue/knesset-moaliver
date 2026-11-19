using System;

namespace KioskApp.Core
{
    /// <summary>שגיאה כללית שהוחזרה מהשרת (netlify/functions/library-*.js) עם success:false.</summary>
    public class KioskApiException : Exception
    {
        public int StatusCode { get; }

        public KioskApiException(string message, int statusCode) : base(message)
        {
            StatusCode = statusCode;
        }
    }

    /// <summary>שם משתמש/סיסמה שגויים, או חשבון מושבת - ראו library-station-login.js.</summary>
    public class KioskAuthException : KioskApiException
    {
        public KioskAuthException(string message, int statusCode) : base(message, statusCode) { }
    }

    /// <summary>
    /// הכניסה הצליחה עם סיסמה זמנית (שחזור בטלפון דרך ימות המשיח) - השרת לא מחזיר
    /// את פרטי המשתמש עד שתיקבע סיסמה חדשה (ChangePasswordAsync). הקורא חייב לתפוס
    /// את זה בנפרד ולהציג מסך "בחירת סיסמה חדשה" - זו לא תקלה.
    /// </summary>
    public class PasswordChangeRequiredException : KioskApiException
    {
        public PasswordChangeRequiredException()
            : base("נכנסת עם סיסמה זמנית - יש לבחור סיסמה חדשה", 200) { }
    }

    /// <summary>
    /// יתרה לא מספיקה לחיוב הדפסה (library-print-charge.js, err.insufficientFunds).
    /// חובה על הקורא לתפוס את זה בנפרד מ-KioskApiException הכללית, כדי להציג את מסך
    /// "הטענת כסף" במקום הודעת שגיאה סתמית - זו לא תקלה, זו התנהגות צפויה.
    /// </summary>
    public class InsufficientFundsException : KioskApiException
    {
        public decimal Balance { get; }
        public decimal Cost { get; }

        public InsufficientFundsException(string message, decimal balance, decimal cost)
            : base(message, 402)
        {
            Balance = balance;
            Cost = cost;
        }
    }
}
