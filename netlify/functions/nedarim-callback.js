// מקבלת את עדכון ה-CallBack השרתי מנדרים פלוס בכל פעם שנפתחת הוראת קבע חדשה
// (או מתבצעת עסקה רגילה, אם בעתיד יתווסף גם דף תרומה חד-פעמית).
// זהו "מקור האמת" האמיתי היחיד - לא מה שהדפדפן של הלקוח "מספר" לנו -
// כי הודעת ה-TransactionResponse שהאייפרם שולח לדפדפן ניתנת לזיוף ע"י גולש עוין.
//
// לפי תיעוד נדרים פלוס (סעיף "אימות מקור העדכון"), העדכונים תמיד מגיעים
// מאחת משתי כתובות ה-IP הבאות בלבד:
const NEDARIM_IPS = ['18.196.146.117', '18.194.219.73'];

function getSourceIp(event) {
  // נטליפיי מעביר את שרשרת ה-IP האמיתית ב-x-nf-client-connection-ip,
  // ובמידה וזה חסר - fallback לכותרת הסטנדרטית x-forwarded-for
  const headers = event.headers || {};
  const direct = headers['x-nf-client-connection-ip'];
  if (direct) return direct.trim();

  const forwarded = headers['x-forwarded-for'];
  if (forwarded) return forwarded.split(',')[0].trim();

  return null;
}

async function sendGabayNotification({ subject, htmlBody }) {
  const apiKey = process.env.SENDGRID_API_KEY;
  if (!apiKey) return; // אם המפתח לא מוגדר, לא נכשיל את כל הבקשה - רק נדלג על שליחת המייל

  try {
    await fetch('https://api.resend.com/emails', {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${apiKey}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        from: 'בית הכנסת מוהליבר <gabay@moaliver.org.il>',
        to: 'b0799186827@gmail.com',
        subject,
        html: htmlBody
      })
    });
  } catch (e) {
    console.error('[nedarim-callback] כשל בשליחת מייל התראה לגבאי:', e.message);
  }
}

exports.handler = async function (event, context) {
  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, body: 'Method Not Allowed' };
  }

  const sourceIp = getSourceIp(event);
  const isFromNedarim = sourceIp && NEDARIM_IPS.includes(sourceIp);

  if (!isFromNedarim) {
    // לא חוסמים לגמרי - רק מתעדים ומתריעים, כי לפעמים כתובות ה-IP עלולות
    // להתעדכן מצד נדרים פלוס. אבל אנחנו לא סומכים על תוכן הבקשה במקרה הזה.
    console.warn(`[nedarim-callback] התקבלה קריאה מכתובת IP לא מוכרת: ${sourceIp}`);
    await sendGabayNotification({
      subject: '⚠️ התראת אבטחה - callback מכתובת IP לא מוכרת',
      htmlBody: `<div dir="rtl">התקבלה קריאה ל-nedarim-callback מכתובת IP שאינה ברשימת הכתובות הידועות של נדרים פלוס: <b>${sourceIp || 'לא ידועה'}</b>.<br>אם זו לא טעות, כדאי לבדוק מול נדרים פלוס האם נוספה כתובת חדשה.</div>`
    });
    return { statusCode: 200, body: 'ignored' };
  }

  try {
    const data = JSON.parse(event.body || '{}');

    // הבחנה בין הקמת הוראת קבע חדשה (יש KevaId) לבין עסקה רגילה (יש TransactionId)
    const isNewKeva = !!data.KevaId;

    const rows = Object.entries(data)
      .map(([key, value]) => `<tr><td style="padding:4px 8px;font-weight:bold;color:#1a365d;">${key}</td><td style="padding:4px 8px;">${value}</td></tr>`)
      .join('');

    const subject = isNewKeva
      ? `✅ הוראת קבע חדשה נפתחה - ${data.ClientName || ''}`
      : `✅ תרומה חדשה התקבלה - ${data.ClientName || ''}`;

    const htmlBody = `
      <div dir="rtl" style="font-family: Arial, sans-serif;">
        <h2 style="color:#1a365d;">${subject}</h2>
        <p>אישור שרתי (callback) התקבל ואומת מול נדרים פלוס:</p>
        <table style="border-collapse:collapse;">${rows}</table>
      </div>
    `;

    await sendGabayNotification({ subject, htmlBody });

    return { statusCode: 200, body: 'OK' };
  } catch (error) {
    console.error('[nedarim-callback] שגיאה בעיבוד הקאלבק:', error.message);
    return { statusCode: 500, body: 'error' };
  }
};
