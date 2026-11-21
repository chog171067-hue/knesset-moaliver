using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using KioskApp.Core.Models;
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
        private string? _transactionId;
        private bool _completed;
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        // מצב "פתיחת חשבון": אין עדיין משתמש מחובר. התשלום יוצר את החשבון בשרת,
        // והחלון רק מדווח שהצליח (SignupCompleted) - מסך הכניסה מתחבר אחר כך.
        private readonly SignupDetails? _signup;

        /// <summary>true אחרי שהשרת אישר את התשלום ופתח את החשבון (מצב פתיחת חשבון)</summary>
        public bool SignupCompleted { get; private set; }

        public TopupWindow()
        {
            InitializeComponent();
            Closed += (_, _) => CloseProgress();
        }

        /// <summary>תשלום פתיחת חשבון - סכום קבוע, נפתח ישר לטופס האשראי</summary>
        public TopupWindow(SignupDetails signup, decimal amount) : this()
        {
            _signup = signup;
            Title = "תשלום לפתיחת חשבון";
            TitleText.Text = "תשלום לפתיחת החשבון";
            AmountLabel.Visibility = Visibility.Collapsed;
            AmountBox.Visibility = Visibility.Collapsed;
            FixedAmountText.Text = $"סכום: {amount:0.##} ₪ - נכנס כולו ליתרה בחשבון שלך";
            FixedAmountBox.Visibility = Visibility.Visible;
        }

        private bool _paymentSubmitted;
        private KioskMessageDialog? _progress;

        private async void OnContinueClick(object sender, RoutedEventArgs e)
        {
            HideError();

            if (!TryGetAmount(out var amount)) return;

            ContinueButton.IsEnabled = false;
            ContinueButton.Content = "פותח טופס תשלום...";

            try
            {
                decimal shownAmount;
                if (_signup != null)
                {
                    var signupInit = await App.ApiClient.InitSignupAsync(_signup);
                    _pendingToken = signupInit.Token;
                    _transactionId = signupInit.TransactionId;
                    shownAmount = signupInit.Amount;
                }
                else
                {
                    var initResult = await App.ApiClient.InitTopupAsync(App.Session.CurrentUser!.Id, amount);
                    _pendingToken = initResult.Token;
                    _transactionId = initResult.TransactionId;
                    shownAmount = amount;
                }
                _completed = false;
                _paymentSubmitted = false;

                // נתוני הדפדפן הפנימי - בתיקייה האישית של המשתמש, ולא ליד קובץ ה-exe (ברירת המחדל):
                // תיקיית הקיוסק היא "קריאה בלבד" למשתמש הקיוסק (ראו Setup-KioskUser.ps1)
                // (רק בפעם הראשונה - אחרי תשלום שנדחה ו"המשך לתשלום" מחדש, הדפדפן כבר מוכן)
                if (WebView.CoreWebView2 == null)
                {
                    var webViewData = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KioskApp", "WebView2");
                    var webViewEnv = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, webViewData);
                    await WebView.EnsureCoreWebView2Async(webViewEnv);
                    WebView.CoreWebView2!.WebMessageReceived += OnWebMessageReceived;
                    WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                }
                // לא שולחים את פקודת התשלום אוטומטית: היא הייתה נשלחת לפני שהמשתמש הספיק
                // להקליד את פרטי הכרטיס, ונדרים פלוס מתעלם ממנה. המשתמש לוחץ "בצע תשלום"
                // בעצמו אחרי שמילא את הפרטים (כמו ב-personal.html, submitLibraryPayment).
                // הדף המתווך מאוחסן באתר (kiosk-topup-wrapper.html) ולא נוצר כאן בקוד: לדף בלי
                // כתובת אמיתית נדרים פלוס לא שולחים את התשובה (אושר/נדחה), והחלון היה ממתין
                // עד תום הזמן גם כשהעסקה נדחתה.
                WebView.CoreWebView2!.Navigate($"{App.Config.ApiBaseUrl.TrimEnd('/')}/kiosk-topup-wrapper.html");

                PaymentSummaryText.Text = _signup != null
                    ? $"פתיחת חשבון - {shownAmount:0.##} ₪ (נכנס כולו ליתרה)"
                    : $"הטענה לחשבון - {shownAmount:0.##} ₪";
                AmountPanel.Visibility = Visibility.Collapsed;
                PaymentPanel.Visibility = Visibility.Visible;
                PayButton.IsEnabled = true;
                PayButton.Content = "בצע תשלום";
            }
            catch (Exception ex)
            {
                App.LogError("כשל בפתיחת עסקת הטענה", ex);
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "לא ניתן לפתוח את טופס התשלום", ex.Message);
            }
            finally
            {
                ContinueButton.IsEnabled = true;
                ContinueButton.Content = "תשלום בכרטיס אשראי";
            }
        }

        private void OnPayClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_transactionId) || WebView.CoreWebView2 == null) return;

            PayButton.IsEnabled = false;
            PayButton.Content = "מבצע את התשלום...";
            _paymentSubmitted = true;

            var command = JsonSerializer.Serialize(new { Name = "StartTransaction", TransactionId = _transactionId });
            WebView.CoreWebView2.PostWebMessageAsJson(command);

            // לא מסתמכים רק על הודעת "הצלחה" מתוך הטופס של נדרים פלוס (היא לא תמיד מגיעה):
            // שואלים את השרת שלנו - שמקבל אישור ישיר מנדרים פלוס - עד שהתשלום מאושר.
            _ = PollTopupStatusAsync(maxAttempts: 90);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            if (_paymentSubmitted && !_completed)
            {
                // התשלום נשלח וממתין לאישור - אפשר לסגור, אבל מסבירים מה יקרה
                _completed = true;
                CloseProgress();
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Info, "החלון נסגר",
                    _signup != null
                        ? "אם התשלום אושר, החשבון ייפתח תוך מספר דקות, ואז אפשר להיכנס דרך \"כניסה לחשבון\" עם תעודת הזהות והסיסמה שבחרתם."
                        : "אם התשלום אושר, היתרה תתעדכן תוך מספר דקות.");
            }
            Close();
        }

        private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                var name = root.GetProperty("Name").GetString();
                if (name != "Height") App.LogInfo($"הטענה: הודעה מטופס נדרים פלוס: {e.WebMessageAsJson}");

                if (name == "Height" && root.TryGetProperty("Value", out var h))
                {
                    // גובה הטופס של נדרים פלוס - כדי שהמסגרת תתאים לו בלי גלילה ובלי רווח ריק
                    var height = h.ValueKind == JsonValueKind.Number ? h.GetDouble()
                        : double.TryParse(h.GetString(), out var parsed) ? parsed : 0;
                    if (height > 0) Dispatcher.Invoke(() => WebView.Height = Math.Clamp(height + 24, 260, 560));
                }

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
            if (_completed) return;
            _completed = true; // עוצר את בדיקת הסטטוס מול השרת
            _paymentSubmitted = false;
            CloseProgress();
            KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "התשלום לא בוצע", message);
            // כמו ב-personal.html: עסקה שנכשלה לא מנסים שוב עם אותו מזהה - חוזרים לשלב
            // הסכום, ו"המשך לתשלום" יקים עסקה חדשה
            PaymentPanel.Visibility = Visibility.Collapsed;
            AmountPanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// זו רק תגובת הדפדפן (ניתנת לזיוף) - לא מעדכנים יתרה על סמך זה, רק מציגים
        /// "מאמתים מול השרת". הבדיקה מול השרת כבר רצה מאז הלחיצה על "בצע תשלום".
        /// </summary>
        private void OnPaymentAcceptedByIframe()
        {
            if (_completed || _progress != null) return;
            _progress = KioskMessageDialog.ShowProgress(this, "התשלום התקבל",
                _signup != null ? "פותחים את החשבון, נא להמתין מספר שניות..." : "מעדכנים את היתרה, נא להמתין מספר שניות...");
        }

        private void CloseProgress()
        {
            _progress?.CloseProgress();
            _progress = null;
        }

        private async Task PollTopupStatusAsync(int maxAttempts)
        {
            var token = _pendingToken;
            if (token == null) return;

            // 2 שניות בין ניסיונות. מתחיל מיד עם הלחיצה על "בצע תשלום", ולכן מספר
            // הניסיונות גבוה (90 = 3 דקות) - כולל הזמן שנדרים פלוס מעבד את הכרטיס.
            // אחרי תשלום שנדחה ו"המשך לתשלום" מחדש יש אסימון חדש - הבדיקה הישנה נעצרת
            for (var attempt = 0; attempt < maxAttempts && !_completed && token == _pendingToken; attempt++)
            {
                try
                {
                    var status = await App.ApiClient.GetTopupStatusAsync(token);
                    if (status.Status == "failed" && !_completed)
                    {
                        _completed = true;
                        CloseProgress();
                        ShowServerFailed(token);
                        return;
                    }
                    if (status.Status == "confirmed" && !_completed)
                    {
                        _completed = true;
                        CloseProgress();
                        await ShowConfirmedAndCloseAsync(status.Amount);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    App.LogError("שגיאה בבדיקת סטטוס הטענה", ex);
                }

                await Task.Delay(2000);
            }

            if (_completed || token != _pendingToken) return;
            _completed = true;
            CloseProgress();
            KioskMessageDialog.ShowMessage(this, KioskMessageKind.Info, "לא התקבל אישור תשלום",
                _signup != null
                    ? "אם חויבתם - החשבון ייפתח אוטומטית תוך מספר דקות, ואז אפשר להיכנס דרך \"כניסה לחשבון\" עם תעודת הזהות והסיסמה שבחרתם. אחרת - אפשר לנסות שוב."
                    : "אם חויבתם - היתרה תתעדכן אוטומטית תוך מספר דקות (אפשר להתנתק ולהתחבר מחדש כדי לראות). אחרת - אפשר לנסות שוב.");
            Close();
        }

        /// <summary>סכום מהשדה (בהטענה רגילה) - בפתיחת חשבון הסכום נקבע בשרת</summary>
        private bool TryGetAmount(out decimal amount)
        {
            amount = 0;
            if (_signup != null) return true;
            if (!decimal.TryParse(AmountBox.Text, out amount) || amount <= 0)
            {
                ShowError("יש להזין סכום תקין");
                return false;
            }
            if (string.IsNullOrEmpty(App.Session.CurrentUser?.Id))
            {
                ShowError("אין משתמש מחובר");
                return false;
            }
            return true;
        }

        /// <summary>התשלום אושר בשרת: הודעת הצלחה, עדכון יתרה (בהטענה רגילה), וסגירה</summary>
        private async Task ShowConfirmedAndCloseAsync(decimal creditedAmount)
        {
            if (_signup != null)
            {
                SignupCompleted = true;
                await KioskMessageDialog.ShowTimedAsync(this, KioskMessageKind.Success,
                    "החשבון נפתח בהצלחה!", $"{creditedAmount:0.##} ₪ נטענו ליתרה שלך. נכנסים לחשבון...", 2.5);
                Close();
                return;
            }
            // creditedAmount הוא הסכום שבאמת זוכה בשרת (ראו confirmPendingTopup) -
            // ולכן זה חישוב מדויק, לא ניחוש, גם אם הוא שונה מהסכום שהוזן במסך
            var newBalance = App.Session.CurrentUser!.Balance + creditedAmount;
            App.Session.UpdateBalance(newBalance);
            await KioskMessageDialog.ShowTimedAsync(this, KioskMessageKind.Success,
                "הטענת הכסף אושרה!", $"{creditedAmount:0.##} ₪ נוספו ליתרה. היתרה עכשיו: {newBalance:0.##} ₪", 2.5);
            Close();
        }

        /// <summary>נדרים פלוס דיווח על התשלום, אבל הטעינה בשרת נכשלה - צריך טיפול ידני</summary>
        private void ShowServerFailed(string token)
        {
            App.LogError("הטענה: התשלום התקבל אך זיכוי היתרה נכשל בשרת", new InvalidOperationException(token));
            KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "נדרש טיפול של הצוות",
                _signup != null
                    ? "התשלום התקבל, אך פתיחת החשבון נכשלה (ייתכן שכבר קיים חשבון עם תעודת הזהות הזאת). נא לפנות לצוות הספרייה - הפרטים נשמרו במערכת."
                    : "התשלום התקבל, אך עדכון היתרה נכשל. נא לפנות לצוות הספרייה - הפרטים נשמרו במערכת.");
            Close();
        }

        /// <summary>
        /// "תשלום בכרטיס שמור": הדף הרגיל של נדרים פלוס, שבו המשתמש נכנס לאזור האישי שלו
        /// ומשלם בכרטיס שנשמר שם. הסכום נטען לפי הודעת ה-Webhook מנדרים פלוס (לא לפי מה
        /// שהדף מציג), והחלון בודק את המצב מול השרת שלנו עד שהתשלום מאושר.
        /// </summary>
        private async void OnOnlinePayClick(object sender, RoutedEventArgs e)
        {
            HideError();
            if (!TryGetAmount(out var amount)) return;

            OnlinePayButton.IsEnabled = false;
            ContinueButton.IsEnabled = false;
            OnlinePaymentResult payment;
            try
            {
                payment = await App.ApiClient.InitOnlinePaymentAsync(
                    _signup == null ? App.Session.CurrentUser!.Id : null, amount, _signup);
            }
            catch (Exception ex)
            {
                App.LogError("הטענה בכרטיס שמור: כשל בהכנת התשלום", ex);
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "לא ניתן לפתוח את נדרים פלוס", ex.Message);
                return;
            }
            finally
            {
                OnlinePayButton.IsEnabled = true;
                ContinueButton.IsEnabled = true;
            }

            string? finalStatus = null;
            decimal finalAmount = 0;
            async Task<bool> CheckAsync()
            {
                var st = await App.ApiClient.GetTopupStatusAsync(payment.Token);
                finalStatus = st.Status;
                finalAmount = st.Amount;
                return st.Status is "confirmed" or "failed";
            }

            var page = new NedarimPageWindow(
                "תשלום בכרטיס שמור",
                "מקלידים את הסיסמה של האזור האישי בנדרים פלוס, בוחרים כרטיס שמור ומשלמים. בסיום החלון ייסגר לבד.",
                payment.Url, CheckAsync,
                pollSeconds: 4, firstCheckAfterSeconds: 10,
                returnUrlPrefix: $"{App.Config.ApiBaseUrl.TrimEnd('/')}/kiosk-payment-done.html",
                autoOpenDonorLogin: true) { Owner = this };
            page.ShowDialog();

            if (!page.Completed)
            {
                // נסגר ב-X: בדיקה אחת (אולי התשלום אושר רגע לפני הסגירה)
                try { await CheckAsync(); } catch (Exception ex) { App.LogError("הטענה בכרטיס שמור: בדיקה אחרי סגירה", ex); }
            }

            if (finalStatus == "confirmed") await ShowConfirmedAndCloseAsync(finalAmount);
            else if (finalStatus == "failed") ShowServerFailed(payment.Token);
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    }
}
