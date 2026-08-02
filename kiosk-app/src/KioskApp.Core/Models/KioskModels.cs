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
        public decimal Balance { get; set; }
        public bool IsActive { get; set; }
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
}
