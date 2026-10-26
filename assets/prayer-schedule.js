/* חישוב זמני התפילות האוטומטי - מקור אמת משותף לדפי התפילה בדפדפן (window.PrayerSchedule),
   לפונקציית שליחת המיילים (require מ-netlify/functions/send-schedule.js) ולתצוגה המקדימה
   בדף הניהול, כדי שכולם יציגו בדיוק אותם זמנים.

   הקלט:
   - zmanim: התוכן של assets/zmanim.json - לכל תאריך (YYYY-MM-DD) נץ, שקיעה, סוף זמן ק"ש
     וצאת השבת (רק בשבתות וחגים), בדקות שלמות, מתוך לוח עתים לבינה.
   - config: ההגדרות שנשמרות בטאב "הגדרת תפילות" בדף הניהול (ראו defaultConfig).

   סוגי מניין (minyan.type):
   - fixed        שעה קבועה (minyan.time)
   - zman         לפי זמן היום: base (netz/sunset/shma/tzeit) + offset דקות (שלילי = לפני),
                  ואז עיגול (round: down/up/nearest/none, לכפולה של step). בטבלת ימות החול
                  הזמן מחושב לכל יום א'-ה' ונלקח המוקדם או המאוחר (weekDay: earliest/latest)
   - weeklyMincha מנחה שבועית: 15 דק' לפני השקיעה המוקדמת בימים א'-ה', מעוגל ל-5 דק' שלפני.
                  כך התנאי "לא פחות מ-15 לפני המוקדמת" תמיד מתקיים; נבדק גם שהמניין לא יותר
                  מ-20 דק' לפני השקיעה בשאר הימים - מותר יום אחד חריג עד 25 דק', ומעבר לזה
                  השבוע מסומן לבדיקה (warn)
   - weeklyMaariv ערבית שבועית: ההפך - 20 דק' אחרי השקיעה המאוחרת, מעוגל ל-5 דק' שאחרי,
                  עד 25 דק' אחרי השקיעה בשאר הימים, ויום אחד חריג עד 30
   - text         טקסט חופשי במקום שעה (למשל "נץ החמה")
   כל המספרים של המנחה והערבית השבועיות ניתנים לשינוי ב-config.weekly. */

(function (root, factory) {
    if (typeof module === 'object' && module.exports) module.exports = factory();
    else root.PrayerSchedule = factory();
})(typeof self !== 'undefined' ? self : this, function () {
    // day: 'week' = טבלה של ימות החול (א'-ה'), 'fri'/'sat' = היום שלפיו מחושבים זמני הטבלה
    var PAGES = {
        yemothachol: {
            label: 'ימות החול',
            tables: [
                { id: 'shacharit', title: 'שחרית', day: 'week' },
                { id: 'mincha', title: 'מנחה', day: 'week' },
                { id: 'maariv', title: 'ערבית', day: 'week' }
            ]
        },
        shabbat: {
            label: 'שבתות',
            tables: [
                { id: 'minchaErev', title: 'מנחה - ערב שבת', day: 'fri' },
                { id: 'maarivLeil', title: 'ערבית - ליל שבת', day: 'fri' },
                { id: 'shacharit', title: 'שחרית - שבת', day: 'sat' },
                { id: 'mincha', title: 'מנחה - שבת', day: 'sat' },
                { id: 'maarivMotzei', title: 'ערבית - מוצאי שבת', day: 'sat' }
            ]
        }
    };

    var BASES = { netz: 'הנץ', sunset: 'שקיעה', shma: 'סוף זמן ק"ש (גר"א)', tzeit: 'צאת השבת' };
    var TYPES = ['fixed', 'zman', 'weeklyMincha', 'weeklyMaariv', 'text'];
    var ROUNDS = ['down', 'up', 'nearest', 'none'];

    var DEFAULT_WEEKLY = {
        minchaBefore: 15, minchaMaxBefore: 20, minchaSingleDayMax: 25,
        maarivAfter: 20, maarivMaxAfter: 25, maarivSingleDayMax: 30,
        step: 5
    };

    function defaultConfig() {
        return {
            enabled: { yemothachol: false, shabbat: false },
            weekly: Object.assign({}, DEFAULT_WEEKLY),
            pages: { yemothachol: {}, shabbat: {} },
            overrides: {}
        };
    }

    // ---------- זמן ----------

    function toMinutes(hhmm) {
        var m = /^(\d{1,2}):(\d{2})/.exec(String(hhmm || ''));
        return m ? Number(m[1]) * 60 + Number(m[2]) : null;
    }

    function formatTime(mins) {
        var t = ((mins % 1440) + 1440) % 1440;
        var h = Math.floor(t / 60), m = t % 60;
        return (h < 10 ? '0' : '') + h + ':' + (m < 10 ? '0' : '') + m;
    }

    function roundTime(mins, dir, step) {
        step = Number(step) || 1;
        if (dir === 'down') return Math.floor(mins / step) * step;
        if (dir === 'up') return Math.ceil(mins / step) * step;
        if (dir === 'nearest') return Math.round(mins / step) * step;
        return mins;
    }

    // ---------- תאריכים (YYYY-MM-DD, בלי אזורי זמן - חישוב לפי UTC כדי לא לזוז ביום) ----------

    function addDays(iso, n) {
        var d = new Date(iso + 'T00:00:00Z');
        d.setUTCDate(d.getUTCDate() + n);
        return d.toISOString().slice(0, 10);
    }

    function dayOfWeek(iso) {
        return new Date(iso + 'T00:00:00Z').getUTCDay(); // 0 = ראשון
    }

    function todayInIsrael() {
        return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Jerusalem' }).format(new Date());
    }

    // יום ראשון של השבוע שמוצג בדף בתאריך נתון. בשבת דף ימות החול כבר מציג את השבוע הבא,
    // ודף השבתות מציג את השבת הקרובה (עד מוצאי שבת, ובראשון עובר לשבת הבאה)
    function weekStartFor(iso, pageId) {
        var dow = dayOfWeek(iso);
        if (pageId === 'yemothachol' && dow === 6) return addDays(iso, 1);
        return addDays(iso, -dow);
    }

    function weekDays(weekStart) {
        return [0, 1, 2, 3, 4].map(function (i) { return addDays(weekStart, i); });
    }

    // ---------- חישוב ----------

    function zmanOf(zmanim, iso, base) {
        var day = zmanim && zmanim.days && zmanim.days[iso];
        return day ? toMinutes(day[base]) : null;
    }

    function weeklySunsets(zmanim, weekStart) {
        var list = weekDays(weekStart).map(function (d) { return zmanOf(zmanim, d, 'sunset'); });
        return list.some(function (v) { return v === null; }) ? null : list;
    }

    function weeklyMincha(sunsets, w) {
        var earliest = Math.min.apply(null, sunsets);
        var time = roundTime(earliest - w.minchaBefore, 'down', w.step);
        var gaps = sunsets.map(function (s) { return s - time; });
        var over = gaps.filter(function (g) { return g > w.minchaMaxBefore; });
        var status = over.length === 0 ? 'ok' : (over.length === 1 && over[0] <= w.minchaSingleDayMax ? 'single' : 'warn');
        return { time: time, status: status, gaps: gaps };
    }

    function weeklyMaariv(sunsets, w) {
        var latest = Math.max.apply(null, sunsets);
        var time = roundTime(latest + w.maarivAfter, 'up', w.step);
        var gaps = sunsets.map(function (s) { return time - s; });
        var over = gaps.filter(function (g) { return g > w.maarivMaxAfter; });
        var status = over.length === 0 ? 'ok' : (over.length === 1 && over[0] <= w.maarivSingleDayMax ? 'single' : 'warn');
        return { time: time, status: status, gaps: gaps };
    }

    // מחזיר { time: דקות או null, text, status, gaps } עבור מניין אחד בשבוע נתון
    function computeMinyan(minyan, table, weekStart, zmanim, weekly) {
        if (minyan.type === 'text') return { text: minyan.text || '' };
        if (minyan.type === 'fixed') return { time: toMinutes(minyan.time) };

        if (minyan.type === 'weeklyMincha' || minyan.type === 'weeklyMaariv') {
            var sunsets = weeklySunsets(zmanim, weekStart);
            if (!sunsets) return { time: null };
            return minyan.type === 'weeklyMincha' ? weeklyMincha(sunsets, weekly) : weeklyMaariv(sunsets, weekly);
        }

        if (minyan.type === 'zman') {
            var days = table.day === 'week' ? weekDays(weekStart)
                : [addDays(weekStart, table.day === 'fri' ? 5 : 6)];
            var values = days.map(function (d) { return zmanOf(zmanim, d, minyan.base); })
                .filter(function (v) { return v !== null; });
            if (!values.length) return { time: null };
            var pick = minyan.weekDay === 'latest' ? Math.max.apply(null, values) : Math.min.apply(null, values);
            return { time: roundTime(pick + (Number(minyan.offset) || 0), minyan.round, minyan.step) };
        }
        return { time: null };
    }

    // כל הטבלאות של דף בשבוע נתון. מניין שאין לו זמן (למשל צאת שבת שחסר בלוח) לא מוצג.
    // override ידני מדף הניהול (config.overrides[weekStart][minyanId]) גובר על החישוב
    function computeWeek(config, zmanim, pageId, weekStart) {
        var page = PAGES[pageId];
        var weekly = Object.assign({}, DEFAULT_WEEKLY, config.weekly || {});
        var overrides = (config.overrides && config.overrides[weekStart]) || {};
        var pageConfig = (config.pages && config.pages[pageId]) || {};

        var tables = page.tables.map(function (table) {
            var rows = (pageConfig[table.id] || []).map(function (minyan) {
                var r = computeMinyan(minyan, table, weekStart, zmanim, weekly);
                var override = overrides[minyan.id];
                var display = override ? override : (r.text !== undefined ? r.text : (r.time === null ? '' : formatTime(r.time)));
                return {
                    minyanId: minyan.id,
                    type: minyan.type,
                    time: display,
                    place: minyan.place || '',
                    status: r.status || null,
                    gaps: r.gaps || null,
                    overridden: !!override
                };
            }).filter(function (r) { return r.time !== ''; });
            return { id: table.id, title: table.title, rows: rows };
        });

        var friday = zmanim.days[addDays(weekStart, 5)] || {};
        var saturday = zmanim.days[addDays(weekStart, 6)] || {};
        return {
            weekStart: weekStart,
            parasha: saturday.parasha || '',
            hebrewRange: [(zmanim.days[weekStart] || {}).heb || '', saturday.heb || friday.heb || ''],
            tables: tables
        };
    }

    // כל השבועות שיש להם נתונים בלוח - לתצוגה המקדימה בדף הניהול
    function computeYear(config, zmanim, pageId) {
        var dates = Object.keys(zmanim.days).sort();
        if (!dates.length) return [];
        var weeks = [];
        for (var ws = weekStartFor(dates[0], 'shabbat'); ws <= dates[dates.length - 1]; ws = addDays(ws, 7)) {
            if (!zmanim.days[ws] || !zmanim.days[addDays(ws, 6)]) continue; // שבוע חלקי בקצוות הלוח
            weeks.push(computeWeek(config, zmanim, pageId, ws));
        }
        return weeks;
    }

    // ---------- אימות הגדרות (בשרת לפני שמירה) ----------

    function cleanStr(s, max) {
        return String(s === undefined || s === null ? '' : s).trim().slice(0, max || 200);
    }

    function cleanInt(v, min, max, fallback) {
        var n = Math.round(Number(v));
        return isFinite(n) && n >= min && n <= max ? n : fallback;
    }

    function normalizeMinyan(m) {
        var type = TYPES.indexOf(m.type) >= 0 ? m.type : 'fixed';
        var validId = typeof m.id === 'string' && /^[a-z0-9]{4,20}$/.test(m.id);
        var out = { id: validId ? m.id : (Math.random().toString(36).slice(2) + '00000000').slice(0, 8), type: type, place: cleanStr(m.place, 100) };
        if (type === 'fixed') {
            var t = toMinutes(m.time);
            if (t === null || t >= 1440) throw new Error('שעה לא תקינה במניין קבוע: ' + (m.time || '(ריק)'));
            out.time = formatTime(t);
        }
        if (type === 'text') out.text = cleanStr(m.text, 60);
        if (type === 'zman') {
            out.base = BASES[m.base] ? m.base : 'sunset';
            out.offset = cleanInt(m.offset, -600, 600, 0);
            out.round = ROUNDS.indexOf(m.round) >= 0 ? m.round : 'none';
            out.step = cleanInt(m.step, 1, 60, 5);
            out.weekDay = m.weekDay === 'latest' ? 'latest' : 'earliest';
        }
        return out;
    }

    function normalizeConfig(input) {
        input = input || {};
        var cfg = defaultConfig();
        Object.keys(PAGES).forEach(function (pageId) {
            cfg.enabled[pageId] = !!(input.enabled && input.enabled[pageId]);
            var inPage = (input.pages && input.pages[pageId]) || {};
            PAGES[pageId].tables.forEach(function (table) {
                var list = Array.isArray(inPage[table.id]) ? inPage[table.id] : [];
                if (list.length > 50) throw new Error('יותר מדי מניינים בטבלה ' + table.title);
                cfg.pages[pageId][table.id] = list.map(normalizeMinyan);
            });
        });
        var w = input.weekly || {};
        Object.keys(DEFAULT_WEEKLY).forEach(function (k) {
            cfg.weekly[k] = cleanInt(w[k], k === 'step' ? 1 : 0, 120, DEFAULT_WEEKLY[k]);
        });
        var ov = input.overrides || {};
        Object.keys(ov).forEach(function (ws) {
            if (!/^\d{4}-\d{2}-\d{2}$/.test(ws)) return;
            var entries = {};
            Object.keys(ov[ws] || {}).forEach(function (id) {
                var t = toMinutes(ov[ws][id]);
                if (t !== null && t < 1440) entries[id] = formatTime(t);
            });
            if (Object.keys(entries).length) cfg.overrides[ws] = entries;
        });
        return cfg;
    }

    // לדפי התפילה בדפדפן: טוען את ההגדרות ואת הלוח, ומחזיר את השבוע הנוכחי - או null אם
    // החישוב האוטומטי לא הופעל לדף הזה (ואז הדף ממשיך לטעון מגיליונות גוגל כמו קודם)
    function loadCurrentWeek(pageId) {
        return Promise.all([
            fetch('/.netlify/functions/get-prayer-config').then(function (r) { return r.json(); }),
            fetch('assets/zmanim.json?v=5787').then(function (r) { return r.json(); })
        ]).then(function (res) {
            var config = res[0] && res[0].config;
            if (!config || !config.enabled || !config.enabled[pageId]) return null;
            var weekStart = weekStartFor(todayInIsrael(), pageId);
            if (!res[1].days[weekStart]) return null; // מחוץ לטווח הלוח
            return computeWeek(config, res[1], pageId, weekStart);
        });
    }

    return {
        loadCurrentWeek: loadCurrentWeek,
        PAGES: PAGES,
        BASES: BASES,
        DEFAULT_WEEKLY: DEFAULT_WEEKLY,
        defaultConfig: defaultConfig,
        normalizeConfig: normalizeConfig,
        toMinutes: toMinutes,
        formatTime: formatTime,
        addDays: addDays,
        todayInIsrael: todayInIsrael,
        weekStartFor: weekStartFor,
        computeWeek: computeWeek,
        computeYear: computeYear
    };
});
