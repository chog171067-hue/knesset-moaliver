/* סרגל ניווט עליון משותף - מקור אמת יחיד לתפריט (כולל לוגו והמבורגר למובייל).
   כדי להוסיף/לשנות עמוד בעתיד: יש לערוך רק את הקובץ הזה,
   וכל הדפים שקוראים אליו יתעדכנו אוטומטית. */

(function () {
    var current = location.pathname.split('/').pop() || 'index.html';

    // מקור אמת יחיד לרשימת דפי התפילה: גם לבניית התפריט הנפתח וגם לפופאפ שליחת המייל
    // (assets/*.html קוראים ל-window.sitePrayerPages כדי לבנות רשימת צ'קבוקסים מסונכרנת אוטומטית)
    var prayerPages = [
        { file: 'shabbat.html', label: 'שבתות' },
        { file: 'yemothachol.html', label: 'ימות החול' },
        { file: 'beinhazmanim.html', label: 'בין הזמנים' }
    ];
    window.sitePrayerPages = prayerPages;

    var isHome = (current === 'index.html' || current === '');
    var isPrayer = prayerPages.some(function (p) { return p.file === current; });
    var isNews = (current === 'news.html');
    var isPersonal = (current === 'personal.html');

    var html =
        '<header>' +
        '  <div class="nav-container">' +
        '    <a href="index.html" class="nav-logo-link">' +
        '      <img src="logo.png" alt="לוגו בית כנסת מוהליבר" class="nav-logo">' +
        '    </a>' +
        '    <ul class="nav-menu" id="navMenu">' +
        '      <li class="nav-item' + (isHome ? ' active' : '') + '"><a href="index.html">דף הבית</a></li>' +
        '      <li class="nav-item' + (isPrayer ? ' active' : '') + '" id="prayersDropdown">' +
        '        <button class="dropdown-toggle" onclick="toggleDropdown(event)">זמני התפילות ▾</button>' +
        '        <ul class="dropdown-menu">' +
        prayerPages.map(function (p) {
            return '<li><a href="' + p.file + '">' + p.label + '</a></li>';
        }).join('') +
        '        </ul>' +
        '      </li>' +
        '      <li class="nav-item' + (isNews ? ' active' : '') + '"><a href="news.html">מהנעשה ונשמע</a></li>' +
        '      <li class="nav-item' + (isPersonal ? ' active' : '') + '"><a href="personal.html">אזור אישי</a></li>' +
        '    </ul>' +
        '    <div class="nav-side-controls">' +
        '      <button class="hamburger" onclick="toggleMenu()" id="hamburgerBtn" aria-expanded="false" aria-controls="navMenu" aria-label="תפריט">' +
        '        <span></span><span></span><span></span>' +
        '      </button>' +
        '      <div id="maw-header-slot"></div>' +
        '    </div>' +
        '  </div>' +
        '</header>';

    var placeholder = document.getElementById('site-header');
    if (placeholder) {
        placeholder.outerHTML = html;
    } else {
        // רשת ביטחון: אם מישהו שכח את ה-placeholder, מוסיפים בתחילת ה-body בכל זאת
        document.body.insertAdjacentHTML('afterbegin', html);
    }

    // הסרגל קבוע (position: fixed) כדי שיישאר מרחף למעלה גם בעת גלילה - לכן צריך
    // "לפצות" על זה עם ריווח עליון לתוכן הדף, בגובה המדויק של הסרגל בפועל.
    function updateHeaderOffset() {
        var headerEl = document.querySelector('header');
        if (headerEl) {
            document.body.style.paddingTop = headerEl.offsetHeight + 'px';
        }
    }
    updateHeaderOffset();
    window.addEventListener('resize', updateHeaderOffset);
    var logoImg = document.querySelector('.nav-logo');
    if (logoImg) logoImg.addEventListener('load', updateHeaderOffset);

    // פונקציות גלובליות עבור כפתורי ההמבורגר והדרופדאון שבתוך ה-HTML שהוזרק
    function closeMenu() {
        var menu = document.getElementById('navMenu');
        var btn = document.getElementById('hamburgerBtn');
        var dropdown = document.getElementById('prayersDropdown');
        if (menu) menu.classList.remove('open');
        if (btn) { btn.classList.remove('open'); btn.setAttribute('aria-expanded', 'false'); }
        if (dropdown) dropdown.classList.remove('open-dropdown');
    }

    window.toggleMenu = function () {
        var menu = document.getElementById('navMenu');
        var btn = document.getElementById('hamburgerBtn');
        var isOpen = menu.classList.toggle('open');
        if (btn) { btn.classList.toggle('open', isOpen); btn.setAttribute('aria-expanded', String(isOpen)); }
        if (!isOpen) {
            var dropdown = document.getElementById('prayersDropdown');
            if (dropdown) dropdown.classList.remove('open-dropdown');
        }
    };

    window.toggleDropdown = function (event) {
        if (window.innerWidth <= 768) {
            event.preventDefault();
            var dropdown = document.getElementById('prayersDropdown');
            dropdown.classList.toggle('open-dropdown');
        }
    };

    // סגירת התפריט בלחיצה מחוץ לו, ובבחירת קישור מתוכו (מובייל)
    document.addEventListener('click', function (event) {
        var menu = document.getElementById('navMenu');
        var btn = document.getElementById('hamburgerBtn');
        if (!menu || !menu.classList.contains('open')) return;
        if (menu.contains(event.target) && event.target.tagName === 'A') {
            closeMenu();
            return;
        }
        if (!menu.contains(event.target) && event.target !== btn && (!btn || !btn.contains(event.target))) {
            closeMenu();
        }
    });

    // מזהה אנונימי קבוע למכשיר/דפדפן הזה (לא מזהה אישית - רק ערך אקראי) - נשמר
    // לוקאלית, ומשמש רק כדי להעריך בדף הניהול כמה מהכניסות הן ממכשירים שונים
    // לעומת אותו מכשיר שחוזר. לא נשלח לשום מקום מלבד track-visit.js.
    function getVisitorId() {
        try {
            var key = 'mohliver_visitor_id';
            var id = localStorage.getItem(key);
            if (!id) {
                id = (window.crypto && crypto.randomUUID) ? crypto.randomUUID() : ('v-' + Date.now() + '-' + Math.random().toString(36).slice(2));
                localStorage.setItem(key, id);
            }
            return id;
        } catch (e) {
            return null; // localStorage חסום/לא זמין - עדיין נספור את הצפייה, רק בלי שיוך למכשיר
        }
    }

    // מונה כניסות לאתר (best-effort, בלי לחכות לתשובה) - למסך "כניסות לאתר"
    // בדף הניהול. כשל כאן לא אמור להשפיע בשום צורה על טעינת הדף.
    try {
        fetch('/.netlify/functions/track-visit', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ visitorId: getVisitorId() }),
            keepalive: true
        });
    } catch (e) { /* לא קריטי */ }
})();
