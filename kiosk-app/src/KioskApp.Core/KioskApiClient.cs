using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KioskApp.Core.Models;

namespace KioskApp.Core
{
    /// <summary>
    /// לקוח HTTP דק מול ה-Netlify Functions של הקיוסק (netlify/functions/library-*.js
    /// בריפו של האתר). כל הבקשות כאן נשלחות עם כותרת x-station-key (ראו
    /// netlify/functions/lib/station-auth.js בצד השרת) - זהו "מפתח העמדה" הקבוע,
    /// לא טוקן session - ראו ההערה בראש library-station-login.js.
    /// </summary>
    public class KioskApiClient : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _http;
        private readonly bool _ownsHttpClient;

        public KioskApiClient(string baseUrl, string stationApiKey, HttpMessageHandler? handler = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("Base URL is required", nameof(baseUrl));
            if (string.IsNullOrWhiteSpace(stationApiKey)) throw new ArgumentException("Station API key is required", nameof(stationApiKey));

            _http = handler != null ? new HttpClient(handler, disposeHandler: false) : new HttpClient();
            _ownsHttpClient = true;
            _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            _http.DefaultRequestHeaders.Add("x-station-key", stationApiKey);
            _http.Timeout = TimeSpan.FromSeconds(20);
        }

        /// <summary>
        /// זורק PasswordChangeRequiredException כשנכנסו עם סיסמה זמנית (שחזור בטלפון) -
        /// אז חובה לקרוא ל-ChangePasswordAsync לפני שפותחים את הקיוסק.
        /// </summary>
        public async Task<KioskUser> LoginAsync(string username, string password, CancellationToken ct = default)
        {
            var response = await PostAsync<LoginResponse>("/.netlify/functions/library-station-login", new { username, password }, ct);
            if (response.Success && response.MustChangePassword)
            {
                throw new PasswordChangeRequiredException();
            }
            if (!response.Success || response.User == null)
            {
                throw new KioskAuthException(response.Error ?? "שם משתמש או סיסמה שגויים", 401);
            }
            return response.User;
        }

        /// <summary>
        /// המשתמש קובע לעצמו סיסמה חדשה (currentPassword = הסיסמה הזמנית או הרגילה).
        /// מחזיר את המשתמש - ומכאן ממשיכים כמו אחרי כניסה רגילה.
        /// </summary>
        public async Task<KioskUser> ChangePasswordAsync(string username, string currentPassword, string newPassword, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<LoginResponse>(
                "/.netlify/functions/library-station-change-password", new { username, currentPassword, newPassword }, ct);
            if (!response.Success || response.User == null)
            {
                if (statusCode == 401) throw new KioskAuthException(response.Error ?? "הסיסמה הזמנית שגויה או שפג תוקפה", 401);
                throw new KioskApiException(response.Error ?? "החלפת הסיסמה נכשלה", statusCode);
            }
            return response.User;
        }

        /// <summary>
        /// למסך "שכחתי סיסמה": לאיזה מספר להתקשר, והטלפון הרשום בחשבון (מוסתר).
        /// </summary>
        /// <summary>
        /// מסך הפתיחה: האם כבר יש חשבון לתעודת הזהות שהוקלדה (כן ← סיסמה, לא ← פתיחת חשבון)
        /// </summary>
        public async Task<bool> AccountExistsAsync(string username, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<AccountCheckResponse>(
                "/.netlify/functions/library-station-account-check", new { username }, ct);
            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "כשל בבדיקת תעודת הזהות", statusCode);
            }
            return response.Exists;
        }

        /// <summary>
        /// הלשונית הצפה: חבר קהילה, יתרה עדכנית והעסקאות האחרונות של המשתמש המחובר
        /// </summary>
        public async Task<AccountInfo> GetAccountInfoAsync(string userId, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<AccountInfoResponse>(
                "/.netlify/functions/library-station-account-info", new { userId }, ct);
            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "כשל בטעינת פרטי החשבון", statusCode);
            }
            return new AccountInfo
            {
                IsCommunityMember = response.IsCommunityMember,
                Balance = response.Balance,
                Transactions = response.Transactions ?? new System.Collections.Generic.List<AccountTransaction>()
            };
        }

        public async Task<PasswordResetInfo> GetPasswordResetInfoAsync(string username, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<ResetInfoResponse>(
                "/.netlify/functions/library-station-reset-info", new { username }, ct);
            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "כשל בטעינת פרטי שחזור הסיסמה", statusCode);
            }
            return new PasswordResetInfo
            {
                ResetPhone = response.ResetPhone ?? "",
                Found = response.Found,
                MaskedPhone = response.MaskedPhone ?? ""
            };
        }

        /// <summary>
        /// פתיחת חשבון, כפתור "המשך": בדיקת הפרטים בשרת (ת"ז, כפילות, חסימה) והאם זה חבר קהילה.
        /// לא יוצר חשבון.
        /// </summary>
        public async Task<SignupCheckResult> CheckSignupAsync(SignupDetails details, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<SignupCheckResponse>(
                "/.netlify/functions/library-station-signup-check", ToPayload(details), ct);
            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "בדיקת הפרטים נכשלה", statusCode);
            }
            return new SignupCheckResult
            {
                IsCommunityMember = response.IsCommunityMember,
                SignupAmount = response.SignupAmount,
                CommunityJoinUrl = response.CommunityJoinUrl ?? "",
                PrepaidAmount = response.PrepaidAmount,
                Prepaid = response.Prepaid
            };
        }

        /// <summary>
        /// פתיחת חשבון על חשבון תשלום ממתין מנדרים פלוס (לפחות סכום פתיחת החשבון). מחזיר כמה נטען.
        /// </summary>
        public async Task<decimal> SignupPrepaidAsync(SignupDetails details, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<PrepaidSignupResponse>(
                "/.netlify/functions/library-station-signup-prepaid", ToPayload(details), ct);
            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "פתיחת החשבון נכשלה", statusCode);
            }
            return response.Loaded;
        }

        /// <summary>
        /// לפני פתיחת דף ההצטרפות: מזהי הוראות הקבע שכבר קיימות לת"ז (רק הוראה חדשה תיחשב).
        /// </summary>
        public async Task<List<string>> GetExistingKevaIdsAsync(string tz, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<KevaSnapshotResponse>(
                "/.netlify/functions/library-station-member-check", new { tz, action = "snapshot" }, ct);
            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "כשל בבדיקת הוראות הקבע", statusCode);
            }
            return response.KevaIds ?? new List<string>();
        }

        /// <summary>
        /// אחרי דף ההצטרפות: האם נוספה הוראת קבע "דמי חבר" (ואז השרת רושם חברות זמנית).
        /// </summary>
        public async Task<bool> CheckCommunityMembershipAsync(string tz, IEnumerable<string> knownKevaIds, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<MemberCheckResponse>(
                "/.netlify/functions/library-station-member-check", new { tz, knownKevaIds }, ct);
            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "כשל בבדיקת החברות", statusCode);
            }
            return response.IsCommunityMember;
        }

        /// <summary>
        /// פתיחת חשבון, כפתור "להטענה": מקים עסקה בנדרים פלוס על סכום פתיחת החשבון.
        /// החשבון נוצר בשרת רק כשהתשלום מאושר - בודקים עם GetTopupStatusAsync(Token).
        /// </summary>
        public async Task<SignupInitResult> InitSignupAsync(SignupDetails details, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<SignupInitResponse>(
                "/.netlify/functions/library-station-signup-init", ToPayload(details), ct);
            if (!response.Success || string.IsNullOrEmpty(response.Token) || string.IsNullOrEmpty(response.TransactionId))
            {
                throw new KioskApiException(response.Error ?? "פתיחת עסקת התשלום נכשלה", statusCode);
            }
            return new SignupInitResult { Token = response.Token!, TransactionId = response.TransactionId!, Amount = response.Amount };
        }

        /// <summary>
        /// "תשלום בכרטיס שמור": מכין תשלום בדף הרגיל של נדרים פלוס (האזור האישי) ומחזיר את
        /// הקישור. signup = פתיחת חשבון (אחרת הטענה ל-userId). המצב נבדק ב-GetTopupStatusAsync(Token).
        /// </summary>
        public async Task<OnlinePaymentResult> InitOnlinePaymentAsync(string? userId, decimal amount, SignupDetails? signup, CancellationToken ct = default)
        {
            object payload = signup != null ? new { signup = ToPayload(signup) } : new { userId, amount };
            var (statusCode, response) = await PostRawAsync<OnlinePaymentResponse>(
                "/.netlify/functions/library-station-online-topup-init", payload, ct);
            if (!response.Success || string.IsNullOrEmpty(response.Token) || string.IsNullOrEmpty(response.Url))
            {
                throw new KioskApiException(response.Error ?? "הכנת התשלום בנדרים פלוס נכשלה", statusCode);
            }
            return new OnlinePaymentResult { Token = response.Token!, Url = response.Url!, Amount = response.Amount };
        }

        /// <summary>
        /// חיוב מכרטיס הוראת הקבע: האם למשתמש יש הוראת קבע פעילה במוסד הראשי עם כרטיס בתוקף.
        /// בכשל תקשורת מחזיר "לא זכאי" (הכפתור פשוט לא יוצג).
        /// </summary>
        public Task<KevaChargeInfo> CheckKevaChargeAsync(string userId, CancellationToken ct = default) =>
            CheckKevaChargeCoreAsync(new { action = "check", userId }, ct);

        /// <summary>פתיחת חשבון: הוראת הקבע נמצאת לפי הת"ז והטלפון שהוקלדו. Amount = סכום הפתיחה</summary>
        public Task<KevaChargeInfo> CheckKevaChargeForSignupAsync(SignupDetails signup, CancellationToken ct = default) =>
            CheckKevaChargeCoreAsync(new { action = "check", signup = ToPayload(signup) }, ct);

        private async Task<KevaChargeInfo> CheckKevaChargeCoreAsync(object payload, CancellationToken ct)
        {
            var (_, response) = await PostRawAsync<KevaChargeCheckResponse>(
                "/.netlify/functions/library-station-keva-charge", payload, ct);
            return new KevaChargeInfo
            {
                Eligible = response.Success && response.Eligible && !string.IsNullOrEmpty(response.Last4),
                Last4 = response.Last4 ?? "",
                Consented = response.Consented,
                MaxPerCharge = response.MaxPerCharge,
                MaxPerDay = response.MaxPerDay,
                ChargedToday = response.ChargedToday,
                Amount = response.Amount,
                Reason = response.Success ? response.Reason : response.Error
            };
        }

        /// <summary>שמירת ההסכמה החד-פעמית לחיוב מכרטיס הוראת הקבע</summary>
        public async Task SaveKevaChargeConsentAsync(string userId, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<ApiEnvelope>(
                "/.netlify/functions/library-station-keva-charge", new { action = "consent", userId }, ct);
            if (!response.Success) throw new KioskApiException(response.Error ?? "שמירת האישור נכשלה", statusCode);
        }

        /// <summary>חיוב בודד מכרטיס הוראת הקבע. לא זורק על סירוב - התוצאה מתארת אותו</summary>
        public Task<KevaChargeResult> ChargeKevaAsync(string userId, decimal amount, CancellationToken ct = default) =>
            ChargeKevaCoreAsync(new { action = "charge", userId, amount }, ct);

        /// <summary>פתיחת חשבון בחיוב סכום הפתיחה מכרטיס הוראת הקבע - החשבון נוצר בשרת אם החיוב הצליח</summary>
        public Task<KevaChargeResult> ChargeKevaForSignupAsync(SignupDetails signup, CancellationToken ct = default) =>
            ChargeKevaCoreAsync(new { action = "charge", signup = ToPayload(signup) }, ct);

        private async Task<KevaChargeResult> ChargeKevaCoreAsync(object payload, CancellationToken ct)
        {
            var (_, response) = await PostRawAsync<KevaChargeResponse>(
                "/.netlify/functions/library-station-keva-charge", payload, ct);
            return new KevaChargeResult
            {
                Success = response.Success,
                Uncertain = response.Uncertain,
                Error = response.Error,
                Amount = response.Amount,
                Balance = response.Balance
            };
        }

        private static object ToPayload(SignupDetails d) =>
            new { tz = d.Tz, displayName = d.DisplayName, phone = d.Phone, password = d.Password };

        public async Task<KioskConfig> GetConfigAsync(CancellationToken ct = default)
        {
            var response = await GetAsync<ConfigResponse>("/.netlify/functions/library-config", ct);
            if (!response.Success || response.Config == null)
            {
                throw new KioskApiException(response.Error ?? "כשל בטעינת הגדרות הקיוסק", 500);
            }
            return response.Config;
        }

        /// <summary>
        /// מבצע חיוב הדפסה לפני שליחה בפועל לתור ההדפסה (ראו סעיף 3.6 באפיון).
        /// זורק InsufficientFundsException (לא KioskApiException כללית) כשהיתרה לא
        /// מספיקה - זה מצב צפוי (לא תקלה), והקורא אמור לתפוס אותו בנפרד כדי להציג
        /// ישירות את מסך "הטענת כסף" עם היתרה/העלות המדויקים.
        /// </summary>
        public async Task<ChargeResult> ChargePrintAsync(string userId, int pages, string? title = null, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<ChargeResponse>(
                "/.netlify/functions/library-print-charge", new { userId, pages, title }, ct);

            if (statusCode == 402 || response.InsufficientFunds)
            {
                throw new InsufficientFundsException(response.Error ?? "היתרה אינה מספיקה לביצוע ההדפסה", response.Balance, response.Cost);
            }
            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "חיוב ההדפסה נכשל", statusCode);
            }

            return new ChargeResult { Balance = response.Balance, Cost = response.Cost, Transaction = response.Transaction };
        }

        /// <summary>
        /// חיוב על זמן שימוש (בסוף כל 10 דקות, או על הדקות שנותרו כשהיתרה נגמרת).
        /// השרת לא מחייב חבר קהילה ולא יורד מתחת ל-0. מחזיר את היתרה החדשה.
        /// </summary>
        public async Task<decimal> ChargeUsageAsync(string userId, int minutes, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<ChargeResponse>(
                "/.netlify/functions/library-station-usage-charge", new { userId, minutes }, ct);
            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "חיוב זמן השימוש נכשל", statusCode);
            }
            return response.Balance;
        }

        /// <summary>
        /// יתרה מהמערכת הישנה (סייפר) לפי שם פרטי + משפחה. null = אין יתרה שלא מומשה.
        /// </summary>
        public async Task<(string LegacyId, decimal Amount)?> CheckLegacyBalanceAsync(string userId, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<LegacyResponse>(
                "/.netlify/functions/library-station-legacy-balance", new { userId, action = "check" }, ct);
            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "כשל בבדיקת יתרה מהמערכת הישנה", statusCode);
            }
            return response.Found && !string.IsNullOrEmpty(response.LegacyId)
                ? (response.LegacyId!, response.Amount)
                : null;
        }

        /// <summary>מימוש היתרה מהמערכת הישנה - נוספת ליתרה. מחזיר את היתרה החדשה.</summary>
        public async Task<decimal> ClaimLegacyBalanceAsync(string userId, string legacyId, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<LegacyResponse>(
                "/.netlify/functions/library-station-legacy-balance", new { userId, action = "claim", legacyId }, ct);
            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "מימוש היתרה נכשל", statusCode);
            }
            return response.Balance;
        }

        /// <summary>השיחה עם הגבאי (markRead: סימון תשובות הגבאי כנקראו)</summary>
        public async Task<GabaiThread> GetGabaiThreadAsync(string userId, bool markRead, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<GabaiThreadResponse>(
                "/.netlify/functions/library-station-gabai", new { userId, action = "get", markRead }, ct);
            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "כשל בטעינת ההודעות", statusCode);
            }
            return new GabaiThread { Messages = response.Messages ?? new System.Collections.Generic.List<GabaiMessage>(), HasUnread = response.HasUnread };
        }

        public async Task<GabaiThread> SendGabaiMessageAsync(string userId, string text, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<GabaiThreadResponse>(
                "/.netlify/functions/library-station-gabai", new { userId, action = "send", text }, ct);
            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "שליחת ההודעה נכשלה", statusCode);
            }
            return new GabaiThread { Messages = response.Messages ?? new System.Collections.Generic.List<GabaiMessage>(), HasUnread = response.HasUnread };
        }

        /// <summary>
        /// החזר על חיוב הדפסה שנכשלה (המדפסת מנותקת/תקלה). מחזיר את היתרה החדשה.
        /// השרת מאפשר החזר אחד בלבד לכל חיוב - קריאה כפולה מחזירה שגיאה ולא כסף כפול.
        /// </summary>
        public async Task<decimal> RefundPrintAsync(string userId, string transactionId, CancellationToken ct = default)
        {
            var response = await PostAsync<RefundResponse>(
                "/.netlify/functions/library-print-refund", new { userId, transactionId }, ct);

            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "החזר החיוב נכשל", 400);
            }
            return response.Balance;
        }

        public async Task<TopupInitResult> InitTopupAsync(string userId, decimal amount, CancellationToken ct = default)
        {
            var response = await PostAsync<TopupInitResponse>(
                "/.netlify/functions/library-topup-init", new { userId, amount }, ct);

            if (!response.Success || string.IsNullOrEmpty(response.Token) || string.IsNullOrEmpty(response.TransactionId))
            {
                throw new KioskApiException(response.Error ?? "פתיחת עסקת ההטענה נכשלה", 400);
            }

            return new TopupInitResult { Token = response.Token!, TransactionId = response.TransactionId! };
        }

        /// <summary>
        /// בודק (polling) האם ה-CallBack מנדרים פלוס כבר אושר בפועל - ראו ההערה
        /// המפורשת בתיעוד נדרים פלוס: אין לסמוך על תגובת האייפרם/הדפדפן בלבד.
        /// </summary>
        public async Task<TopupStatusResult> GetTopupStatusAsync(string token, CancellationToken ct = default)
        {
            var response = await GetAsync<TopupStatusResponse>(
                $"/.netlify/functions/library-topup-status?token={Uri.EscapeDataString(token)}", ct);

            if (!response.Success)
            {
                throw new KioskApiException(response.Error ?? "כשל בבדיקת סטטוס ההטענה", 404);
            }

            return new TopupStatusResult { Status = response.Status ?? "", Amount = response.Amount, ConfirmedAt = response.ConfirmedAt };
        }

        private async Task<T> GetAsync<T>(string path, CancellationToken ct) where T : ApiEnvelope, new()
        {
            using var httpResponse = await _http.GetAsync(path, ct);
            var (_, body) = await ReadResponseAsync<T>(httpResponse, ct);
            return body;
        }

        private async Task<T> PostAsync<T>(string path, object payload, CancellationToken ct) where T : ApiEnvelope, new()
        {
            var (_, body) = await PostRawAsync<T>(path, payload, ct);
            return body;
        }

        private async Task<(int StatusCode, T Body)> PostRawAsync<T>(string path, object payload, CancellationToken ct) where T : ApiEnvelope, new()
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var httpResponse = await _http.PostAsync(path, content, ct);
            return await ReadResponseAsync<T>(httpResponse, ct);
        }

        private static async Task<(int StatusCode, T Body)> ReadResponseAsync<T>(HttpResponseMessage httpResponse, CancellationToken ct) where T : ApiEnvelope, new()
        {
            var text = await httpResponse.Content.ReadAsStringAsync(ct);
            T body;
            try
            {
                body = JsonSerializer.Deserialize<T>(text, JsonOptions) ?? new T();
            }
            catch (JsonException)
            {
                body = new T { Success = false, Error = $"תשובה לא תקינה מהשרת (קוד {(int)httpResponse.StatusCode})" };
            }
            return ((int)httpResponse.StatusCode, body);
        }

        public void Dispose()
        {
            if (_ownsHttpClient) _http.Dispose();
        }

        // --- מבני JSON פנימיים, תואמים בדיוק לתשובות ה-Functions (ראו ההערה בראש הקובץ) ---

        private class ApiEnvelope
        {
            public bool Success { get; set; }
            public string? Error { get; set; }
        }

        private class LoginResponse : ApiEnvelope
        {
            public KioskUser? User { get; set; }
            public bool MustChangePassword { get; set; }
        }

        private class AccountInfoResponse : ApiEnvelope
        {
            public bool IsCommunityMember { get; set; }
            public decimal Balance { get; set; }
            public System.Collections.Generic.List<AccountTransaction>? Transactions { get; set; }
        }

        private class AccountCheckResponse : ApiEnvelope
        {
            public bool Exists { get; set; }
        }

        private class ResetInfoResponse : ApiEnvelope
        {
            public string? ResetPhone { get; set; }
            public bool Found { get; set; }
            public string? MaskedPhone { get; set; }
        }

        private class LegacyResponse : ApiEnvelope
        {
            public bool Found { get; set; }
            public string? LegacyId { get; set; }
            public decimal Amount { get; set; }
            public decimal Balance { get; set; }
        }

        private class GabaiThreadResponse : ApiEnvelope
        {
            public System.Collections.Generic.List<GabaiMessage>? Messages { get; set; }
            public bool HasUnread { get; set; }
        }

        private class ConfigResponse : ApiEnvelope
        {
            public KioskConfig? Config { get; set; }
        }

        private class ChargeResponse : ApiEnvelope
        {
            public decimal Balance { get; set; }
            public decimal Cost { get; set; }
            public TransactionRecord? Transaction { get; set; }
            public bool InsufficientFunds { get; set; }
        }

        private class RefundResponse : ApiEnvelope
        {
            public decimal Balance { get; set; }
            public decimal Amount { get; set; }
        }

        private class TopupInitResponse : ApiEnvelope
        {
            public string? Token { get; set; }
            public string? TransactionId { get; set; }
        }

        private class SignupCheckResponse : ApiEnvelope
        {
            public bool IsCommunityMember { get; set; }
            public decimal SignupAmount { get; set; }
            public string? CommunityJoinUrl { get; set; }
            public decimal PrepaidAmount { get; set; }
            public bool Prepaid { get; set; }
        }

        private class PrepaidSignupResponse : ApiEnvelope
        {
            public decimal Loaded { get; set; }
        }

        private class OnlinePaymentResponse : ApiEnvelope
        {
            public string? Token { get; set; }
            public string? Url { get; set; }
            public decimal Amount { get; set; }
        }

        private class KevaChargeCheckResponse : ApiEnvelope
        {
            public bool Eligible { get; set; }
            public string? Last4 { get; set; }
            public bool Consented { get; set; }
            public decimal MaxPerCharge { get; set; }
            public decimal MaxPerDay { get; set; }
            public decimal ChargedToday { get; set; }
            public decimal Amount { get; set; }
            public string? Reason { get; set; }
        }

        private class KevaChargeResponse : ApiEnvelope
        {
            public bool Uncertain { get; set; }
            public decimal Amount { get; set; }
            public decimal? Balance { get; set; }
        }

        private class KevaSnapshotResponse : ApiEnvelope
        {
            public List<string>? KevaIds { get; set; }
        }

        private class MemberCheckResponse : ApiEnvelope
        {
            public bool IsCommunityMember { get; set; }
        }

        private class SignupInitResponse : ApiEnvelope
        {
            public string? Token { get; set; }
            public string? TransactionId { get; set; }
            public decimal Amount { get; set; }
        }

        private class TopupStatusResponse : ApiEnvelope
        {
            public string? Status { get; set; }
            public decimal Amount { get; set; }
            public DateTimeOffset? ConfirmedAt { get; set; }
        }
    }
}
