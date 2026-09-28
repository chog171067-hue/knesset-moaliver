/* מפענח משותף לגיליון שבו שחרית, מנחה וערבית נמצאים יחד בלשונית אחת (למשל חול המועד).
   משמש גם את הדף בדפדפן (window.PrayerSections) וגם את פונקציית שליחת המיילים
   (require מ-netlify/functions/send-schedule.js), כדי ששניהם יציגו בדיוק אותם זמנים.

   נתמכים שני מבנים של הגיליון:
   1. זה מתחת לזה: שורה עם שם התפילה (בלי שעה) פותחת קטע, ואחריה שורות שעה + מקום.
      גם עמודה שבה כתוב שם התפילה בכל שורה נתמכת.
   2. זה לצד זה: שורת כותרת עם שמות התפילות, כל אחת מעל זוג העמודות (שעה, מקום) שלה.
   מקום ריק בשורה מקבל את המקום שמעליו באותה תפילה (כך נראה תא ממוזג בייצוא ל-CSV).

   התוצאה היא אובייקט: שם קטע -> רשימת { time, place }, לפי סדר ההופעה בגיליון. מלבד
   שלוש התפילות, גם כותרת במבנה "זה מתחת לזה" שאינה שם של תפילה (למשל "תיקון ליל
   הושענא רבה") פותחת קטע משלה, אם יש תחתיה זמנים. תפילה שלא הופיעה כלל מתווספת בסוף
   כרשימה ריקה, כדי שהדף יוכל להציג לה "הזמנים יפורסמו בקרוב". */

(function (root, factory) {
    if (typeof module === 'object' && module.exports) module.exports = factory();
    else root.PrayerSections = factory();
})(typeof self !== 'undefined' ? self : this, function () {
    var PRAYERS = ['שחרית', 'מנחה', 'ערבית'];
    // שמות נוספים שבהם תפילה יכולה להופיע בגיליון (למשל "מעריב" במקום "ערבית")
    var ALIASES = { 'שחרית': ['שחרית'], 'מנחה': ['מנחה'], 'ערבית': ['ערבית', 'מעריב'] };

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

    // מילים שמופיעות בשורת כותרות עמודות או בהערה, ולא בכותרת של קטע
    var NOT_TITLE_WORDS = ['שעה', 'מקום', 'מיקום', 'תפילה', 'time', 'location'];

    // כותרת קטע שאינה תפילה: שורה בלי שעה עם תא אחד בלבד
    function extraTitleOf(cells) {
        var filled = cells.filter(function (c) { return c; });
        if (filled.length !== 1) return null;
        var lower = filled[0].toLowerCase();
        if (NOT_TITLE_WORDS.some(function (w) { return lower.indexOf(w) !== -1; })) return null;
        return filled[0];
    }

    function prayerOf(cell) {
        if (!cell || isTimeLike(cell)) return null;
        for (var i = 0; i < PRAYERS.length; i++) {
            var names = ALIASES[PRAYERS[i]];
            for (var j = 0; j < names.length; j++) {
                if (cell.indexOf(names[j]) !== -1) return PRAYERS[i];
            }
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
        // משלים תפילות חסרות כרשימה ריקה, במקומן הרגיל ביחס לתפילות האחרות
        function withAllPrayers() {
            var keys = Object.keys(result);
            PRAYERS.forEach(function (p, order) {
                if (result[p]) return;
                var before = -1;
                for (var i = 0; i < keys.length && before === -1; i++) {
                    if (PRAYERS.indexOf(keys[i]) > order) before = i;
                }
                if (before === -1) keys.push(p); else keys.splice(before, 0, p);
            });
            var ordered = {};
            keys.forEach(function (k) { ordered[k] = result[k] || []; });
            return ordered;
        }

        function add(prayer, entry) {
            if (!prayer || !entry) return;
            var list = result[prayer] || (result[prayer] = []);
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
                return withAllPrayers();
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
                else if (extraTitleOf(cells)) current = extraTitleOf(cells);
                return;
            }
            add(named || current, entry);
        });
        return withAllPrayers();
    }

    return { PRAYERS: PRAYERS, parsePrayerSections: parsePrayerSections };
});
