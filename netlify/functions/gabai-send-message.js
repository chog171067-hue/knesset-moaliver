// אזור אישי - טאב "הודעות לגבאי": שליחת הודעה חדשה מהמשתמש המחובר לגבאי.
// כל משתמש מאומת (עם ת.ז מקושרת) יכול לשלוח; ההודעה נשמרת בשרשור האישי שלו,
// מקושרת לפי מזהה המשתמש (context.clientContext.user.sub), ונשלחת התראה
// במייל לגבאי כדי שלא יצטרך לבדוק את דף הניהול כל הזמן.
const { getAdminStore } = require('./lib/blobs-store');

const GABAI_EMAIL = 'b0799186827@gmail.com';
const THREADS_INDEX_KEY = 'gabai-threads-index.json';

function threadKey(userId) {
  return `gabai-thread-${userId}.json`;
}

async function notifyGabai(userName, userTz, userEmail, text) {
  try {
    const apiKey = process.env.SENDGRID_API_KEY;
    if (!apiKey) return;

    await fetch('https://api.resend.com/emails', {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${apiKey}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({
        from: 'בית הכנסת מוהליבר <gabay@moaliver.org.il>',
        to: GABAI_EMAIL,
        reply_to: userEmail || GABAI_EMAIL,
        subject: `הודעה חדשה מ${userName || userEmail} באזור האישי`,
        html: `
          <div dir="rtl" style="font-family:'Segoe UI',sans-serif; padding:15px; text-align:right;">
            <p><b>שם:</b> ${userName || '-'}</p>
            <p><b>ת.ז:</b> ${userTz || '-'}</p>
            <p><b>מייל:</b> ${userEmail || '-'}</p>
            <p><b>הודעה:</b></p>
            <p style="background:#f7fafc; padding:12px; border-radius:8px;">${text}</p>
            <p><a href="https://moaliver.org.il/admin.html">מענה מתוך דף הניהול</a></p>
          </div>
        `
      })
    });
  } catch (e) {
    console.warn('[gabai-send-message] כשל בשליחת התראה לגבאי:', e.message);
  }
}

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const authedUser = context.clientContext && context.clientContext.user;
  const authedTz = authedUser && authedUser.app_metadata && String(authedUser.app_metadata.verified_tz || '').trim();
  if (!authedTz) {
    return { statusCode: 401, headers, body: JSON.stringify({ success: false, error: 'עליך להתחבר עם חשבון מאומת' }) };
  }

  try {
    const { text } = JSON.parse(event.body || '{}');
    const cleanText = String(text || '').trim();
    if (!cleanText) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'ההודעה ריקה' }) };
    }
    if (cleanText.length > 2000) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'ההודעה ארוכה מדי (מקסימום 2000 תווים)' }) };
    }

    const store = getAdminStore(event);
    const userId = authedUser.sub || authedUser.id;
    const um = authedUser.user_metadata || {};
    const userName = um.full_name || um.name || authedUser.email;

    const messages = (await store.get(threadKey(userId), { type: 'json' })) || [];
    messages.push({ from: 'user', text: cleanText, timestamp: new Date().toISOString() });
    await store.setJSON(threadKey(userId), messages);

    const index = (await store.get(THREADS_INDEX_KEY, { type: 'json' })) || [];
    const summary = {
      userId,
      userName,
      userEmail: authedUser.email,
      userTz: authedTz,
      lastMessageAt: new Date().toISOString(),
      lastMessageFrom: 'user',
      lastMessagePreview: cleanText.slice(0, 100),
      unreadByAdmin: true,
      unreadByUser: false
    };
    const existing = index.find(t => t.userId === userId);
    if (existing) {
      Object.assign(existing, summary);
    } else {
      index.push(summary);
    }
    await store.setJSON(THREADS_INDEX_KEY, index);

    await notifyGabai(userName, authedTz, authedUser.email, cleanText);

    return { statusCode: 200, headers, body: JSON.stringify({ success: true }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
