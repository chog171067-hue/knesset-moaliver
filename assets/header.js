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
        { file: 'tishabav.html', label: 'תשעה באב' },
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
        '      <button class="hamburger" onclick="toggleMenu()">' +
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
    window.toggleMenu = function () {
        var menu = document.getElementById('navMenu');
        menu.classList.toggle('open');
    };

    window.toggleDropdown = function (event) {
        if (window.innerWidth <= 768) {
            event.preventDefault();
            var dropdown = document.getElementById('prayersDropdown');
            dropdown.classList.toggle('open-dropdown');
        }
    };
})();
