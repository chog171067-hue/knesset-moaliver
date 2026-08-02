using System;
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

        public async Task<KioskUser> LoginAsync(string username, string password, CancellationToken ct = default)
        {
            var response = await PostAsync<LoginResponse>("/.netlify/functions/library-station-login", new { username, password }, ct);
            if (!response.Success || response.User == null)
            {
                throw new KioskAuthException(response.Error ?? "שם משתמש או סיסמה שגויים", 401);
            }
            return response.User;
        }

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
        public async Task<ChargeResult> ChargePrintAsync(string userId, int pages, CancellationToken ct = default)
        {
            var (statusCode, response) = await PostRawAsync<ChargeResponse>(
                "/.netlify/functions/library-print-charge", new { userId, pages }, ct);

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

        private class TopupInitResponse : ApiEnvelope
        {
            public string? Token { get; set; }
            public string? TransactionId { get; set; }
        }

        private class TopupStatusResponse : ApiEnvelope
        {
            public string? Status { get; set; }
            public decimal Amount { get; set; }
            public DateTimeOffset? ConfirmedAt { get; set; }
        }
    }
}
