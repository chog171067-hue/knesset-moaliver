using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using KioskApp.Services;

namespace KioskApp.Views
{
    /// <summary>
    /// חלון הטענת כסף - מקים עסקה בצד השרת (כדי שהסכום ייקבע שם ולא ישתנה בצד
    /// הלקוח, ראו KioskApiClient.InitTopupAsync), מציג את אייפרם נדרים פלוס להזנת
    /// פרטי הכרטיס, ולבסוף מאמת מול השרת שלנו (לא מסתמך על תגובת האייפרם בלבד -
    /// ראו lib/kiosk-store.js ותיעוד נדרים פלוס, "אימות תשלום ואבטחה") לפני עדכון היתרה.
    /// </summary>
    public partial class TopupWindow : Window
    {
        private string? _pendingToken;
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public TopupWindow()
        {
            InitializeComponent();
        }

        private async void OnContinueClick(object sender, RoutedEventArgs e)
        {
            HideError();

            if (!decimal.TryParse(AmountBox.Text, out var amount) || amount <= 0)
            {
                ShowError("יש להזין סכום תקין");
                return;
            }

            var userId = App.Session.CurrentUser?.Id;
            if (string.IsNullOrEmpty(userId))
            {
                ShowError("אין משתמש מחובר");
                return;
            }

            ContinueButton.IsEnabled = false;
            ContinueButton.Content = "פותח טופס תשלום...";

            try
            {
                var initResult = await App.ApiClient.InitTopupAsync(userId, amount);
                _pendingToken = initResult.Token;

                await WebView.EnsureCoreWebView2Async();
                WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                WebView.CoreWebView2.NavigationCompleted += async (_, _) =>
                {
                    // ראו הערה מקבילה ב-personal.html (startNewKeva): פרק זמן קצר
                    // שנועד לתת לאייפרם המקונן בתוך הדף המקומי סיכוי סביר להיטען
                    await Task.Delay(600);
                    var command = JsonSerializer.Serialize(new { Name = "StartTransaction", TransactionId = initResult.TransactionId });
                    WebView.CoreWebView2.PostWebMessageAsJson(command);
                };
                WebView.CoreWebView2.NavigateToString(NedarimWrapperHtml.Content);

                AmountPanel.Visibility = Visibility.Collapsed;
                WebView.Visibility = Visibility.Visible;
                ShowStatus("ממתין להשלמת התשלום בטופס למעלה...");
            }
            catch (Exception ex)
            {
                App.LogError("כשל בפתיחת עסקת הטענה", ex);
                ShowError(ex.Message);
                ContinueButton.IsEnabled = true;
                ContinueButton.Content = "המשך לתשלום";
            }
        }

        private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                var name = root.GetProperty("Name").GetString();

                if (name == "TransactionResponse")
                {
                    var value = root.GetProperty("Value");
                    var status = value.TryGetProperty("Status", out var s) ? s.GetString() : null;

                    if (status == "Error")
                    {
                        var message = value.TryGetProperty("Message", out var m) ? m.GetString() : "התשלום נכשל";
                        Dispatcher.Invoke(() => OnPaymentFailed(message ?? "התשלום נכשל"));
                    }
                    else
                    {
                        Dispatcher.Invoke(OnPaymentAcceptedByIframe);
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogError("שגיאה בפענוח הודעה מ-WebView2", ex);
            }
        }

        private void OnPaymentFailed(string message)
        {
            WebView.Visibility = Visibility.Collapsed;
            AmountPanel.Visibility = Visibility.Visible;
            HideStatus();
            ShowError(message);
            ContinueButton.IsEnabled = true;
            ContinueButton.Content = "המשך לתשלום";
        }

        /// <summary>
        /// זו רק תגובת הדפדפן (ניתנת לזיוף) - לא מעדכנים יתרה על סמך זה, רק עוברים
        /// למצב "מאמתים מול השרת" ומתחילים polling אמיתי.
        /// </summary>
        private async void OnPaymentAcceptedByIframe()
        {
            WebView.Visibility = Visibility.Collapsed;
            ShowStatus("התשלום התקבל, מאמתים מול השרת...");
            await PollTopupStatusAsync();
        }

        private async Task PollTopupStatusAsync()
        {
            if (_pendingToken == null) return;

            // עד כ-30 שניות (15 ניסיונות, 2 שניות בין כל אחד) - תואם את הלוגיקה
            // המקבילה ב-personal.html (pollLibraryTopupStatus)
            for (var attempt = 0; attempt < 15; attempt++)
            {
                try
                {
                    var status = await App.ApiClient.GetTopupStatusAsync(_pendingToken);
                    if (status.Status == "confirmed")
                    {
                        // status.Amount הוא הסכום שבאמת זוכה בשרת (ראו confirmPendingTopup) -
                        // ולכן זה חישוב מדויק, לא ניחוש, גם אם הוא שונה מהסכום שהוזן במסך
                        var newBalance = App.Session.CurrentUser!.Balance + status.Amount;
                        App.Session.UpdateBalance(newBalance);
                        ShowStatus("✅ הטענת הכסף אושרה! היתרה עודכנה.");
                        await Task.Delay(1500);
                        Close();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    App.LogError("שגיאה בבדיקת סטטוס הטענה", ex);
                }

                await Task.Delay(2000);
            }

            ShowStatus("התשלום התקבל, אך האישור מהשרת מתעכב. היתרה תתעדכן אוטומטית תוך מספר דקות.");
        }

        private void ShowStatus(string text)
        {
            StatusText.Text = text;
            StatusText.Visibility = Visibility.Visible;
        }

        private void HideStatus() => StatusText.Visibility = Visibility.Collapsed;

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    }
}
