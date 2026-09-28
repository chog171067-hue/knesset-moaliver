const { getAdminStore } = require('./lib/blobs-store');
// אותו מפענח שהדף בדפדפן משתמש בו, כדי שהמייל והאתר יציגו בדיוק אותם זמנים
const { parsePrayerSections } = require('../../assets/prayer-sections.js');

// תצורת כל דפי התפילה: לכל דף - התוויות והקישורים (CSV) של הטבלאות שבו.
// חשוב: מפתחות האובייקט (shabbat, yemothachol) חייבים להיות
// זהים לשמות קבצי ה-HTML (ללא הסיומת .html) כפי שמוגדרים ב-assets/header.js,
// כי אלו בדיוק הערכים שהצ'קבוקסים בעמודים שולחים לכאן.
const PAGE_CONFIG = {
    shabbat: {
        label: 'שבתות',
        tables: [
            { title: 'מנחה - ערב שבת', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=1147316138&single=true&output=csv' },
            { title: 'ערבית - ליל שבת', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=932518422&single=true&output=csv' },
            { title: 'שחרית - שבת', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=1917318083&single=true&output=csv' },
            { title: 'מנחה - שבת', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=81670794&single=true&output=csv' },
            { title: 'ערבית - מוצאי שבת', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=1569741220&single=true&output=csv' }
        ]
    },
    yemothachol: {
        label: 'ימות החול - בין הזמנים תשרי',
        tables: [
            { title: 'שחרית', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=380588342&single=true&output=csv' },
            { title: 'מנחה', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=937935590&single=true&output=csv' },
            { title: 'ערבית', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=879735471&single=true&output=csv' }
        ]
    },
    // מפתח זה הוא מזהה אפשרות השליחה (id) שמוגדר ב-mailSchedules של הדף ב-assets/header.js
    'sukkot-yomtov-rishon': {
        label: 'חג הסוכות - יו"ט ראשון',
        flexibleTable: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vShhYyWWzh47GjKvj0xofb_Hd6CCLoJMFr9S5LnGtnTDMJnuskDTq63lxXl1zQ-0wi0ASMVDaOVGK69/pub?gid=1773662680&single=true&output=csv'
    },
    // לשונית אחת שבה שחרית, מנחה וערבית יחד - מפוענחת ב-assets/prayer-sections.js
    // (אותו קישור מוגדר גם ב-sukkot.html לטעינת הזמנים בדף עצמו)
    'sukkot-chol-hamoed': {
        label: 'חג הסוכות - חול המועד',
        prayerSections: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vShhYyWWzh47GjKvj0xofb_Hd6CCLoJMFr9S5LnGtnTDMJnuskDTq63lxXl1zQ-0wi0ASMVDaOVGK69/pub?gid=188569280&single=true&output=csv'
    },
    'sukkot-hoshana-raba': {
        label: 'חג הסוכות - הושענא רבה',
        exclude: ['ערבית'],
        prayerSections: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vShhYyWWzh47GjKvj0xofb_Hd6CCLoJMFr9S5LnGtnTDMJnuskDTq63lxXl1zQ-0wi0ASMVDaOVGK69/pub?gid=1801812322&single=true&output=csv'
    },
    // בדף עצמו מוצגת תמונה; למייל נשלחים הנתונים מהגיליון (כמו יו"ט ראשון)
    'sukkot-simchat-torah': {
        label: 'חג הסוכות - שמחת תורה',
        flexibleTable: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vShhYyWWzh47GjKvj0xofb_Hd6CCLoJMFr9S5LnGtnTDMJnuskDTq63lxXl1zQ-0wi0ASMVDaOVGK69/pub?gid=1362226132&single=true&output=csv'
    }
};

async function fetchTableAsHtml(url, title) {
    try {
        const res = await fetch(url);
        if (!res.ok) return ''; // אם טבלה מסוימת ריקה או נכשלה, נדלג עליה בשקט כדי לא לנפח
        const text = await res.text();
        const lines = text.split('\n').map(line => line.split(','));

        let hasRows = false;
        let tableRowsHtml = '';

        lines.forEach((cols, idx) => {
            if (idx === 0 || cols.length < 2) return;
            const time = cols[0]?.trim();
            const loc = cols[1]?.trim();

            // סינון קשוח: מכניס רק שורות שיש בהן תוכן אמיתי ושאינן כותרות ריקות של גוגל
            if (time && loc && time.toLowerCase() !== 'time' && time !== 'שעה' && loc.toLowerCase() !== 'location' && loc !== 'מיקום') {
                hasRows = true;
                tableRowsHtml += `<tr><td style="padding:5px; border-bottom:1px solid #eee; font-weight:bold; color:#000080;">${time}</td><td style="padding:5px; border-bottom:1px solid #eee;">${loc}</td></tr>`;
            }
        });

        // אם אין שורות אמיתיות בטבלה הזו, לא מייצרים עבורה קוד HTML בכלל (חוסך המון נפח!)
        if (!hasRows) return '';

        let html = `<div style="margin-bottom: 15px; background: white; padding: 12px; border: 1px solid #000080; border-radius: 8px;">`;
        html += `<h3 style="color: #800020; margin-top:0; margin-bottom:8px; border-bottom: 2px solid #000080; padding-bottom: 3px; font-size:15px;">${title}</h3>`;
        html += `<table style="width:100%; border-collapse:collapse; font-size:13px; text-align:center;" dir="rtl">`;
        html += `<thead><tr style="color:#000080;"><th style="padding:4px; border-bottom:1px solid #ddd;">שעה</th><th style="padding:4px; border-bottom:1px solid #ddd;">מיקום</th></tr></thead>`;
        html += `<tbody>${tableRowsHtml}</tbody></table></div>`;

        return html;
    } catch (e) {
        return '';
    }
}

// גרסה לטבלת עמודה אחת בלבד (שעה בלבד, בלי מיקום) - עבור קידוש לבנה
async function fetchSingleColumnTableAsHtml(url, title) {
    try {
        const res = await fetch(url);
        if (!res.ok) return '';
        const text = await res.text();
        const lines = text.split('\n').map(line => line.split(','));

        let hasRows = false;
        let tableRowsHtml = '';

        lines.forEach((cols, idx) => {
            if (idx === 0 || cols.length < 1) return;
            const time = cols[0]?.trim();

            if (time && time.toLowerCase() !== 'time' && time !== 'שעה') {
                hasRows = true;
                tableRowsHtml += `<tr><td style="padding:5px; border-bottom:1px solid #eee; font-weight:bold; color:#000080;">${time}</td></tr>`;
            }
        });

        if (!hasRows) return '';

        let html = `<div style="margin-bottom: 15px; background: white; padding: 12px; border: 1px solid #000080; border-radius: 8px;">`;
        html += `<h3 style="color: #800020; margin-top:0; margin-bottom:8px; border-bottom: 2px solid #000080; padding-bottom: 3px; font-size:15px;">${title}</h3>`;
        html += `<table style="width:100%; border-collapse:collapse; font-size:13px; text-align:center;" dir="rtl">`;
        html += `<thead><tr style="color:#000080;"><th style="padding:4px; border-bottom:1px solid #ddd;">שעה</th></tr></thead>`;
        html += `<tbody>${tableRowsHtml}</tbody></table></div>`;

        return html;
    } catch (e) {
        return '';
    }
}

// מפענח CSV מלא (כולל תאים במרכאות שמכילים פסיקים או מרכאות כפולות)
function parseCsv(text) {
    const rows = [];
    let row = [], cell = '', inQuotes = false;
    for (let i = 0; i < text.length; i++) {
        const ch = text[i];
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
    return rows.map(r => r.map(c => c.trim()));
}

function escapeHtml(s) {
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

// "מכיל שעה" ולא "הוא שעה בדיוק", כדי שתאים כמו "18:25 בדיוק!" עדיין ייחשבו נתונים ולא כותרת
function isTimeLike(s) {
    return /\d{1,2}:\d{2}/.test(s || '');
}

// קוצץ שניות משעה מובילה ("18:25:00" -> "18:25") ושומר על טקסט חופשי שאחריה באותו תא
function normalizeTime(s) {
    const m = (s || '').match(/^(\d{1,2}:\d{2})(:\d{2})?(.*)$/);
    return m ? (m[1] + (m[3] || '')).trim() : (s || '');
}

const HEADER_WORDS = ['שעה', 'מקום', 'מיקום', 'time', 'location'];

// מפענח גמיש לגיליונות חגים, שהמבנה שלהם משתנה מחג לחג:
// - שורה עם תא יחיד שאינו שעה = כותרת שפותחת קטע (טבלה) חדש במייל
// - שורת כותרות עמודות (שעה/מקום וכו') משמשת ככותרת הטבלאות ולא כנתונים
// - כל שורה אחרת היא שורת נתונים; תא ריק בה מקבל את הערך שמעליו באותו קטע
//   (כך נראה תא ממוזג בגיליון כשמייצאים אותו ל-CSV)
// מנותק מ-fetch כדי שיהיה ניתן לבדוק בלי רשת
function buildFlexibleTableHtml(csvText) {
    const rows = parseCsv(csvText).filter(cols => cols.some(c => c !== ''));

    let columnHeaders = null;
    const blocks = [];

    rows.forEach(cols => {
        while (cols.length && cols[cols.length - 1] === '') cols.pop();
        const filled = cols.filter(c => c !== '');

        if (!filled.some(isTimeLike) && filled.some(c => HEADER_WORDS.includes(c.toLowerCase()))) {
            if (!columnHeaders) columnHeaders = cols;
            return;
        }

        if (filled.length === 1 && !isTimeLike(filled[0])) {
            blocks.push({ title: filled[0], rows: [] });
            return;
        }

        if (blocks.length === 0) blocks.push({ title: '', rows: [] });
        const block = blocks[blocks.length - 1];
        const prev = block.rows[block.rows.length - 1];
        const width = Math.max(cols.length, prev ? prev.length : 0);
        block.rows.push(Array.from({ length: width }, (_, i) => normalizeTime(cols[i] || (prev && prev[i]) || '')));
    });

    return blocks.filter(b => b.rows.length > 0).map(block => {
        const width = Math.max(...block.rows.map(r => r.length));
        const pad = r => Array.from({ length: width }, (_, i) => escapeHtml(r[i] || ''));

        const theadHtml = columnHeaders
            ? `<thead><tr style="color:#000080;">${pad(columnHeaders).map(h => `<th style="padding:4px; border-bottom:1px solid #ddd;">${h}</th>`).join('')}</tr></thead>`
            : '';
        const rowsHtml = block.rows.map(r => '<tr>' + pad(r).map((c, i) =>
            `<td style="padding:5px; border-bottom:1px solid #eee;${i === 0 ? ' font-weight:bold; color:#000080;' : ''}">${c}</td>`).join('') + '</tr>').join('');
        const titleHtml = block.title
            ? `<h3 style="color: #800020; margin-top:0; margin-bottom:8px; border-bottom: 2px solid #000080; padding-bottom: 3px; font-size:15px;">${escapeHtml(block.title)}</h3>`
            : '';

        return `
            <div style="margin-bottom: 15px; background: white; padding: 12px; border: 1px solid #000080; border-radius: 8px;">
                ${titleHtml}
                <table style="width:100%; border-collapse:collapse; font-size:13px; text-align:center;" dir="rtl">
                    ${theadHtml}
                    <tbody>${rowsHtml}</tbody>
                </table>
            </div>
        `;
    }).join('');
}

async function fetchFlexibleTableAsHtml(url) {
    try {
        const res = await fetch(url);
        if (!res.ok) return '';
        return buildFlexibleTableHtml(await res.text());
    } catch (e) {
        return '';
    }
}

// טבלת שעה | מיקום לכל קטע (שחרית, מנחה, ערבית וכותרות נוספות) שיש לו זמנים בגיליון
async function fetchPrayerSectionsAsHtml(url, exclude) {
    try {
        const res = await fetch(url);
        if (!res.ok) return '';
        const sections = parsePrayerSections(await res.text(), { exclude });

        return Object.keys(sections).filter(p => sections[p].length > 0).map(prayer => {
            const rowsHtml = sections[prayer].map(r =>
                `<tr><td style="padding:5px; border-bottom:1px solid #eee; font-weight:bold; color:#000080;">${escapeHtml(r.time)}</td><td style="padding:5px; border-bottom:1px solid #eee;">${escapeHtml(r.place)}</td></tr>`).join('');
            return `
            <div style="margin-bottom: 15px; background: white; padding: 12px; border: 1px solid #000080; border-radius: 8px;">
                <h3 style="color: #800020; margin-top:0; margin-bottom:8px; border-bottom: 2px solid #000080; padding-bottom: 3px; font-size:15px;">${escapeHtml(prayer)}</h3>
                <table style="width:100%; border-collapse:collapse; font-size:13px; text-align:center;" dir="rtl">
                    <thead><tr style="color:#000080;"><th style="padding:4px; border-bottom:1px solid #ddd;">שעה</th><th style="padding:4px; border-bottom:1px solid #ddd;">מיקום</th></tr></thead>
                    <tbody>${rowsHtml}</tbody>
                </table>
            </div>
        `;
        }).join('');
    } catch (e) {
        return '';
    }
}

// בונה קטע HTML שלם עבור דף תפילה מסוים (כותרת ראשית + כל הטבלאות שלו),
// ומחזיר מחרוזת ריקה אם לא נמצא אף שורת נתונים אמיתית באף אחת מהטבלאות שלו.
async function buildPageSection(pageId) {
    const config = PAGE_CONFIG[pageId];
    if (!config) return '';

    const tableTasks = (config.tables || []).map(t => fetchTableAsHtml(t.url, t.title));
    const singleColTasks = (config.singleColumnTables || []).map(t => fetchSingleColumnTableAsHtml(t.url, t.title));

    const flexibleTableTasks = config.flexibleTable ? [fetchFlexibleTableAsHtml(config.flexibleTable)] : [];

    const prayerSectionsTasks = config.prayerSections ? [fetchPrayerSectionsAsHtml(config.prayerSections, config.exclude)] : [];

    const results = await Promise.all([...tableTasks, ...singleColTasks, ...flexibleTableTasks, ...prayerSectionsTasks]);
    const combined = results.filter(html => html !== '').join('');

    if (!combined) return '';

    return `
        <div style="margin-bottom: 20px;">
            <h2 style="color:#000080; text-align:center; border-bottom:2px solid #800020; padding-bottom:6px; font-size:17px; margin-bottom:10px;">${config.label}</h2>
            ${combined}
        </div>
    `;
}

// רישום כל בקשה לשליחת זמנים במייל ליומן ב-Blobs, כדי שדף הניהול יוכל להציג
// אותן - כולל בקשות ממי שאין לו אזור אישי כלל (הבקשה הזו לא דורשת התחברות).
// כשל ברישום לא אמור אף פעם למנוע את שליחת המייל עצמו.
async function logEmailRequest(entry, event) {
  try {
    const store = getAdminStore(event);
    const key = 'email-requests.json';
    const MAX_ENTRIES = 2000;

    const list = (await store.get(key, { type: 'json' })) || [];
    list.push(entry);
    const trimmed = list.length > MAX_ENTRIES ? list.slice(list.length - MAX_ENTRIES) : list;

    await store.setJSON(key, trimmed);
  } catch (e) {
    console.warn('[send-schedule] כשל ברישום הבקשה ליומן:', e.message);
  }
}

exports.handler = async (event, context) => {
    const headers = {
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Headers': 'Content-Type',
        'Access-Control-Allow-Methods': 'POST, OPTIONS'
    };

    if (event.httpMethod === 'OPTIONS') return { statusCode: 200, headers };
    if (event.httpMethod !== 'POST') return { statusCode: 405, headers, body: 'Method Not Allowed' };

    try {
        const { email, pages } = JSON.parse(event.body);
        if (!email) return { statusCode: 400, headers, body: 'כתובת מייל חסרה' };

        if (!Array.isArray(pages) || pages.length === 0) {
            return { statusCode: 400, headers, body: 'יש לבחור לפחות לוח זמנים אחד לשליחה' };
        }

        // מסננים רק מזהי דפים תקינים שיש להם תצורה מוכרת, כדי למנוע קלט שרירותי מהלקוח
        const validPageIds = pages.filter(p => PAGE_CONFIG[p]);
        if (validPageIds.length === 0) {
            return { statusCode: 400, headers, body: 'לא נמצאו לוחות זמנים תקינים לשליחה' };
        }

        const pageLabels = validPageIds.map(p => PAGE_CONFIG[p].label);

        const apiKey = process.env.SENDGRID_API_KEY;

        const sections = await Promise.all(validPageIds.map(buildPageSection));
        const tablesHtml = sections.filter(s => s !== '').join('');

        if (!tablesHtml) {
            return { statusCode: 404, headers, body: 'לא נמצאו זמנים זמינים עבור הלוחות שנבחרו כרגע' };
        }

        const emailHtml = `
            <div style="direction: rtl; text-align: right; font-family: 'Segoe UI', sans-serif; padding: 15px; background-color: #f5f5dc; color: #333;">
                <h2 style="color: #800020; text-align:center; margin-top:5px; margin-bottom:15px; font-size:20px;">זמני התפילות - בית הכנסת מוהליבר</h2>
                <p style="margin-bottom:15px; font-size:14px;">שלום וברכה,<br>להלן לוח זמני התפילות כפי שמעודכן כעת בגיליון בית הכנסת:</p>

                <div style="max-width: 500px; margin: 0 auto;">
                    ${tablesHtml}
                </div>

                <p style="text-align:center; margin-top:20px;">
                    <a href="https://moaliver.org.il" style="display: inline-block; padding: 10px 20px; background-color: #000080; color: white; text-decoration: none; border-radius: 5px; font-weight: bold; font-size:14px;">למעבר לאתר הדינמי לחץ כאן</a>
                </p>
                <hr style="border: none; border-top: 1px solid #ccc; margin: 20px 0;">
                <p style="font-size: 11px; color: #666; text-align:center; margin-bottom:5px;">המייל נשלח אוטומטית לבקשת המשתמש מאתר בית הכנסת.</p>
            </div>
        `;

        const response = await fetch('https://api.resend.com/emails', {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${apiKey}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                from: 'בית הכנסת מוהליבר <gabay@moaliver.org.il>',
                to: email,
                reply_to: 'b0799186827@gmail.com',
                subject: 'זמני תפילות מעודכנים - בית הכנסת מוהליבר',
                html: emailHtml
            })
        });

        if (response.ok) {
            await logEmailRequest({ timestamp: new Date().toISOString(), email, pages: validPageIds, pageLabels, success: true }, event);
            return { statusCode: 200, headers, body: JSON.stringify({ message: 'המייל נשלח בהצלחה!' }) };
        } else {
            const errorData = await response.text();
            await logEmailRequest({ timestamp: new Date().toISOString(), email, pages: validPageIds, pageLabels, success: false, error: errorData }, event);
            return { statusCode: response.status, headers, body: `שגיאה: ${errorData}` };
        }

    } catch (error) {
        return { statusCode: 500, headers, body: `שגיאה פנימית: ${error.message}` };
    }
};
