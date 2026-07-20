// תצורת כל דפי התפילה: לכל דף - התוויות והקישורים (CSV) של הטבלאות שבו.
// חשוב: מפתחות האובייקט (shabbat, yemothachol, tishabav, beinhazmanim) חייבים להיות
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
        label: 'ימות החול',
        tables: [
            { title: 'שחרית - ימות החול', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=380588342&single=true&output=csv' },
            { title: 'מנחה - ימות החול', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=937935590&single=true&output=csv' },
            { title: 'ערבית - ימות החול', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=879735471&single=true&output=csv' }
        ]
    },
    tishabav: {
        label: 'תשעה באב',
        tables: [
            { title: 'מנחה ערב תשעה באב', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vSqsR9YPqI3e8NPLqqbWZ0j_cK3Vbql9DwtSqrJZhpc_lDlNWvGSR-6L4mK8P-oXgNA3kiMi-Jp5s5J/pub?gid=369068471&single=true&output=csv' },
            { title: 'ערבית ליל תשעה באב', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vSqsR9YPqI3e8NPLqqbWZ0j_cK3Vbql9DwtSqrJZhpc_lDlNWvGSR-6L4mK8P-oXgNA3kiMi-Jp5s5J/pub?gid=1208663626&single=true&output=csv' },
            { title: 'שחרית - תשעה באב', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vSqsR9YPqI3e8NPLqqbWZ0j_cK3Vbql9DwtSqrJZhpc_lDlNWvGSR-6L4mK8P-oXgNA3kiMi-Jp5s5J/pub?gid=271049172&single=true&output=csv' },
            { title: 'מנחה - תשעה באב', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vSqsR9YPqI3e8NPLqqbWZ0j_cK3Vbql9DwtSqrJZhpc_lDlNWvGSR-6L4mK8P-oXgNA3kiMi-Jp5s5J/pub?gid=517647281&single=true&output=csv' },
            { title: 'ערבית מוצאי תשעה באב', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vSqsR9YPqI3e8NPLqqbWZ0j_cK3Vbql9DwtSqrJZhpc_lDlNWvGSR-6L4mK8P-oXgNA3kiMi-Jp5s5J/pub?gid=998787293&single=true&output=csv' }
        ],
        singleColumnTables: [
            { title: 'קידוש לבנה', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vSqsR9YPqI3e8NPLqqbWZ0j_cK3Vbql9DwtSqrJZhpc_lDlNWvGSR-6L4mK8P-oXgNA3kiMi-Jp5s5J/pub?gid=160509364&single=true&output=csv' }
        ]
    },
    beinhazmanim: {
        label: 'בין הזמנים',
        tables: [
            { title: 'שחרית - בין הזמנים', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vSqsR9YPqI3e8NPLqqbWZ0j_cK3Vbql9DwtSqrJZhpc_lDlNWvGSR-6L4mK8P-oXgNA3kiMi-Jp5s5J/pub?gid=0&single=true&output=csv' },
            { title: 'מנחה - בין הזמנים', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vSqsR9YPqI3e8NPLqqbWZ0j_cK3Vbql9DwtSqrJZhpc_lDlNWvGSR-6L4mK8P-oXgNA3kiMi-Jp5s5J/pub?gid=1976712448&single=true&output=csv' },
            { title: 'ערבית - בין הזמנים', url: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vSqsR9YPqI3e8NPLqqbWZ0j_cK3Vbql9DwtSqrJZhpc_lDlNWvGSR-6L4mK8P-oXgNA3kiMi-Jp5s5J/pub?gid=905054164&single=true&output=csv' }
        ]
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

// בונה קטע HTML שלם עבור דף תפילה מסוים (כותרת ראשית + כל הטבלאות שלו),
// ומחזיר מחרוזת ריקה אם לא נמצא אף שורת נתונים אמיתית באף אחת מהטבלאות שלו.
async function buildPageSection(pageId) {
    const config = PAGE_CONFIG[pageId];
    if (!config) return '';

    const tableTasks = (config.tables || []).map(t => fetchTableAsHtml(t.url, t.title));
    const singleColTasks = (config.singleColumnTables || []).map(t => fetchSingleColumnTableAsHtml(t.url, t.title));

    const results = await Promise.all([...tableTasks, ...singleColTasks]);
    const combined = results.filter(html => html !== '').join('');

    if (!combined) return '';

    return `
        <div style="margin-bottom: 20px;">
            <h2 style="color:#000080; text-align:center; border-bottom:2px solid #800020; padding-bottom:6px; font-size:17px; margin-bottom:10px;">${config.label}</h2>
            ${combined}
        </div>
    `;
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
            return { statusCode: 200, headers, body: JSON.stringify({ message: 'המייל נשלח בהצלחה!' }) };
        } else {
            const errorData = await response.text();
            return { statusCode: response.status, headers, body: `שגיאה: ${errorData}` };
        }

    } catch (error) {
        return { statusCode: 500, headers, body: `שגיאה פנימית: ${error.message}` };
    }
};
