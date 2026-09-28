/* מפענח משותף לגיליון שבו שחרית, מנחה וערבית נמצאים יחד בלשונית אחת (למשל חול המועד).
   משמש גם את הדף בדפדפן (window.PrayerSections) וגם את פונקציית שליחת המיילים
   (require מ-netlify/functions/send-schedule.js), כדי ששניהם יציגו בדיוק אותם זמנים.

   נתמכים שני מבנים של הגיליון:
   1. זה מתחת לזה: שורה עם שם התפילה (בלי שעה) פותחת קטע, ואחריה שורות שעה + מקום.
      גם עמודה שבה כתוב שם התפילה בכל שורה נתמכת.
   2. זה לצד זה: שורת כותרת עם שמות התפילות, כל אחת מעל זוג העמודות (שעה, מקום) שלה.
   מקום ריק בשורה מקבל את המקום שמעליו באותה תפילה (כך נראה תא ממוזג בייצוא ל-CSV). */

(function (root, factory) {
    if (typeof module === 'object' && module.exports) module.exports = factory();
    else root.PrayerSections = factory();
})(typeof self !== 'undefined' ? self : this, function () {
    var PRAYERS = ['שחרית', 'מנחה', 'ערבית'];

    // מפענח CSV מלא (כולל תאים במרכאות שמכילים פסיקים או מרכאות כפולות)
    function parseCsv(text) {
        var rows = [], row = [], cell = '', inQuotes = false;
        for (var i = 0; i < text.length; i++) {
            var ch = text[i];
            if (inQuotes) {
                if (ch === '"' && text[i + 1] === '"') { cell += '"'; i++; }
                else if (ch === '"') inQuotes = false;
                else cell += ch;
            } else if (ch === '"') inQuotes = true;
            else if (ch === ',') { row.push(cell); cell = ''; }
            else if (ch === '\n') { row.push(cell); rows.push(row); row = []; cell = ''; }
            else if (ch !== '\r') cell += ch;
        }
        row.push(cell);
        rows.push(row);
        return rows.map(function (r) { return r.map(function (c) { return c.trim(); }); });
    }

    function isTimeLike(s) {
        return /\d{1,2}:\d{2}/.test(s || '');
    }

    // קוצץ שניות משעה מובילה ("18:25:00" -> "18:25") ושומר על טקסט חופשי שאחריה
    function normalizeTime(s) {
        var m = (s || '').match(/^(\d{1,2}:\d{2})(:\d{2})?(.*)$/);
        return m ? (m[1] + (m[3] || '')).trim() : (s || '');
    }

    function prayerOf(cell) {
        if (!cell || isTimeLike(cell)) return null;
        for (var i = 0; i < PRAYERS.length; i++) {
            if (cell.indexOf(PRAYERS[i]) !== -1) return PRAYERS[i];
        }
        return null;
    }

    // מוציא { time, place } משורת נתונים: השעה היא התא הראשון שמכיל שעה,
    // והמקום הוא שאר התאים שאחריה (בלי תאים שהם רק שם התפילה)
    function extractRow(cells) {
        var timeIdx = -1;
        for (var i = 0; i < cells.length; i++) {
            if (isTimeLike(cells[i])) { timeIdx = i; break; }
        }
        if (timeIdx === -1) return null;
        var place = cells.slice(timeIdx + 1).filter(function (c) {
            return c && !prayerOf(c);
        }).join(' - ');
        return { time: normalizeTime(cells[timeIdx]), place: place };
    }

    function parsePrayerSections(csvText) {
        var rows = parseCsv(csvText);
        var result = {};
        PRAYERS.forEach(function (p) { result[p] = []; });

        function add(prayer, entry) {
            if (!prayer || !entry) return;
            var list = result[prayer];
            if (!entry.place && list.length) entry.place = list[list.length - 1].place;
            list.push(entry);
        }

        // מבנה "זה לצד זה": שורה שיש בה שתי תפילות שונות או יותר בתאים נפרדים
        for (var r = 0; r < rows.length; r++) {
            var hits = [];
            rows[r].forEach(function (c, idx) {
                var p = prayerOf(c);
                if (p && !hits.some(function (h) { return h.prayer === p; })) hits.push({ prayer: p, col: idx });
            });
            if (hits.length >= 2) {
                rows.slice(r + 1).forEach(function (cells) {
                    hits.forEach(function (h, i) {
                        var end = i + 1 < hits.length ? hits[i + 1].col : cells.length;
                        add(h.prayer, extractRow(cells.slice(h.col, end)));
                    });
                });
                return result;
            }
        }

        // מבנה "זה מתחת לזה"
        var current = null;
        rows.forEach(function (cells) {
            var entry = extractRow(cells);
            var named = null;
            for (var i = 0; i < cells.length && !named; i++) named = prayerOf(cells[i]);
            if (!entry) {
                if (named) current = named;
                return;
            }
            add(named || current, entry);
        });
        return result;
    }

    return { PRAYERS: PRAYERS, parsePrayerSections: parsePrayerSections };
});
