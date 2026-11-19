using System.Net;
using System.Text.Json;
using KioskApp.Core;
using Xunit;

namespace KioskApp.Core.Tests
{
    public class KioskApiClientTests
    {
        private const string BaseUrl = "https://example-site.netlify.app";
        private const string StationKey = "test-station-key";

        [Fact]
        public async Task LoginAsync_Success_ReturnsUser()
        {
            var json = """
            { "success": true, "user": { "id": "u1", "username": "dani", "balance": 12.5, "isActive": true } }
            """;
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.OK, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            var user = await client.LoginAsync("dani", "1234");

            Assert.Equal("u1", user.Id);
            Assert.Equal("dani", user.Username);
            Assert.Equal(12.5m, user.Balance);
            Assert.True(user.IsActive);
        }

        [Fact]
        public async Task LoginAsync_SendsStationKeyHeaderAndCredentialsBody()
        {
            var json = """{ "success": true, "user": { "id": "u1", "username": "dani", "balance": 0, "isActive": true } }""";
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.OK, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            await client.LoginAsync("dani", "secret123");

            Assert.NotNull(handler.LastRequest);
            Assert.Equal(StationKey, handler.LastRequest!.Headers.GetValues("x-station-key").First());
            Assert.Contains("/.netlify/functions/library-station-login", handler.LastRequest.RequestUri!.ToString());

            using var body = JsonDocument.Parse(handler.LastRequestBody!);
            Assert.Equal("dani", body.RootElement.GetProperty("username").GetString());
            Assert.Equal("secret123", body.RootElement.GetProperty("password").GetString());
        }

        [Fact]
        public async Task LoginAsync_WrongCredentials_ThrowsKioskAuthException()
        {
            var json = """{ "success": false, "error": "שם משתמש או סיסמה שגויים" }""";
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.Unauthorized, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            var ex = await Assert.ThrowsAsync<KioskAuthException>(() => client.LoginAsync("dani", "wrong"));
            Assert.Equal("שם משתמש או סיסמה שגויים", ex.Message);
        }

        [Fact]
        public async Task LoginAsync_TemporaryPassword_ThrowsPasswordChangeRequired()
        {
            var json = """{ "success": true, "mustChangePassword": true }""";
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.OK, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            await Assert.ThrowsAsync<PasswordChangeRequiredException>(() => client.LoginAsync("dani", "123456"));
        }

        [Fact]
        public async Task ChangePasswordAsync_Success_SendsBodyAndReturnsUser()
        {
            var json = """{ "success": true, "user": { "id": "u1", "username": "dani", "balance": 3, "isActive": true } }""";
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.OK, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            var user = await client.ChangePasswordAsync("dani", "123456", "newpass");

            Assert.Equal("u1", user.Id);
            Assert.Contains("/.netlify/functions/library-station-change-password", handler.LastRequest!.RequestUri!.ToString());
            using var body = JsonDocument.Parse(handler.LastRequestBody!);
            Assert.Equal("dani", body.RootElement.GetProperty("username").GetString());
            Assert.Equal("123456", body.RootElement.GetProperty("currentPassword").GetString());
            Assert.Equal("newpass", body.RootElement.GetProperty("newPassword").GetString());
        }

        [Fact]
        public async Task ChangePasswordAsync_ExpiredTemp_ThrowsKioskAuthException()
        {
            var json = """{ "success": false, "error": "פג תוקף" }""";
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.Unauthorized, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            var ex = await Assert.ThrowsAsync<KioskAuthException>(() => client.ChangePasswordAsync("dani", "123456", "newpass"));
            Assert.Equal("פג תוקף", ex.Message);
        }

        [Fact]
        public async Task ChangePasswordAsync_TooShort_ThrowsApiExceptionNotAuth()
        {
            var json = """{ "success": false, "error": "קצרה מדי" }""";
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.BadRequest, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            var ex = await Assert.ThrowsAsync<KioskApiException>(() => client.ChangePasswordAsync("dani", "123456", "ab"));
            Assert.IsNotType<KioskAuthException>(ex);
        }

        [Fact]
        public async Task GetPasswordResetInfoAsync_ParsesMaskedPhone()
        {
            var json = """{ "success": true, "resetPhone": "02-1234567", "found": true, "maskedPhone": "050-•••••67" }""";
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.OK, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            var info = await client.GetPasswordResetInfoAsync("123456789");

            Assert.True(info.Found);
            Assert.Equal("02-1234567", info.ResetPhone);
            Assert.Equal("050-•••••67", info.MaskedPhone);
            Assert.Contains("/.netlify/functions/library-station-reset-info", handler.LastRequest!.RequestUri!.ToString());
            using var body = JsonDocument.Parse(handler.LastRequestBody!);
            Assert.Equal("123456789", body.RootElement.GetProperty("username").GetString());
        }

        [Fact]
        public async Task GetPasswordResetInfoAsync_NotFound_ReturnsFoundFalse()
        {
            var json = """{ "success": true, "resetPhone": "02-1234567", "found": false }""";
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.OK, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            var info = await client.GetPasswordResetInfoAsync("999");

            Assert.False(info.Found);
            Assert.Equal("", info.MaskedPhone);
        }

        [Fact]
        public async Task GetConfigAsync_ParsesPriceAndAllowedApps()
        {
            var json = """
            {
              "success": true,
              "config": {
                "pricePerPage": 0.5,
                "allowedApps": [
                  { "id": "a1", "displayName": "אוצר החכמה", "exePath": "C:\\otzar.exe", "iconPath": "", "sortOrder": 1 }
                ]
              }
            }
            """;
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.OK, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            var config = await client.GetConfigAsync();

            Assert.Equal(0.5m, config.PricePerPage);
            Assert.Single(config.AllowedApps);
            Assert.Equal("אוצר החכמה", config.AllowedApps[0].DisplayName);
        }

        [Fact]
        public async Task ChargePrintAsync_SufficientBalance_ReturnsUpdatedBalance()
        {
            var json = """
            { "success": true, "balance": 8, "cost": 2, "transaction": { "id": "t1", "userId": "u1", "type": "charge", "source": "print", "amount": 2, "timestamp": "2026-01-01T00:00:00.000Z" } }
            """;
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.OK, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            var result = await client.ChargePrintAsync("u1", 4);

            Assert.Equal(8m, result.Balance);
            Assert.Equal(2m, result.Cost);
            Assert.NotNull(result.Transaction);
            Assert.Equal("charge", result.Transaction!.Type);
        }

        [Fact]
        public async Task ChargePrintAsync_InsufficientBalance_ThrowsWithBalanceAndCost()
        {
            var json = """
            { "success": false, "error": "היתרה אינה מספיקה לביצוע ההדפסה", "insufficientFunds": true, "balance": 8, "cost": 50 }
            """;
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.PaymentRequired, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            var ex = await Assert.ThrowsAsync<InsufficientFundsException>(() => client.ChargePrintAsync("u1", 100));

            Assert.Equal(8m, ex.Balance);
            Assert.Equal(50m, ex.Cost);
        }

        [Fact]
        public async Task InitTopupAsync_ReturnsTokenAndTransactionId()
        {
            var json = """{ "success": true, "token": "tok-123", "transactionId": "tx-456" }""";
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.OK, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            var result = await client.InitTopupAsync("u1", 50m);

            Assert.Equal("tok-123", result.Token);
            Assert.Equal("tx-456", result.TransactionId);
        }

        [Fact]
        public async Task GetTopupStatusAsync_ReturnsConfirmedStatus()
        {
            var json = """{ "success": true, "status": "confirmed", "amount": 50, "confirmedAt": "2026-01-01T00:00:00.000Z" }""";
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.OK, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            var result = await client.GetTopupStatusAsync("tok-123");

            Assert.Equal("confirmed", result.Status);
            Assert.Equal(50m, result.Amount);
            Assert.NotNull(result.ConfirmedAt);
        }

        [Fact]
        public async Task GetTopupStatusAsync_UnknownToken_ThrowsKioskApiException()
        {
            var json = """{ "success": false, "error": "אסימון תשלום לא מוכר" }""";
            using var handler = FakeHttpMessageHandler.ReturningJson(HttpStatusCode.NotFound, json);
            using var client = new KioskApiClient(BaseUrl, StationKey, handler);

            var ex = await Assert.ThrowsAsync<KioskApiException>(() => client.GetTopupStatusAsync("no-such-token"));
            Assert.Equal(404, ex.StatusCode);
        }
    }
}
