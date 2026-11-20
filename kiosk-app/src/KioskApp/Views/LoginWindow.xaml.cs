using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using KioskApp.Core;
using KioskApp.Core.Models;

namespace KioskApp.Views
{
    public partial class LoginWindow : Window
    {
        // שם המשתמש והסיסמה הזמנית - נשמרים בזיכרון רק בזמן מסך "בחירת סיסמה חדשה"
        private string _pendingUsername = "";
        private string _pendingTempPassword = "";

        // פתיחת חשבון: הפרטים שאושרו ב"המשך" - עד התשלום (או חזרה למסך הפתיחה)
        private SignupDetails? _signupDetails;
        private decimal _signupAmount;
        private string _communityJoinUrl = "";
        // הוראות הקבע שהיו לת"ז לפני דף ההצטרפות - רק הוראה חדשה מזכה בחברות מיידית
        private System.Collections.Generic.List<string>? _kevaIdsBeforeJoin;
        private int _joinCheckGeneration;

        // אחרי כמה דקות בלי שימוש - חזרה למסך הפתיחה, כדי שפרטים שהוקלדו לא יישארו על המסך
        private static readonly TimeSpan IdleReset = TimeSpan.FromMinutes(3);
        private DateTime _lastActivity = DateTime.Now;
        private bool _paymentOpen;

        public LoginWindow()
        {
            InitializeComponent();
            App.KeepFullScreen(this);

            // שעה ותאריך עברי בפינת המסך
            UpdateClock();
            var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            clock.Tick += (_, _) => { UpdateClock(); ResetIfIdle(); };
            PreviewKeyDown += (_, _) => _lastActivity = DateTime.Now;
            PreviewMouseDown += (_, _) => _lastActivity = DateTime.Now;
            clock.Start();
            Closed += (_, _) => clock.Stop();
            Loaded += async (_, _) => await LoadForgotPasswordButtonAsync();
        }

        // הכפתור "שכחתי סיסמה" מוצג רק אם בדף הניהול הוגדר מספר לשחזור. תקלה כאן
        // (למשל אין אינטרנט) רק משאירה את הכפתור מוסתר - לא חוסמת את הכניסה.
        private async System.Threading.Tasks.Task LoadForgotPasswordButtonAsync()
        {
            try
            {
                var config = await App.ApiClient.GetConfigAsync();
                if (!string.IsNullOrWhiteSpace(config.PasswordResetPhone))
                {
                    ForgotPasswordButton.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                App.LogError("כשל בטעינת הגדרת שחזור הסיסמה", ex);
            }
        }

        private void OnFieldKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OnLoginClick(sender, e);
        }

        private void OnChangeFieldKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OnChangePasswordClick(sender, e);
        }

        private async void OnLoginClick(object sender, RoutedEventArgs e)
        {
            var username = UsernameBox.Text.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowError("יש להזין תעודת זהות וסיסמה");
                return;
            }

            LoginButton.IsEnabled = false;
            LoginButton.Content = "מתחבר...";
            HideError();

            try
            {
                var user = await App.ApiClient.LoginAsync(username, password);
                await StartSessionAsync(user);
            }
            catch (PasswordChangeRequiredException)
            {
                // נכנסו עם סיסמה זמנית מהטלפון: אסור להמשיך לפני בחירת סיסמה חדשה
                ShowChangePasswordPanel(username, password);
            }
            catch (Exception ex)
            {
                App.LogError("כשל בהתחברות", ex);
                ShowError(ex.Message);
                PasswordBox.Clear();
                PasswordBox.Focus();
            }
            finally
            {
                LoginButton.IsEnabled = true;
                LoginButton.Content = "התחברות";
            }
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            ClockText.Text = now.ToString("HH:mm");
            try
            {
                var hebrew = new System.Globalization.CultureInfo("he-IL");
                hebrew.DateTimeFormat.Calendar = new System.Globalization.HebrewCalendar();
                DateText.Text = now.ToString("dddd · d MMMM", hebrew);
            }
            catch
            {
                DateText.Text = now.ToString("dd/MM/yyyy");
            }
        }

        private async void OnChangePasswordClick(object sender, RoutedEventArgs e)
        {
            var newPassword = NewPasswordBox.Password;
            var confirm = ConfirmPasswordBox.Password;

            if (newPassword.Length < 4)
            {
                ShowError("הסיסמה החדשה חייבת להכיל לפחות 4 תווים");
                return;
            }
            if (newPassword != confirm)
            {
                ShowError("שתי הסיסמאות שהוקלדו אינן זהות");
                ConfirmPasswordBox.Clear();
                ConfirmPasswordBox.Focus();
                return;
            }

            ChangePasswordButton.IsEnabled = false;
            ChangePasswordButton.Content = "שומר...";
            HideError();

            KioskUser user;
            try
            {
                user = await App.ApiClient.ChangePasswordAsync(_pendingUsername, _pendingTempPassword, newPassword);
                App.LogInfo($"סיסמה הוחלפה אחרי כניסה עם סיסמה זמנית: {_pendingUsername}");
            }
            catch (KioskAuthException ex)
            {
                // הסיסמה הזמנית פגה בינתיים או בוטלה - חוזרים למסך הכניסה
                App.LogError("כשל בהחלפת סיסמה", ex);
                ShowLoginPanel();
                ShowError(ex.Message);
                return;
            }
            catch (Exception ex)
            {
                App.LogError("כשל בהחלפת סיסמה", ex);
                ShowError(ex.Message);
                return;
            }
            finally
            {
                ChangePasswordButton.IsEnabled = true;
                ChangePasswordButton.Content = "שמירה וכניסה";
            }

            // הסיסמה כבר הוחלפה בשרת. אם פתיחת הקיוסק נכשלה (למשל תקלת רשת) - חוזרים
            // למסך הכניסה, כי הסיסמה הזמנית כבר לא בתוקף והכניסה הבאה היא עם החדשה.
            ClearPending();
            try
            {
                await StartSessionAsync(user);
            }
            catch (Exception ex)
            {
                App.LogError("כשל בפתיחת הקיוסק אחרי החלפת סיסמה", ex);
                ShowLoginPanel();
                ShowError("הסיסמה החדשה נשמרה. נא להתחבר שוב עם הסיסמה החדשה.");
            }
        }

        private async void OnForgotPasswordClick(object sender, RoutedEventArgs e)
        {
            var username = UsernameBox.Text.Trim();
            if (string.IsNullOrEmpty(username))
            {
                ShowError("כדי לשחזר סיסמה, הקלידו קודם את תעודת הזהות ולחצו שוב על \"שכחתי סיסמה\"");
                UsernameBox.Focus();
                return;
            }

            ForgotPasswordButton.IsEnabled = false;
            HideError();
            try
            {
                var info = await App.ApiClient.GetPasswordResetInfoAsync(username);
                if (string.IsNullOrWhiteSpace(info.ResetPhone))
                {
                    ShowError("שחזור סיסמה בטלפון אינו זמין כרגע. יש לפנות לצוות הספרייה.");
                    return;
                }
                if (!info.Found)
                {
                    ShowError("לא נמצא חשבון פעיל עם תעודת הזהות שהוקלדה. יש לבדוק את המספר, או לפנות לצוות הספרייה.");
                    return;
                }

                ResetPhoneText.Text = info.ResetPhone;
                MaskedPhoneText.Text = info.MaskedPhone;
                ForgotInstructionsText.Text =
                    "במערכת הטלפונית תתבקשו להקיש את תעודת הזהות, ותשמעו סיסמה זמנית. " +
                    "היכנסו איתה כאן, ותתבקשו לבחור סיסמה חדשה. " +
                    "אם המספר הזה כבר לא שלכם, יש לפנות לצוות הספרייה.";

                LoginPanel.Visibility = Visibility.Collapsed;
                ForgotPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                App.LogError("כשל בטעינת פרטי שחזור הסיסמה", ex);
                ShowError(ex.Message);
            }
            finally
            {
                ForgotPasswordButton.IsEnabled = true;
            }
        }

        private void OnBackToLoginClick(object sender, RoutedEventArgs e)
        {
            ShowPanel(LoginPanel);
            PasswordBox.Clear();
            PasswordBox.Focus();
        }

        private void OnCancelChangeClick(object sender, RoutedEventArgs e)
        {
            ShowLoginPanel();
        }

        private async System.Threading.Tasks.Task StartSessionAsync(KioskUser user)
        {
            var config = await App.ApiClient.GetConfigAsync();
            App.Session.Start(user, config);
            App.StartSessionServices(); // חיוב על הדפסה - לאורך כל ההתחברות

            var appPicker = new Views.AppPickerWindow();
            Application.Current.MainWindow = appPicker;
            appPicker.Show();
            Close();
        }

        private void ShowChangePasswordPanel(string username, string tempPassword)
        {
            _pendingUsername = username;
            _pendingTempPassword = tempPassword;
            HideError();
            LoginPanel.Visibility = Visibility.Collapsed;
            ChangePasswordPanel.Visibility = Visibility.Visible;
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            NewPasswordBox.Focus();
        }

        private void ShowLoginPanel()
        {
            ClearPending();
            ShowPanel(LoginPanel);
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            PasswordBox.Clear();
            if (string.IsNullOrEmpty(UsernameBox.Text)) UsernameBox.Focus(); else PasswordBox.Focus();
        }

        /// <summary>מציג את הכרטיס המרכזי עם אחד המסכים שבו (כניסה / פתיחת חשבון / ...)</summary>
        private void ShowPanel(FrameworkElement panel)
        {
            HideError();
            ChoicePanel.Visibility = Visibility.Collapsed;
            SignupOfferOverlay.Visibility = Visibility.Collapsed;
            CardBorder.Visibility = Visibility.Visible;
            foreach (var p in new FrameworkElement[] { LoginPanel, SignupPanel, ForgotPanel, ChangePasswordPanel })
            {
                p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>חזרה למסך הפתיחה (שני הריבועים) - מוחק כל מה שהוקלד</summary>
        private void ShowChoice()
        {
            ClearPending();
            HideError();
            UsernameBox.Clear();
            PasswordBox.Clear();
            ClearSignupForm();
            SignupOfferOverlay.Visibility = Visibility.Collapsed;
            CardBorder.Visibility = Visibility.Collapsed;
            ChoicePanel.Visibility = Visibility.Visible;
        }

        private void OnChooseLoginClick(object sender, RoutedEventArgs e) => ShowLoginPanel();

        private void ResetIfIdle()
        {
            if (_paymentOpen || ChoicePanel.Visibility == Visibility.Visible) return;
            if (ChangePasswordPanel.Visibility == Visibility.Visible) return; // באמצע החלפת סיסמה זמנית
            if (DateTime.Now - _lastActivity < IdleReset) return;
            ShowChoice();
        }

        private void OnBackToChoiceClick(object sender, RoutedEventArgs e) => ShowChoice();

        // ===================== פתיחת חשבון =====================

        private void OnChooseSignupClick(object sender, RoutedEventArgs e)
        {
            ClearSignupForm();
            ShowPanel(SignupPanel);
            SignupNameBox.Focus();
        }

        private void ClearSignupForm()
        {
            SignupNameBox.Clear();
            SignupTzBox.Clear();
            SignupPhoneBox.Clear();
            SignupPasswordBox.Clear();
            SignupPasswordVisibleBox.Clear();
            ShowSignupPasswordCheck.IsChecked = false;
            _signupDetails = null;
            _signupAmount = 0;
            _communityJoinUrl = "";
            _kevaIdsBeforeJoin = null;
            _joinCheckGeneration++; // עוצר בדיקת הצטרפות שרצה ברקע
        }

        private void OnDigitsOnlyInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void OnPhoneInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(c => char.IsDigit(c) || c == '-');
        }

        private void OnShowSignupPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ShowSignupPasswordCheck.IsChecked == true)
            {
                SignupPasswordVisibleBox.Text = SignupPasswordBox.Password;
                SignupPasswordBox.Visibility = Visibility.Collapsed;
                SignupPasswordVisibleBox.Visibility = Visibility.Visible;
            }
            else
            {
                SignupPasswordBox.Password = SignupPasswordVisibleBox.Text;
                SignupPasswordVisibleBox.Visibility = Visibility.Collapsed;
                SignupPasswordBox.Visibility = Visibility.Visible;
            }
        }

        private string SignupPassword =>
            ShowSignupPasswordCheck.IsChecked == true ? SignupPasswordVisibleBox.Text : SignupPasswordBox.Password;

        private void OnSignupFieldKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OnSignupContinueClick(sender, e);
        }

        /// <summary>ספרת ביקורת של תעודת זהות ישראלית (אותה בדיקה גם בשרת)</summary>
        private static bool IsValidIsraeliId(string value)
        {
            if (!Regex.IsMatch(value, @"^\d{5,9}$")) return false;
            var padded = value.PadLeft(9, '0');
            if (padded.All(c => c == '0')) return false;
            var sum = 0;
            for (var i = 0; i < 9; i++)
            {
                var n = (padded[i] - '0') * ((i % 2) + 1);
                sum += n > 9 ? n - 9 : n;
            }
            return sum % 10 == 0;
        }

        private async void OnSignupContinueClick(object sender, RoutedEventArgs e)
        {
            HideError();
            var name = Regex.Replace(SignupNameBox.Text.Trim(), @"\s+", " ");
            var tz = SignupTzBox.Text.Trim();
            var phone = SignupPhoneBox.Text.Trim().Replace("-", "");
            var password = SignupPassword;

            if (name.Split(' ').Length < 2)
            {
                ShowError("יש להזין שם מלא (שם פרטי ושם משפחה)");
                SignupNameBox.Focus();
                return;
            }
            if (!IsValidIsraeliId(tz))
            {
                ShowError("מספר תעודת הזהות אינו תקין. יש לבדוק את המספר (כולל ספרת הביקורת)");
                SignupTzBox.Focus();
                return;
            }
            if (!Regex.IsMatch(phone, @"^0\d{8,9}$"))
            {
                ShowError("יש להזין מספר טלפון תקין (למשל 0501234567)");
                SignupPhoneBox.Focus();
                return;
            }
            if (!Regex.IsMatch(password, @"^\d{4,8}$"))
            {
                ShowError("הסיסמה צריכה להיות בין 4 ל-8 ספרות");
                if (ShowSignupPasswordCheck.IsChecked == true) SignupPasswordVisibleBox.Focus(); else SignupPasswordBox.Focus();
                return;
            }

            var details = new SignupDetails { Tz = tz, DisplayName = name, Phone = phone, Password = password };
            SignupContinueButton.IsEnabled = false;
            SignupContinueButton.Content = "בודק...";
            try
            {
                var check = await App.ApiClient.CheckSignupAsync(details);
                _signupDetails = details;
                _signupAmount = check.SignupAmount;
                _communityJoinUrl = check.CommunityJoinUrl;
                _kevaIdsBeforeJoin = null;
                SetMemberView(check.IsCommunityMember);
                JoinStatusText.Visibility = Visibility.Collapsed;
                JoinRecheckButton.Visibility = Visibility.Collapsed;
                SignupAmountText.Text = $"כדי ליצור את החשבון יש להטעין {check.SignupAmount:0.##} ₪";
                SignupOfferOverlay.Visibility = Visibility.Visible;
                App.LogInfo($"פתיחת חשבון: פרטים אושרו (חבר קהילה: {check.IsCommunityMember})");
            }
            catch (Exception ex)
            {
                App.LogError("פתיחת חשבון: בדיקת הפרטים נכשלה", ex);
                ShowError(ex.Message);
            }
            finally
            {
                SignupContinueButton.IsEnabled = true;
                SignupContinueButton.Content = "המשך";
            }
        }

        private void OnSignupOfferBackClick(object sender, RoutedEventArgs e)
        {
            _joinCheckGeneration++;
            SignupOfferOverlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>חבר קהילה: תגית + "זכאי להנחות". אחרת: הסבר + כפתור הצטרפות (אם הוגדר דף)</summary>
        private void SetMemberView(bool isMember)
        {
            MemberSection.Visibility = isMember ? Visibility.Visible : Visibility.Collapsed;
            NonMemberSection.Visibility = isMember ? Visibility.Collapsed : Visibility.Visible;
            JoinCommunityButton.Visibility = string.IsNullOrWhiteSpace(_communityJoinUrl) ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void OnJoinCommunityClick(object sender, RoutedEventArgs e)
        {
            if (_signupDetails == null || string.IsNullOrWhiteSpace(_communityJoinUrl)) return;
            var tz = _signupDetails.Tz;

            JoinCommunityButton.IsEnabled = false;
            JoinRecheckButton.Visibility = Visibility.Collapsed;
            ShowJoinStatus("פותח את דף ההצטרפות...");
            try
            {
                // לפני הדף: אילו הוראות קבע כבר קיימות (כדי לזהות רק הוראה חדשה)
                if (_kevaIdsBeforeJoin == null)
                {
                    _kevaIdsBeforeJoin = await App.ApiClient.GetExistingKevaIdsAsync(tz);
                }
            }
            catch (Exception ex)
            {
                App.LogError("הצטרפות לחבר קהילה: כשל בבדיקת הוראות קבע קיימות", ex);
                ShowJoinStatus("לא ניתן להתחבר כרגע לנדרים פלוס. אפשר להמשיך בפתיחת החשבון ולהצטרף בהמשך.");
                JoinCommunityButton.IsEnabled = true;
                return;
            }

            var join = new JoinCommunityWindow(_communityJoinUrl) { Owner = this };
            _paymentOpen = true;
            try { join.ShowDialog(); }
            finally { _paymentOpen = false; _lastActivity = DateTime.Now; }

            JoinCommunityButton.IsEnabled = true;
            await CheckJoinedAsync(tz);
        }

        private async void OnJoinRecheckClick(object sender, RoutedEventArgs e)
        {
            if (_signupDetails != null) await CheckJoinedAsync(_signupDetails.Tz);
        }

        // הוראת קבע חדשה מופיעה ברשימת המוסד לפעמים אחרי כמה שניות - בודקים כמה פעמים
        private async Task CheckJoinedAsync(string tz)
        {
            var generation = ++_joinCheckGeneration;
            JoinRecheckButton.Visibility = Visibility.Collapsed;
            ShowJoinStatus("בודקים את ההצטרפות מול נדרים פלוס...");

            for (var attempt = 0; attempt < 8; attempt++)
            {
                if (generation != _joinCheckGeneration) return; // חזרו אחורה / התחילו מחדש
                try
                {
                    var known = (System.Collections.Generic.IEnumerable<string>?)_kevaIdsBeforeJoin ?? Array.Empty<string>();
                    if (await App.ApiClient.CheckCommunityMembershipAsync(tz, known))
                    {
                        if (generation != _joinCheckGeneration) return;
                        App.LogInfo("הצטרפות לחבר קהילה: נמצאה הוראת קבע חדשה - חבר קהילה");
                        JoinStatusText.Visibility = Visibility.Collapsed;
                        SetMemberView(true);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    App.LogError("הצטרפות לחבר קהילה: כשל בבדיקת החברות", ex);
                }
                await Task.Delay(attempt == 0 ? 2000 : 8000);
            }

            if (generation != _joinCheckGeneration) return;
            ShowJoinStatus("לא נמצאה עדיין הוראת קבע \"דמי חבר\" חדשה. אם ההצטרפות הושלמה, אפשר לבדוק שוב בעוד רגע. " +
                           "אפשר גם להמשיך בפתיחת החשבון - החברות תתעדכן בהמשך.");
            JoinRecheckButton.Visibility = Visibility.Visible;
        }

        private void ShowJoinStatus(string text)
        {
            JoinStatusText.Text = text;
            JoinStatusText.Visibility = Visibility.Visible;
        }

        private async void OnSignupPayClick(object sender, RoutedEventArgs e)
        {
            if (_signupDetails == null) return;
            var details = _signupDetails;

            var payment = new TopupWindow(details, _signupAmount) { Owner = this };
            _paymentOpen = true;
            try { payment.ShowDialog(); }
            finally { _paymentOpen = false; _lastActivity = DateTime.Now; }
            if (!payment.SignupCompleted) return; // ביטול או תשלום שלא אושר - נשארים בחלון ההטענה

            App.LogInfo("פתיחת חשבון: התשלום אושר והחשבון נפתח");
            SignupOfferOverlay.Visibility = Visibility.Collapsed;
            SignupContinueButton.IsEnabled = false;
            SignupContinueButton.Content = "נכנס לחשבון...";

            // החשבון נוצר בשרת ברגע אישור התשלום. כניסה אוטומטית - עם כמה ניסיונות,
            // למקרה שהחשבון החדש עוד לא "נראה" בשנייה הראשונה
            KioskUser? user = null;
            for (var attempt = 0; attempt < 4 && user == null; attempt++)
            {
                try
                {
                    user = await App.ApiClient.LoginAsync(details.Tz, details.Password);
                }
                catch (Exception ex)
                {
                    App.LogError($"פתיחת חשבון: כניסה אוטומטית, ניסיון {attempt + 1}", ex);
                    await Task.Delay(1500);
                }
            }

            SignupContinueButton.IsEnabled = true;
            SignupContinueButton.Content = "המשך";

            if (user != null)
            {
                try
                {
                    ClearSignupForm();
                    await StartSessionAsync(user);
                    return;
                }
                catch (Exception ex)
                {
                    App.LogError("פתיחת חשבון: כשל בפתיחת הקיוסק", ex);
                }
            }

            ClearSignupForm();
            ShowLoginPanel();
            UsernameBox.Text = details.Tz;
            PasswordBox.Focus();
            ShowError("החשבון נפתח בהצלחה! נא להיכנס עם תעודת הזהות והסיסמה שבחרתם.");
        }

        private void ClearPending()
        {
            _pendingUsername = "";
            _pendingTempPassword = "";
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }
    }
}
