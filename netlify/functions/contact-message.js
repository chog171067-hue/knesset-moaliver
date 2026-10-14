// טופס "צור קשר" שבסרגל הניווט העליון (assets/header.js): שליחת הודעה לגבאים במייל.
// פתוח לכל גולש (לא דורש התחברות), ולכן כל הקלט מנוקה ומוגבל באורכו לפני שהוא נכנס למייל.
// השליחה דרך Resend, באותו מפתח ובאותה כתובת שולח כמו התראות "הודעות לגבאי" (gabai-send-message.js).
const GABAI_EMAIL = 'b0799186827@gmail.com';

const LIMITS = { name: 100, phone: 30, email: 200, message: 3000 };

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function isValidEmail(email) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
}

exports.handler = async function (event) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: JSON.stringify({ success: false, error: 'Method Not Allowed' }) };
  }

  let body;
  try {
    body = JSON.parse(event.body || '{}');
  } catch (e) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'בקשה לא תקינה' }) };
  }

  // שדה "מלכודת" נסתר בטופס - גולש אמיתי לא רואה ולא ממלא אותו, בוטים כן.
  // מחזירים הצלחה כדי לא לרמוז לבוט שנחסם.
  if (body.website) {
    return { statusCode: 200, headers, body: JSON.stringify({ success: true }) };
  }

  const name = String(body.name || '').trim();
  const phone = String(body.phone || '').trim();
  const email = String(body.email || '').trim();
  const message = String(body.message || '').trim();

  if (!name) return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'יש להזין שם' }) };
  if (!email || !isValidEmail(email)) return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'יש להזין כתובת מייל תקינה' }) };
  if (!message) return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'יש להזין את תוכן ההודעה' }) };
  if (name.length > LIMITS.name || phone.length > LIMITS.phone || email.length > LIMITS.email || message.length > LIMITS.message) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'אחד השדות ארוך מדי' }) };
  }

  const apiKey = process.env.SENDGRID_API_KEY;
  if (!apiKey) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שירות המייל אינו מוגדר' }) };
  }

  try {
    const res = await fetch('https://api.resend.com/emails', {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${apiKey}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({
        from: 'בית הכנסת מוהליבר <gabay@moaliver.org.il>',
        to: GABAI_EMAIL,
        reply_to: email,
        subject: `פנייה חדשה מהאתר - ${name.replace(/[\r\n]+/g, ' ')}`,
        html: `
          <div dir="rtl" style="font-family:'Segoe UI',sans-serif; padding:15px; text-align:right;">
            <h2 style="color:#1a365d; margin-top:0;">פנייה חדשה דרך "צור קשר" באתר</h2>
            <p><b>שם:</b> ${escapeHtml(name)}</p>
            <p><b>טלפון:</b> ${phone ? escapeHtml(phone) : '-'}</p>
            <p><b>מייל:</b> ${escapeHtml(email)}</p>
            <p><b>הודעה:</b></p>
            <p style="background:#f7fafc; padding:12px; border-radius:8px; white-space:pre-wrap;">${escapeHtml(message)}</p>
            <p style="color:#718096; font-size:13px;">אפשר להשיב ישירות למייל זה - התשובה תגיע לפונה.</p>
          </div>
        `
      })
    });

    if (!res.ok) {
      const errText = await res.text();
      console.warn('[contact-message] Resend החזיר שגיאה:', res.status, errText);
      return { statusCode: 502, headers, body: JSON.stringify({ success: false, error: 'שליחת ההודעה נכשלה, נסו שוב מאוחר יותר' }) };
    }

    return { statusCode: 200, headers, body: JSON.stringify({ success: true }) };
  } catch (error) {
    console.warn('[contact-message] כשל בשליחה:', error.message);
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שליחת ההודעה נכשלה, נסו שוב מאוחר יותר' }) };
  }
};
