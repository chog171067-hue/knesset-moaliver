// דף ניהול - טאב "הודעות לגבאי": שליחת תשובת הגבאי למשתמש ספציפי, וגם
// יזום שיחה חדשה עם משתמש שעדיין לא כתב (userName/userEmail/userTz
// נשלחים במקרה הזה כדי ליצור את רשומת האינדקס עבורו).
// בנוסף לשמירת ההודעה בשרשור, נשלחת התראה במייל למשתמש עם תוכן ההודעה
// וכפתור לחזרה ישירות לצ'אט באזור האישי כדי להמשיך את השיחה.
const { getAdminStore } = require('./lib/blobs-store');
const { requireAdmin } = require('./lib/admin-auth');

const THREADS_INDEX_KEY = 'gabai-threads-index.json';

function threadKey(userId) {
  return `gabai-thread-${userId}.json`;
}

async function notifyUser(userEmail, text) {
  try {
    const apiKey = process.env.SENDGRID_API_KEY;
    if (!apiKey || !userEmail) return;

    await fetch('https://api.resend.com/emails', {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${apiKey}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({
        from: 'בית הכנסת מוהליבר <gabay@moaliver.org.il>',
        to: userEmail,
        subject: 'הודעה חדשה מהגבאי - בית הכנסת מוהליבר',
        html: `
          <div dir="rtl" style="font-family:'Segoe UI',sans-serif; padding:15px; text-align:right;">
            <p>שלום,</p>
            <p>התקבלה הודעה חדשה מהגבאי באזור האישי שלך באתר בית הכנסת:</p>
            <p style="background:#f7fafc; padding:12px; border-radius:8px;">${text}</p>
            <p style="text-align:center; margin-top:20px;">
              <a href="https://moaliver.org.il/personal.html?tab=gabai-chat" style="display: inline-block; padding: 10px 20px; background-color: #000080; color: white; text-decoration: none; border-radius: 5px; font-weight: bold; font-size:14px;">מעבר לשיחה באתר</a>
            </p>
          </div>
        `
      })
    });
  } catch (e) {
    console.warn('[admin-gabai-reply] כשל בשליחת התראה למשתמש:', e.message);
  }
}

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireAdmin(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  try {
    const { userId, text, userName, userEmail, userTz } = JSON.parse(event.body || '{}');
    const cleanText = String(text || '').trim();

    if (!userId || !cleanText) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסרים פרטים' }) };
    }
    if (cleanText.length > 2000) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'ההודעה ארוכה מדי (מקסימום 2000 תווים)' }) };
    }

    const store = getAdminStore(event);
    const index = (await store.get(THREADS_INDEX_KEY, { type: 'json' })) || [];
    let entry = index.find(t => t.userId === userId);

    if (!entry && !userEmail) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסרים פרטי המשתמש לפתיחת שיחה חדשה' }) };
    }

    const messages = (await store.get(threadKey(userId), { type: 'json' })) || [];
    messages.push({ from: 'admin', text: cleanText, timestamp: new Date().toISOString() });
    await store.setJSON(threadKey(userId), messages);

    if (entry) {
      entry.lastMessageAt = new Date().toISOString();
      entry.lastMessageFrom = 'admin';
      entry.lastMessagePreview = cleanText.slice(0, 100);
      entry.unreadByUser = true;
      entry.unreadByAdmin = false;
    } else {
      entry = {
        userId,
        userName: userName || userEmail,
        userEmail,
        userTz: userTz || '',
        lastMessageAt: new Date().toISOString(),
        lastMessageFrom: 'admin',
        lastMessagePreview: cleanText.slice(0, 100),
        unreadByAdmin: false,
        unreadByUser: true
      };
      index.push(entry);
    }
    await store.setJSON(THREADS_INDEX_KEY, index);

    await notifyUser(entry.userEmail, cleanText);

    return { statusCode: 200, headers, body: JSON.stringify({ success: true }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
