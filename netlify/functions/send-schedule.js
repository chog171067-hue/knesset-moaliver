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

exports.handler = async (event, context) => {
    const headers = {
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Headers': 'Content-Type',
        'Access-Control-Allow-Methods': 'POST, OPTIONS'
    };

    if (event.httpMethod === 'OPTIONS') return { statusCode: 200, headers };
    if (event.httpMethod !== 'POST') return { statusCode: 405, headers, body: 'Method Not Allowed' };

    try {
        const { email } = JSON.parse(event.body);
        if (!email) return { statusCode: 400, headers, body: 'כתובת מייל חסרה' };

        const apiKey = process.env.SENDGRID_API_KEY;

        const links = {
            shacharitChol: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=380588342&single=true&output=csv',
            minchaChol: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=937935590&single=true&output=csv',
            maarivChol: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=879735471&single=true&output=csv',
            minchaErvShabbat: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=1147316138&single=true&output=csv',
            maarivLeilShabbat: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=932518422&single=true&output=csv',
            shacharitShabbat: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=1917318083&single=true&output=csv',
            minchaShabbat: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=81670794&single=true&output=csv',
            maarivMotzeiShabbat: 'https://docs.google.com/spreadsheets/d/e/2PACX-1vTmA3Y2N1hboh3wdH5wYGm35-pdS_z6MHoCCz6QOYYzSvk4bGPYnaMvgqAVna6v738HGEmOdHGHrH98/pub?gid=1569741220&single=true&output=csv'
        };

        const [t1, t2, t3, t4, t5, t6, t7, t8] = await Promise.all([
            fetchTableAsHtml(links.shacharitChol, 'שחרית - ימות החול'),
            fetchTableAsHtml(links.minchaChol, 'מנחה - ימות החול'),
            fetchTableAsHtml(links.maarivChol, 'ערבית - ימות החול'),
            fetchTableAsHtml(links.minchaErvShabbat, 'מנחה - ערב שבת'),
            fetchTableAsHtml(links.maarivLeilShabbat, 'ערבית - ליל שבת'),
            fetchTableAsHtml(links.shacharitShabbat, 'שחרית - שבת'),
            fetchTableAsHtml(links.minchaShabbat, 'מנחה - שבת'),
            fetchTableAsHtml(links.maarivMotzeiShabbat, 'ערבית - מוצאי שבת')
        ]);

        // חיבור רק של טבלאות שיש בהן תוכן
        const tablesHtml = [t1, t2, t3, t4, t5, t6, t7, t8].filter(t => t !== '').join('');

        const emailHtml = `
            <div style="direction: rtl; text-align: right; font-family: 'Segoe UI', sans-serif; padding: 15px; background-color: #f5f5dc; color: #333;">
                <h2 style="color: #800020; text-align:center; margin-top:5px; margin-bottom:15px; font-size:20px;">זמני התפילות - בית הכנסת מוהליבר</h2>
                <p style="margin-bottom:15px; font-size:14px;">שלום וברכה,<br>להלן לוח זמני התפילות המלא כפי שמעודכן כעת בגיליון בית הכנסת:</p>
                
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