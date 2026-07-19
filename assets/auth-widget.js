/**
 * auth-widget.js - רכיב הזדהות משותף לבית הכנסת המרכזי מוהליבר
 *
 * מטרתו: להוסיף כפתור "כניסה / הרשמה" (או "שלום, X" כשמחוברים) בפינה השמאלית
 * העליונה של כל דף באתר, ולנהל את כל תהליך ההזדהות (בדיקת ת"ז מול הגיליון,
 * הרשמה/התחברות, Google, שכחתי סיסמה, הגדרות חשבון, ניתוק לפי חוסר פעילות)
 * כחלון קופץ - בלי לעזוב את הדף הנוכחי.
 *
 * שימוש בכל דף באתר: להוסיף לפני </body> בדיוק שתי שורות:
 *   <script src="https://identity.netlify.com/v1/netlify-identity-widget.js"></script>
 *   <script src="assets/auth-widget.js"></script>
 * (בהנחה שהקובץ הזה נמצא בנתיב assets/auth-widget.js בריפו - אפשר לשנות לפי הצורך)
 *
 * דפים שרוצים לדעת מתי משתמש מזוהה ומקושר (כדי להציג נתונים אישיים, כמו
 * personal.html) יכולים להאזין לאירועים מותאמים אישית:
 *   document.addEventListener('mohliver:authenticated', e => { const user = e.detail.user; ... });
 *   document.addEventListener('mohliver:logout', () => { ... });
 */
(function () {
    'use strict';

    // ===== מצב פנימי =====
    let verifiedTz = null;
    let currentMode = null; // 'login' | 'signup'
    let initHandled = false;
    let confirmationHandled = false;
    let boundUser = null;

    const _hash = window.location.hash;
    const authTokenType = _hash.includes('confirmation_token') ? 'signup'
        : _hash.includes('email_change_token') ? 'email_change'
        : _hash.includes('recovery_token') ? 'recovery'
        : null;

    // ===== הזרקת CSS =====
    const style = document.createElement('style');
    style.textContent = `
        #maw-corner { position: fixed; top: 14px; left: 20px; z-index: 9999; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; }
        #maw-corner button { font-family: inherit; }
        .maw-toggle {
            background: #1a365d; color: #f7fafc; border: none; padding: 9px 16px; border-radius: 8px;
            font-size: 14px; font-weight: 600; cursor: pointer; display: flex; align-items: center; gap: 6px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.25);
        }
        .maw-toggle:hover { background: #2a4365; }
        .maw-dropdown {
            display: none; position: absolute; top: 100%; left: 0; margin-top: 6px;
            background: #fff; border-radius: 8px; box-shadow: 0 8px 20px rgba(0,0,0,0.2);
            min-width: 200px; overflow: hidden;
        }
        .maw-dropdown.open { display: block; }
        .maw-dropdown a, .maw-dropdown button {
            display: block; width: 100%; text-align: right; padding: 12px 16px; box-sizing: border-box;
            background: none; border: none; font-size: 14px; color: #1a365d; cursor: pointer; text-decoration: none;
            font-family: inherit;
        }
        .maw-dropdown a:hover, .maw-dropdown button:hover { background: #f7fafc; }
        .maw-overlay {
            display: none; position: fixed; inset: 0; background: rgba(0,0,0,0.5);
            z-index: 10000; align-items: center; justify-content: center; padding: 20px; box-sizing: border-box;
        }
        .maw-overlay.open { display: flex; }
        .maw-box {
            background: #fff; border-radius: 16px; padding: 34px 30px; max-width: 420px; width: 100%;
            text-align: right; direction: rtl; max-height: 90vh; overflow-y: auto; box-sizing: border-box;
            position: relative;
        }
        .maw-close { position: absolute; top: 14px; left: 14px; background: none; border: none; font-size: 20px; color: #999; cursor: pointer; }
        .maw-box h2, .maw-box h3 { color: #1a365d; margin-top: 0; }
        .maw-subtitle { font-size: 14px; color: #555; margin-bottom: 20px; line-height: 1.5; }
        .maw-info-box {
            background-color: #ebf8ff; border-right: 4px solid #2b6cb0; padding: 12px; border-radius: 6px;
            text-align: right; font-size: 13px; color: #2b6cb0; margin-bottom: 20px; line-height: 1.5;
        }
        .maw-form-group { margin-bottom: 16px; text-align: right; }
        .maw-form-group label { display: block; font-weight: bold; margin-bottom: 6px; color: #1a365d; font-size: 14px; }
        .maw-form-group input {
            width: 100%; padding: 12px; border: 2px solid #ddd; border-radius: 8px; font-size: 16px;
            text-align: center; box-sizing: border-box; font-family: inherit;
        }
        .maw-form-group input:focus { border-color: #1a365d; outline: none; }
        .maw-btn {
            display: block; width: 100%; padding: 13px; background: #1a365d; color: #fff; text-decoration: none;
            border-radius: 8px; font-weight: bold; font-size: 16px; border: none; cursor: pointer; box-sizing: border-box;
        }
        .maw-btn:hover { background: #2a4365; }
        .maw-btn-secondary { background: #718096; }
        .maw-btn-secondary:hover { background: #4a5568; }
        .maw-google-btn {
            background: #fff; color: #757575; border: 1px solid #ddd; display: flex; align-items: center;
            justify-content: center; gap: 10px; margin-top: 12px;
        }
        .maw-google-btn:hover { background: #f7fafc; }
        .maw-error {
            color: #c53030; background: #fff5f5; padding: 10px; border-radius: 8px; margin-bottom: 16px;
            font-weight: bold; display: none; text-align: right; line-height: 1.4; font-size: 14px;
        }
        .maw-success { display: none; color: #38a169; font-weight: bold; margin-bottom: 14px; text-align: center; }
        .maw-loading { display: none; font-weight: bold; color: #1a365d; margin: 14px 0; text-align: center; }
        .maw-forgot { display: none; text-align: center; margin-top: 10px; }
        .maw-forgot a { color: #2b6cb0; font-size: 13px; text-decoration: underline; cursor: pointer; }
        @media (max-width: 480px) {
            #maw-corner { top: 8px; left: 8px; }
        }
    `;
    document.head.appendChild(style);

    // ===== הזרקת HTML =====
    const html = `
        <div id="maw-corner">
            <button class="maw-toggle" id="mawToggle">
                <span id="mawGreeting">כניסה / הרשמה</span> <span>▾</span>
            </button>
            <div class="maw-dropdown" id="mawDropdown"></div>
        </div>

        <div class="maw-overlay" id="mawAuthOverlay">
            <div class="maw-box">
                <button class="maw-close" id="mawAuthClose" aria-label="סגור">✕</button>

                <div id="mawTzStep">
                    <h2>אזור אישי</h2>
                    <div class="maw-info-box" id="mawInfoBox">
                        ההרשמה והגישה לאזור האישי מיועדות עבור חברי הקהילה בעלי הוראת קבע פעילה.<br>
                        במידה והינך חבר קהילה ואינך מצליח להתחבר, אנא שלחו הודעה מפורטת למייל התמיכה:
                        <a href="mailto:b0799186827@gmail.com" style="color:#2b6cb0; font-weight:bold;">b0799186827@gmail.com</a>
                    </div>
                    <div class="maw-error" id="mawErrorMessage"></div>
                    <form id="mawWhitelistForm">
                        <div class="maw-form-group">
                            <label for="mawVerificationTz">הזן מספר תעודת זהות:</label>
                            <input type="text" id="mawVerificationTz" required pattern="[0-9]{4,9}">
                        </div>
                        <button type="submit" class="maw-btn">המשך</button>
                    </form>

                    <div id="mawIdentityAuthBox" style="display:none; margin-top:22px; padding-top:22px; border-top:2px dashed #e2e8f0;">
                        <p class="maw-success" id="mawSuccessMessage"></p>
                        <h3 id="mawAuthTitle"></h3>
                        <p class="maw-subtitle" id="mawAuthSubtitle" style="text-align:center;"></p>
                        <form id="mawCustomAuthForm">
                            <div class="maw-form-group" id="mawFullNameGroup" style="display:none;">
                                <label for="mawAuthFullName">שם מלא:</label>
                                <input type="text" id="mawAuthFullName">
                            </div>
                            <div class="maw-form-group">
                                <label for="mawAuthEmail">כתובת אימייל:</label>
                                <input type="email" id="mawAuthEmail" required>
                            </div>
                            <div class="maw-form-group">
                                <label for="mawAuthPassword">סיסמה:</label>
                                <input type="password" id="mawAuthPassword" required minlength="6">
                            </div>
                            <button type="submit" class="maw-btn" id="mawAuthSubmitBtn">המשך</button>
                        </form>
                        <div class="maw-forgot" id="mawForgotPasswordLink"><a id="mawForgotPasswordAnchor">שכחתי סיסמה</a></div>
                        <button class="maw-btn maw-google-btn" id="mawGoogleBtn">
                            <img src="https://upload.wikimedia.org/wikipedia/commons/c/c1/Google_%22G%22_logo.svg" alt="Google" style="width:18px;height:18px;">
                            <span id="mawGoogleBtnLabel">המשך באמצעות Google</span>
                        </button>
                    </div>

                    <div class="maw-loading" id="mawLoadingSpinner">מבצע פעולה...</div>
                </div>

                <div id="mawInfoScreen" style="display:none; text-align:center;">
                    <h2 id="mawInfoScreenTitle" style="color:#38a169;"></h2>
                    <p class="maw-subtitle" id="mawInfoScreenText"></p>
                    <button class="maw-btn" id="mawInfoScreenBtn">כניסה לאזור האישי</button>
                </div>

                <div id="mawNewPasswordStep" style="display:none;">
                    <h2 style="text-align:center;">בחירת סיסמה חדשה</h2>
                    <p class="maw-subtitle" style="text-align:center;">הזן סיסמה חדשה לחשבון שלך.</p>
                    <div class="maw-error" id="mawNewPasswordError"></div>
                    <form id="mawNewPasswordForm">
                        <div class="maw-form-group">
                            <label for="mawNewPasswordInput">סיסמה חדשה:</label>
                            <input type="password" id="mawNewPasswordInput" required minlength="6">
                        </div>
                        <button type="submit" class="maw-btn">שמור סיסמה והמשך</button>
                    </form>
                </div>
            </div>
        </div>

        <div class="maw-overlay" id="mawSettingsOverlay">
            <div class="maw-box">
                <h3>הגדרות חשבון</h3>
                <div class="maw-error" id="mawSettingsError"></div>
                <div class="maw-success" id="mawSettingsSuccess" style="display:none;"></div>
                <form id="mawSettingsForm">
                    <div class="maw-form-group">
                        <label for="mawNewEmail">כתובת אימייל:</label>
                        <input type="email" id="mawNewEmail" required>
                    </div>
                    <div class="maw-form-group">
                        <label for="mawNewPassword">סיסמה חדשה:</label>
                        <input type="password" id="mawNewPassword" minlength="6" placeholder="השאר ריק אם לא רוצים לשנות">
                    </div>
                    <button type="submit" class="maw-btn">שמור שינויים</button>
                    <button type="button" class="maw-btn maw-btn-secondary" id="mawSettingsCancel" style="margin-top:10px;">ביטול</button>
                </form>
            </div>
        </div>

        <div class="maw-overlay" id="mawInactivityOverlay">
            <div class="maw-box" style="text-align:center;">
                <h3>עדיין שם?</h3>
                <p style="color:#555;">בשל חוסר פעילות, תנותק אוטומטית מהמערכת בעוד <span id="mawInactivityCountdown">60</span> שניות.</p>
                <button class="maw-btn" id="mawStayLoggedIn">המשך להישאר מחובר</button>
            </div>
        </div>
    `;
    document.body.insertAdjacentHTML('beforeend', html);

    // ===== עזרי DOM =====
    const $ = id => document.getElementById(id);

    // ===== תפריט פינתי (כניסה/הרשמה או שלום X) =====
    function renderCornerLoggedOut() {
        $('mawGreeting').textContent = 'כניסה / הרשמה';
        $('mawDropdown').innerHTML = '';
    }

    function renderCornerLoggedIn(displayName) {
        $('mawGreeting').textContent = `שלום, ${displayName} `;
        $('mawDropdown').innerHTML = `
            <a href="personal.html">אזור אישי</a>
            <button id="mawOpenSettings">הגדרות חשבון</button>
            <button id="mawDoLogout">יציאה מהמערכת</button>
        `;
        $('mawOpenSettings').addEventListener('click', openAccountSettings);
        $('mawDoLogout').addEventListener('click', handleLogout);
    }

    $('mawToggle').addEventListener('click', () => {
        if (netlifyIdentity.currentUser() && netlifyIdentity.currentUser().app_metadata && netlifyIdentity.currentUser().app_metadata.verified_tz) {
            $('mawDropdown').classList.toggle('open');
        } else {
            openAuthModal();
        }
    });

    document.addEventListener('click', e => {
        if (!$('maw-corner').contains(e.target)) {
            $('mawDropdown').classList.remove('open');
        }
    });

    function openAuthModal() {
        resetAuthModalToTzStep();
        $('mawAuthOverlay').classList.add('open');
    }
    function closeAuthModal() {
        $('mawAuthOverlay').classList.remove('open');
    }
    $('mawAuthClose').addEventListener('click', closeAuthModal);

    function resetAuthModalToTzStep() {
        $('mawTzStep').style.display = 'block';
        $('mawInfoScreen').style.display = 'none';
        $('mawNewPasswordStep').style.display = 'none';
        $('mawWhitelistForm').style.display = 'block';
        $('mawIdentityAuthBox').style.display = 'none';
        $('mawErrorMessage').style.display = 'none';
        $('mawVerificationTz').value = '';
    }

    // ===== שלב 1: בדיקת ת"ז =====
    $('mawWhitelistForm').addEventListener('submit', async event => {
        event.preventDefault();
        const tz = $('mawVerificationTz').value;
        const errorDiv = $('mawErrorMessage');
        const loadingDiv = $('mawLoadingSpinner');
        errorDiv.style.display = 'none';
        loadingDiv.style.display = 'block';

        try {
            const response = await fetch('/.netlify/functions/verify-tz', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ tz })
            });
            const result = await response.json();
            loadingDiv.style.display = 'none';

            if (!result.allowed) {
                throw new Error('תעודת הזהות שהזנת אינה מופיעה ברשימת חברי הקהילה המאושרים.<br>במידה והינך חבר קהילה בעל הוראת קבע פעילה ואינך מצליח להתחבר, שלחו הודעה מפורטת למייל התמיכה.');
            }

            verifiedTz = tz;
            localStorage.setItem('pending_verified_tz', tz);
            $('mawWhitelistForm').style.display = 'none';
            $('mawIdentityAuthBox').style.display = 'block';

            if (result.exists === true) {
                currentMode = 'login';
                $('mawSuccessMessage').style.display = 'none';
                $('mawInfoBox').style.display = 'none';
                $('mawAuthTitle').textContent = 'התחברות לחשבון';
                $('mawAuthSubtitle').textContent = 'הזן את המייל והסיסמה שאיתם נרשמת בעבר.';
                $('mawAuthSubmitBtn').textContent = 'התחבר';
                $('mawGoogleBtnLabel').textContent = 'התחבר עם Google';
                $('mawFullNameGroup').style.display = 'none';
                $('mawAuthFullName').required = false;
                $('mawForgotPasswordLink').style.display = 'block';
            } else {
                currentMode = 'signup';
                $('mawSuccessMessage').textContent = '✓ נמצאת זכאי ברשימת חברי הקהילה!';
                $('mawSuccessMessage').style.display = 'block';
                $('mawAuthTitle').textContent = 'פתיחת חשבון חדש';
                $('mawAuthSubtitle').textContent = 'בחר מייל וסיסמה לחשבון החדש שלך.';
                $('mawAuthSubmitBtn').textContent = 'הרשם';
                $('mawGoogleBtnLabel').textContent = 'הרשם עם Google';
                $('mawFullNameGroup').style.display = 'block';
                $('mawAuthFullName').required = true;
                $('mawForgotPasswordLink').style.display = 'none';
            }
        } catch (err) {
            loadingDiv.style.display = 'none';
            errorDiv.innerHTML = err.message;
            errorDiv.style.display = 'block';
        }
    });

    // ===== שלב 2: התחברות / הרשמה =====
    $('mawCustomAuthForm').addEventListener('submit', async event => {
        event.preventDefault();
        const email = $('mawAuthEmail').value;
        const password = $('mawAuthPassword').value;
        const errorDiv = $('mawErrorMessage');
        const loadingDiv = $('mawLoadingSpinner');
        errorDiv.style.display = 'none';
        loadingDiv.style.display = 'block';

        try {
            if (currentMode === 'login') {
                const user = await netlifyIdentity.gotrue.login(email, password, true);
                loadingDiv.style.display = 'none';
                closeAuthModal();
                handleAuthenticated(user);
            } else {
                const fullName = $('mawAuthFullName').value.trim();
                await netlifyIdentity.gotrue.signup(email, password, { data: { verified_tz: verifiedTz, full_name: fullName } });
                loadingDiv.style.display = 'none';
                alert('נרשמת בהצלחה! מייל אישור נשלח לכתובת שלך. אנא אשר אותו בתיבת המייל ולאחר מכן חזור לכאן להתחבר.');
                closeAuthModal();
            }
        } catch (err) {
            loadingDiv.style.display = 'none';
            const msg = (err.message || '').toLowerCase();
            if (currentMode === 'login' && msg.includes('email not confirmed')) {
                errorDiv.innerHTML = 'חשבונך טרם אושר. אנא בדוק את תיבת המייל שלך (כולל ספאם) ואשר את הקישור.';
            } else if (currentMode === 'signup' && (msg.includes('already exists') || msg.includes('registered'))) {
                errorDiv.innerHTML = 'כבר קיים חשבון תחת אימייל זה. אם זה החשבון שלך - אנא בצע שוב את בדיקת תעודת הזהות כדי לעבור למסך ההתחברות.';
            } else if (currentMode === 'signup') {
                errorDiv.innerHTML = err.message;
            } else {
                errorDiv.innerHTML = 'שם המשתמש או הסיסמה שגויים.';
            }
            errorDiv.style.display = 'block';
        }
    });

    $('mawGoogleBtn').addEventListener('click', () => {
        netlifyIdentity.gotrue.loginExternal('google');
    });

    $('mawForgotPasswordAnchor').addEventListener('click', async event => {
        event.preventDefault();
        const email = $('mawAuthEmail').value;
        const errorDiv = $('mawErrorMessage');
        const loadingDiv = $('mawLoadingSpinner');
        errorDiv.style.display = 'none';

        if (!email) {
            errorDiv.innerHTML = 'יש להזין קודם את כתובת המייל בשדה למעלה, ואז ללחוץ שוב על "שכחתי סיסמה".';
            errorDiv.style.display = 'block';
            return;
        }
        loadingDiv.style.display = 'block';
        try {
            await netlifyIdentity.gotrue.requestPasswordRecovery(email);
            loadingDiv.style.display = 'none';
            alert('נשלח מייל לאיפוס סיסמה לכתובת ' + email + '. יש לבדוק את תיבת הדואר (כולל ספאם).');
        } catch (err) {
            loadingDiv.style.display = 'none';
            errorDiv.innerHTML = 'שגיאה בשליחת מייל איפוס: ' + err.message;
            errorDiv.style.display = 'block';
        }
    });

    // ===== מסכי מידע (אימות מייל / שינוי מייל) וסיסמה חדשה =====
    function showInfoScreen(title, text, user) {
        if (confirmationHandled) return;
        confirmationHandled = true;
        boundUser = user;
        history.replaceState(null, '', window.location.pathname);
        $('mawTzStep').style.display = 'none';
        $('mawInfoScreenTitle').textContent = title;
        $('mawInfoScreenText').textContent = text;
        $('mawInfoScreen').style.display = 'block';
        $('mawAuthOverlay').classList.add('open');
    }

    $('mawInfoScreenBtn').addEventListener('click', () => {
        closeAuthModal();
        if (boundUser) handleAuthenticated(boundUser);
    });

    function showNewPasswordScreen(user) {
        if (confirmationHandled) return;
        confirmationHandled = true;
        boundUser = user;
        history.replaceState(null, '', window.location.pathname);
        $('mawTzStep').style.display = 'none';
        $('mawNewPasswordStep').style.display = 'block';
        $('mawAuthOverlay').classList.add('open');
    }

    $('mawNewPasswordForm').addEventListener('submit', async event => {
        event.preventDefault();
        const newPassword = $('mawNewPasswordInput').value;
        const errorDiv = $('mawNewPasswordError');
        errorDiv.style.display = 'none';
        try {
            const user = boundUser || netlifyIdentity.currentUser();
            if (!user) throw new Error('פג תוקף הקישור - יש לבצע את תהליך איפוס הסיסמה מחדש');
            await user.update({ password: newPassword });
            closeAuthModal();
            handleAuthenticated(user);
        } catch (err) {
            errorDiv.innerHTML = 'שגיאה בשמירת הסיסמה: ' + err.message;
            errorDiv.style.display = 'block';
        }
    });

    function routeAuthTokenUser(user) {
        if (authTokenType === 'signup') {
            showInfoScreen('✓ האימייל אומת בהצלחה!', 'החשבון שלך פעיל כעת. לחץ כדי להיכנס לאזור האישי.', user);
        } else if (authTokenType === 'email_change') {
            showInfoScreen('✓ כתובת המייל עודכנה בהצלחה!', 'מעכשיו תוכל להתחבר עם כתובת המייל החדשה.', user);
        } else if (authTokenType === 'recovery') {
            showNewPasswordScreen(user);
        } else {
            handleAuthenticated(user);
        }
    }

    // ===== נקודת כניסה יחידה לכל מקרה של "יש לנו משתמש מחובר" =====
    async function handleAuthenticated(user) {
        const boundTz = user.app_metadata && user.app_metadata.verified_tz;

        if (boundTz) {
            const savedName = user.user_metadata && user.user_metadata.full_name;
            renderCornerLoggedIn(savedName || user.email);
            resetInactivityTimer();
            document.dispatchEvent(new CustomEvent('mohliver:authenticated', { detail: { user } }));
            return;
        }

        const pendingTz = verifiedTz || localStorage.getItem('pending_verified_tz');
        if (!pendingTz) {
            // חשבון קיים בלי ת"ז מקושרת - פותחים את המודאל ומבקשים ת"ז פעם אחרונה
            resetAuthModalToTzStep();
            $('mawInfoBox').style.display = 'block';
            $('mawInfoBox').innerHTML = '<b>החשבון אומת בהצלחה!</b><br>כדי לקשר את הנתונים שלך, אנא הזן את תעודת הזהות שלך פעם אחת אחרונה:';
            $('mawAuthOverlay').classList.add('open');
            return;
        }

        try {
            const token = await user.jwt();
            const res = await fetch('/.netlify/functions/bind-tz', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${token}` },
                body: JSON.stringify({ tz: pendingTz })
            });
            const result = await res.json();
            if (!res.ok || !result.success) {
                throw new Error(result.error || 'לא ניתן היה לקשר את תעודת הזהות לחשבון');
            }
            await user.jwt(true);
            localStorage.removeItem('pending_verified_tz');
            handleAuthenticated(user);
        } catch (err) {
            resetAuthModalToTzStep();
            $('mawErrorMessage').innerHTML = err.message;
            $('mawErrorMessage').style.display = 'block';
            $('mawAuthOverlay').classList.add('open');
            netlifyIdentity.logout();
        }
    }

    function handleLogout() {
        verifiedTz = null;
        currentMode = null;
        localStorage.removeItem('pending_verified_tz');
        if (typeof netlifyIdentity !== 'undefined') netlifyIdentity.logout();
        renderCornerLoggedOut();
        clearTimeout(inactivityWarnTimer);
        clearTimeout(inactivityLogoutTimer);
        clearInterval(inactivityCountdownInterval);
        $('mawInactivityOverlay').classList.remove('open');
        document.dispatchEvent(new CustomEvent('mohliver:logout'));
    }

    // ===== הגדרות חשבון =====
    function openAccountSettings() {
        $('mawDropdown').classList.remove('open');
        const currentUser = netlifyIdentity.currentUser();
        $('mawNewEmail').value = currentUser ? currentUser.email : '';
        $('mawNewPassword').value = '';
        $('mawSettingsError').style.display = 'none';
        $('mawSettingsSuccess').style.display = 'none';
        $('mawSettingsOverlay').classList.add('open');
    }
    $('mawSettingsCancel').addEventListener('click', () => $('mawSettingsOverlay').classList.remove('open'));

    $('mawSettingsForm').addEventListener('submit', async event => {
        event.preventDefault();
        const newEmail = $('mawNewEmail').value;
        const newPassword = $('mawNewPassword').value;
        const errorDiv = $('mawSettingsError');
        const successDiv = $('mawSettingsSuccess');
        errorDiv.style.display = 'none';
        successDiv.style.display = 'none';
        try {
            const currentUser = netlifyIdentity.currentUser();
            if (!currentUser) throw new Error('יש להתחבר מחדש');
            const updates = {};
            if (newEmail && newEmail !== currentUser.email) updates.email = newEmail;
            if (newPassword) updates.password = newPassword;
            if (Object.keys(updates).length === 0) {
                $('mawSettingsOverlay').classList.remove('open');
                return;
            }
            await currentUser.update(updates);
            successDiv.textContent = updates.email
                ? 'העדכון בוצע. אם שינית את כתובת המייל, נשלח אליה מייל אימות - יש לאשר אותו כדי שהשינוי ייכנס לתוקף.'
                : 'הסיסמה עודכנה בהצלחה.';
            successDiv.style.display = 'block';
            $('mawNewPassword').value = '';
        } catch (err) {
            errorDiv.innerHTML = 'שגיאה בעדכון: ' + err.message;
            errorDiv.style.display = 'block';
        }
    });

    // ===== ניתוק אוטומטי לאחר 10 דקות של חוסר פעילות =====
    const INACTIVITY_LIMIT_MS = 10 * 60 * 1000;
    const INACTIVITY_WARNING_MS = 60 * 1000;
    let inactivityWarnTimer = null, inactivityLogoutTimer = null, inactivityCountdownInterval = null;

    function isCurrentlyAuthenticated() {
        const u = netlifyIdentity.currentUser();
        return !!(u && u.app_metadata && u.app_metadata.verified_tz);
    }

    function resetInactivityTimer() {
        clearTimeout(inactivityWarnTimer);
        clearTimeout(inactivityLogoutTimer);
        clearInterval(inactivityCountdownInterval);
        $('mawInactivityOverlay').classList.remove('open');
        if (!isCurrentlyAuthenticated()) return;
        inactivityWarnTimer = setTimeout(showInactivityWarning, INACTIVITY_LIMIT_MS - INACTIVITY_WARNING_MS);
    }

    function showInactivityWarning() {
        let secondsLeft = INACTIVITY_WARNING_MS / 1000;
        $('mawInactivityCountdown').textContent = secondsLeft;
        $('mawInactivityOverlay').classList.add('open');
        inactivityCountdownInterval = setInterval(() => {
            secondsLeft--;
            $('mawInactivityCountdown').textContent = Math.max(secondsLeft, 0);
            if (secondsLeft <= 0) clearInterval(inactivityCountdownInterval);
        }, 1000);
        inactivityLogoutTimer = setTimeout(() => {
            $('mawInactivityOverlay').classList.remove('open');
            handleLogout();
        }, INACTIVITY_WARNING_MS);
    }

    $('mawStayLoggedIn').addEventListener('click', resetInactivityTimer);

    ['mousemove', 'keydown', 'click', 'scroll', 'touchstart'].forEach(evt => {
        document.addEventListener(evt, () => {
            if (isCurrentlyAuthenticated() && !$('mawInactivityOverlay').classList.contains('open')) {
                resetInactivityTimer();
            }
        }, { passive: true });
    });

    // ===== אתחול (init / login / logout events) =====
    function safeHandleInit(user) {
        if (initHandled) return;
        initHandled = true;
        if (user) {
            routeAuthTokenUser(user);
        } else {
            renderCornerLoggedOut();
        }
    }

    if (typeof netlifyIdentity !== 'undefined') {
        netlifyIdentity.on('init', safeHandleInit);
        netlifyIdentity.on('login', user => {
            netlifyIdentity.close();
            routeAuthTokenUser(user);
        });
        netlifyIdentity.on('logout', () => {
            renderCornerLoggedOut();
        });
        setTimeout(() => {
            if (!initHandled) safeHandleInit(netlifyIdentity.currentUser());
        }, 1500);
    } else {
        console.error('auth-widget.js: netlify-identity-widget.js לא נטען - יש לוודא שהוא כלול לפני קובץ זה');
    }
})();
