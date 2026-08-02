using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace KioskApp.Core.Tests
{
    /// <summary>
    /// מדמה שרת HTTP בלי לפתוח שקע רשת אמיתי - מחזיר תשובה קבועה מראש (או שמריץ
    /// callback שבוחר תשובה לפי הבקשה, למקרים שבהם הבדיקה צריכה לוודא מה בדיוק נשלח).
    /// </summary>
    public class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public static FakeHttpMessageHandler ReturningJson(HttpStatusCode statusCode, string json)
        {
            return new FakeHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json)
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
            return _responder(request);
        }
    }
}
