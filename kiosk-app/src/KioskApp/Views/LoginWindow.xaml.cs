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
            Loaded += async (_, _) =>
            {
                UsernameBox.Focus();
                await LoadForgotPasswordButtonAsync();
            };
            LoadLogo();
        }

        // לוגו הספרייה (Assets\library-logo.png, שקוף). אם הקובץ לא נכלל בבנייה - בלי לוגו.
        private void LoadLogo()
        {
            try
            {
                var logo = new System.Windows.Media.Imaging.BitmapImage();
                logo.BeginInit();
                logo.UriSource = new Uri("pack://application:,,,/Assets/library-logo.png", UriKind.Absolute);
                logo.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                logo.EndInit();
                LogoImage.Source = logo;
                LogoImage.Visibility = Visibility.Visible;
            }
            catch
            {
                LogoImage.Visibility = Visibility.Collapsed;
            }
        }

        // ===================== מסך הפתיחה: תעודת זהות =====================

        private void OnIdentifyKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OnIdentifyClick(sender, e);
        }

        /// <summary>"המשך" אחרי הקלדת ת"ז: יש חשבון ← סיסמה. אין ← פתיחת חשבון.</summary>
        private async void OnIdentifyClick(object sender, RoutedEventArgs e)
        {
            var tz = UsernameBox.Text.Trim();
            HideError();
            if (string.IsNullOrEmpty(tz))
            {
                ShowError("יש להקליד מספר תעודת זהות");
                UsernameBox.Focus();
                return;
            }

            IdentifyButton.IsEnabled = false;
            IdentifyButton.Content = "בודק...";
            try
            {
                if (await App.ApiClient.AccountExistsAsync(tz))
                {
                    ShowLoginPanel();
                    return;
                }
                if (!IsValidIsraeliId(tz))
                {
                    ShowError("מספר תעודת הזהות אינו תקין. יש לבדוק את המספר (כולל ספרת הביקורת)");
                    UsernameBox.Focus();
                    UsernameBox.SelectAll();
                    return;
                }
                // אין חשבון - פתיחת חשבון חדש עם הת"ז שהוקלדה
                ClearSignupForm();
                SignupTzBox.Text = tz;
                ShowPanel(SignupPanel);
                SignupNameBox.Focus();
            }
            catch (Exception ex)
            {
                App.LogError("כשל בבדיקת תעודת הזהות", ex);
                ShowError(ex.Message);
            }
            finally
            {
                IdentifyButton.IsEnabled = true;
                IdentifyButton.Content = "המשך";
            }
        }

        // הכפתור "שכחתי סיסמה" מוצג רק אם בדף הניהול הוגדר מספר לשחזור. תקלה כאן
        // (למשל אין אינטרנט) רק משאירה את הכפתור מוסתר - לא חוסמת את הכניסה.
        private async System.Threading.Tasks.Task LoadForgotPasswordButtonAsync()
        {
            try
            {
                var config = await App.ApiClient.GetConfigAsync();
                // מחיר ההדפסה בתעריפים - לפי ההגדרה בדף הניהול
                if (config.PricePerPage > 0)
                {
                    PrintPriceText.Text = config.PricePerPage < 1
                        ? $"{config.PricePerPage * 100:0} אג' לעמוד"
                        : $"{config.PricePerPage:0.##} ₪ לעמוד";
                }
                if (config.UsagePricePerMinute > 0)
                {
                    UsagePriceText.Text = $"{config.UsagePricePerMinute * 10:0.##} ₪ ל-10 דקות";
                }
                else
                {
                    UsagePriceText.Text = "חינם";
                }
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

            if (string.IsNullOrEmpty(username))
            {
                ShowIdentify();
                return;
            }
            if (string.IsNullOrEmpty(password))
            {
                ShowError("יש להקליד סיסמה");
                PasswordBox.Focus();
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
                DateText.Text = now.ToString("dddd · d MMMM yyyy", hebrew);
            }
            catch
            {
                DateText.Text = "";
            }
            try
            {
                GregorianDateText.Text = now.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("he-IL"));
            }
            catch
            {
                GregorianDateText.Text = now.ToString("dd/MM/yyyy");
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
                    ShowError("שחזור סיסמה בטלפון אינו זמין כרגע. יש לפנות לגבאים.");
                    return;
                }
                if (!info.Found)
                {
                    ShowError("לא נמצא חשבון פעיל עם תעודת הזהות שהוקלדה. יש לבדוק את המספר, או לפנות לגבאים.");
                    return;
                }

                ResetPhoneText.Text = info.ResetPhone;
                MaskedPhoneText.Text = info.MaskedPhone;
                ForgotInstructionsText.Text =
                    "במערכת הטלפונית תתבקשו להקיש את תעודת הזהות, ותשמעו סיסמה זמנית. " +
                    "היכנסו איתה כאן, ותתבקשו לבחור סיסמה חדשה. " +
                    "אם המספר הזה כבר לא שלכם, יש לפנות לגבאים.";

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
            // חבר קהילה? (לתגית בלשונית, ולחיוב על זמן - חברי קהילה בחינם). תקלה כאן לא
            // חוסמת את הכניסה: עד שזה נבדק (הלשונית מנסה שוב) - לא מחייבים על זמן.
            try
            {
                var info = await App.ApiClient.GetAccountInfoAsync(user.Id);
                App.Session.IsCommunityMember = info.IsCommunityMember;
                App.Session.UpdateBalance(info.Balance);
            }
            catch (Exception ex)
            {
                App.LogError("כניסה: כשל בבדיקת חבר קהילה", ex);
            }
            App.StartSessionServices(); // חיוב על הדפסה ועל זמן - לאורך כל ההתחברות

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
            if (string.IsNullOrWhiteSpace(UsernameBox.Text))
            {
                ShowIdentify();
                return;
            }
            LoginTzText.Text = "תעודת זהות: " + UsernameBox.Text.Trim();
            ShowPanel(LoginPanel);
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            PasswordBox.Clear();
            PasswordBox.Focus();
        }

        /// <summary>מציג את הכרטיס המרכזי עם אחד המסכים שבו (כניסה / פתיחת חשבון / ...)</summary>
        private void ShowPanel(FrameworkElement panel)
        {
            HideError();
            SignupOfferOverlay.Visibility = Visibility.Collapsed;
            foreach (var p in new FrameworkElement[] { IdentifyPanel, LoginPanel, SignupPanel, ForgotPanel, ChangePasswordPanel })
            {
                p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
            }
            // טופס פתיחת החשבון ארוך - הלוגו קטן יותר כדי שהכול ייכנס במסך
            LogoImage.MaxHeight = panel == SignupPanel ? 95 : 170;
        }

        /// <summary>חזרה למסך הפתיחה (הקלדת ת"ז) - מוחק כל מה שהוקלד</summary>
        private void ShowIdentify()
        {
            ClearPending();
            UsernameBox.Clear();
            PasswordBox.Clear();
            ClearSignupForm();
            ShowPanel(IdentifyPanel);
            UsernameBox.Focus();
        }

        private void ResetIfIdle()
        {
            if (_paymentOpen) return;
            if (IdentifyPanel.Visibility == Visibility.Visible && UsernameBox.Text.Length == 0) return;
            if (ChangePasswordPanel.Visibility == Visibility.Visible) return; // באמצע החלפת סיסמה זמנית
            if (DateTime.Now - _lastActivity < IdleReset) return;
            ShowIdentify();
        }

        private void OnBackToIdentifyClick(object sender, RoutedEventArgs e) => ShowIdentify();

        // ===================== פתיחת חשבון =====================

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

            var known = _kevaIdsBeforeJoin;
            var join = new NedarimPageWindow(
                "הצטרפות לחבר קהילה",
                "ממלאים את הטופס להקמת הוראת קבע. בסיום ההצטרפות החלון ייסגר לבד ותחזרו לפתיחת החשבון.",
                _communityJoinUrl,
                () => App.ApiClient.CheckCommunityMembershipAsync(tz, known),
                pollSeconds: 12, firstCheckAfterSeconds: 15,
                successTitle: "ההצטרפות נקלטה!",
                successMessage: "החשבון יוגדר כחבר קהילה. חוזרים לפתיחת החשבון...") { Owner = this };
            _paymentOpen = true;
            try { join.ShowDialog(); }
            finally { _paymentOpen = false; _lastActivity = DateTime.Now; }

            JoinCommunityButton.IsEnabled = true;
            if (join.Completed)
            {
                JoinStatusText.Visibility = Visibility.Collapsed;
                SetMemberView(true);
                return;
            }
            // נסגר ב-X: בדיקה שקטה אחת (אולי ההוראה הוקמה רגע לפני הסגירה)
            JoinStatusText.Visibility = Visibility.Collapsed;
            try
            {
                if (await App.ApiClient.CheckCommunityMembershipAsync(tz, known)) SetMemberView(true);
            }
            catch (Exception ex)
            {
                App.LogError("הצטרפות לחבר קהילה: בדיקה אחרי סגירה", ex);
            }
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
            UsernameBox.Text = details.Tz;
            ShowLoginPanel();
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
