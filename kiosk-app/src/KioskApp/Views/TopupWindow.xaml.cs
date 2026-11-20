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
        }

        /// <summary>תשלום פתיחת חשבון - סכום קבוע, נפתח ישר לטופס האשראי</summary>
        public TopupWindow(SignupDetails signup, decimal amount) : this()
        {
            _signup = signup;
            Title = "תשלום לפתיחת חשבון";
            TitleText.Text = "תשלום לפתיחת החשבון";
            AmountLabel.Visibility = Visibility.Collapsed;
            AmountBox.Visibility = Visibility.Collapsed;
            FixedAmountText.Text = $"סכום: {amount:0.##} ₪ (נכנס כולו ליתרה בחשבון שלך)";
            FixedAmountText.Visibility = Visibility.Visible;
            Loaded += (_, _) => OnContinueClick(this, new RoutedEventArgs());
        }

        private async void OnContinueClick(object sender, RoutedEventArgs e)
        {
            HideError();

            ContinueButton.IsEnabled = false;
            ContinueButton.Content = "פותח טופס תשלום...";

            try
            {
                if (_signup != null)
                {
                    var signupInit = await App.ApiClient.InitSignupAsync(_signup);
                    _pendingToken = signupInit.Token;
                    _transactionId = signupInit.TransactionId;
                }
                else
                {
                    if (!decimal.TryParse(AmountBox.Text, out var amount) || amount <= 0)
                    {
                        throw new InvalidOperationException("יש להזין סכום תקין");
                    }
                    var userId = App.Session.CurrentUser?.Id;
                    if (string.IsNullOrEmpty(userId))
                    {
                        throw new InvalidOperationException("אין משתמש מחובר");
                    }
                    var initResult = await App.ApiClient.InitTopupAsync(userId, amount);
                    _pendingToken = initResult.Token;
                    _transactionId = initResult.TransactionId;
                }
                _completed = false;

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
                }
                // לא שולחים את פקודת התשלום אוטומטית: היא הייתה נשלחת לפני שהמשתמש הספיק
                // להקליד את פרטי הכרטיס, ונדרים פלוס מתעלם ממנה. המשתמש לוחץ "בצע תשלום"
                // בעצמו אחרי שמילא את הפרטים (כמו ב-personal.html, submitLibraryPayment).
                // הדף המתווך מאוחסן באתר (kiosk-topup-wrapper.html) ולא נוצר כאן בקוד: לדף בלי
                // כתובת אמיתית נדרים פלוס לא שולחים את התשובה (אושר/נדחה), והחלון היה ממתין
                // עד תום הזמן גם כשהעסקה נדחתה.
                WebView.CoreWebView2!.Navigate($"{App.Config.ApiBaseUrl.TrimEnd('/')}/kiosk-topup-wrapper.html");

                AmountPanel.Visibility = Visibility.Collapsed;
                WebView.Visibility = Visibility.Visible;
                PayButton.IsEnabled = true;
                PayButton.Content = "בצע תשלום";
                PayButton.Visibility = Visibility.Visible;
                ShowStatus("הקלידו את פרטי הכרטיס ולחצו \"בצע תשלום\" למטה.");
            }
            catch (Exception ex)
            {
                App.LogError("כשל בפתיחת עסקת הטענה", ex);
                ShowError(ex.Message);
                ContinueButton.IsEnabled = true;
                ContinueButton.Content = "המשך לתשלום";
            }
        }

        private void OnPayClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_transactionId) || WebView.CoreWebView2 == null) return;

            PayButton.IsEnabled = false;
            PayButton.Content = "שולח...";
            ShowStatus("מבצע את התשלום, נא להמתין...");

            var command = JsonSerializer.Serialize(new { Name = "StartTransaction", TransactionId = _transactionId });
            WebView.CoreWebView2.PostWebMessageAsJson(command);

            // לא מסתמכים רק על הודעת "הצלחה" מתוך הטופס של נדרים פלוס (היא לא תמיד מגיעה):
            // שואלים את השרת שלנו - שמקבל אישור ישיר מנדרים פלוס - עד שהתשלום מאושר.
            _ = PollTopupStatusAsync(maxAttempts: 90);
        }

        private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                var name = root.GetProperty("Name").GetString();
                if (name != "Height") App.LogInfo($"הטענה: הודעה מטופס נדרים פלוס: {e.WebMessageAsJson}");

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
            // כמו ב-personal.html: עסקה שנכשלה לא מנסים שוב עם אותו מזהה - חוזרים למסך
            // הסכום, ו"המשך לתשלום" יקים עסקה חדשה
            PayButton.Visibility = Visibility.Collapsed;
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
            if (_completed) return;
            PayButton.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Collapsed;
            ShowStatus("התשלום התקבל, מאמתים מול השרת...");
            // הבדיקה מול השרת כבר רצה מאז הלחיצה על "בצע תשלום" - היא תסיים את התהליך
            await Task.CompletedTask;
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
                        // נדרים פלוס דיווח על התשלום, אבל זיכוי היתרה בשרת נכשל - צריך טיפול ידני
                        _completed = true;
                        PayButton.Visibility = Visibility.Collapsed;
                        WebView.Visibility = Visibility.Collapsed;
                        App.LogError("הטענה: התשלום התקבל אך זיכוי היתרה נכשל בשרת", new InvalidOperationException(_pendingToken));
                        ShowStatus(_signup != null
                            ? "התשלום התקבל, אך פתיחת החשבון נכשלה (ייתכן שכבר קיים חשבון עם תעודת הזהות הזאת). נא לפנות לצוות הספרייה - הפרטים נשמרו במערכת."
                            : "התשלום התקבל, אך עדכון היתרה נכשל. נא לפנות לצוות הספרייה - הפרטים נשמרו במערכת.");
                        return;
                    }
                    if (status.Status == "confirmed" && !_completed)
                    {
                        _completed = true;
                        PayButton.Visibility = Visibility.Collapsed;
                        WebView.Visibility = Visibility.Collapsed;
                        if (_signup != null)
                        {
                            SignupCompleted = true;
                            ShowStatus("✅ התשלום אושר והחשבון נפתח!");
                            await Task.Delay(1500);
                            Close();
                            return;
                        }
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

            if (_completed || token != _pendingToken) return;
            _completed = true;
            PayButton.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Collapsed;
            if (_signup != null)
            {
                ShowStatus("לא התקבל אישור תשלום. אם חויבתם - החשבון ייפתח אוטומטית תוך מספר דקות, ואז אפשר להיכנס דרך \"כניסה לחשבון\" עם תעודת הזהות והסיסמה שבחרתם. אחרת - סגרו חלון זה ונסו שוב.");
                return;
            }
            ShowStatus("לא התקבל אישור תשלום. אם חויבתם - היתרה תתעדכן אוטומטית תוך מספר דקות (אפשר להתנתק ולהתחבר מחדש כדי לראות). אחרת - סגרו חלון זה ונסו שוב.");
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
