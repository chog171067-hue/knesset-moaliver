/* סרגל ניווט עליון משותף - מקור אמת יחיד לתפריט (כולל לוגו והמבורגר למובייל).
   כדי להוסיף/לשנות עמוד בעתיד: יש לערוך רק את הקובץ הזה,
   וכל הדפים שקוראים אליו יתעדכנו אוטומטית. */

(function () {
    var current = location.pathname.split('/').pop() || 'index.html';

    var prayerPages = ['shabbat.html', 'yemothachol.html', 'tishabav.html', 'beinhazmanim.html'];

    var isHome = (current === 'index.html' || current === '');
    var isPrayer = prayerPages.indexOf(current) !== -1;
    var isNews = (current === 'news.html');
    var isPersonal = (current === 'personal.html');

    var html =
        '<header>' +
        '  <div class="nav-container">' +
        '    <a href="index.html" class="nav-logo-link">' +
        '      <img src="logo.png" alt="לוגו בית כנסת מוהליבר" class="nav-logo">' +
        '    </a>' +
        '    <button class="hamburger" onclick="toggleMenu()">' +
        '      <span></span><span></span><span></span>' +
        '    </button>' +
        '    <ul class="nav-menu" id="navMenu">' +
        '      <li class="nav-item' + (isHome ? ' active' : '') + '"><a href="index.html">דף הבית</a></li>' +
        '      <li class="nav-item' + (isPrayer ? ' active' : '') + '" id="prayersDropdown">' +
        '        <button class="dropdown-toggle" onclick="toggleDropdown(event)">זמני התפילות ▾</button>' +
        '        <ul class="dropdown-menu">' +
        '          <li><a href="shabbat.html">שבתות</a></li>' +
        '          <li><a href="yemothachol.html">ימות החול</a></li>' +
        '          <li><a href="tishabav.html">תשעה באב</a></li>' +
        '          <li><a href="beinhazmanim.html">בין הזמנים</a></li>' +
        '        </ul>' +
        '      </li>' +
        '      <li class="nav-item' + (isNews ? ' active' : '') + '"><a href="news.html">מהנעשה ונשמע</a></li>' +
        '      <li class="nav-item' + (isPersonal ? ' active' : '') + '"><a href="personal.html">אזור אישי</a></li>' +
        '    </ul>' +
        '  </div>' +
        '</header>';

    var placeholder = document.getElementById('site-header');
    if (placeholder) {
        placeholder.outerHTML = html;
    } else {
        // רשת ביטחון: אם מישהו שכח את ה-placeholder, מוסיפים בתחילת ה-body בכל זאת
        document.body.insertAdjacentHTML('afterbegin', html);
    }

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
