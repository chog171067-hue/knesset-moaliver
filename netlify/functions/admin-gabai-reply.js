// דף ניהול - טאב "הודעות לגבאי": שליחת תשובת הגבאי למשתמש ספציפי.
const { getAdminStore } = require('./lib/blobs-store');
const { requireAdmin } = require('./lib/admin-auth');

const THREADS_INDEX_KEY = 'gabai-threads-index.json';

function threadKey(userId) {
  return `gabai-thread-${userId}.json`;
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
    const { userId, text } = JSON.parse(event.body || '{}');
    const cleanText = String(text || '').trim();

    if (!userId || !cleanText) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסרים פרטים' }) };
    }
    if (cleanText.length > 2000) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'ההודעה ארוכה מדי (מקסימום 2000 תווים)' }) };
    }

    const store = getAdminStore(event);
    const messages = (await store.get(threadKey(userId), { type: 'json' })) || [];
    messages.push({ from: 'admin', text: cleanText, timestamp: new Date().toISOString() });
    await store.setJSON(threadKey(userId), messages);

    const index = (await store.get(THREADS_INDEX_KEY, { type: 'json' })) || [];
    const entry = index.find(t => t.userId === userId);
    if (entry) {
      entry.lastMessageAt = new Date().toISOString();
      entry.lastMessageFrom = 'admin';
      entry.lastMessagePreview = cleanText.slice(0, 100);
      entry.unreadByUser = true;
      entry.unreadByAdmin = false;
      await store.setJSON(THREADS_INDEX_KEY, index);
    }

    return { statusCode: 200, headers, body: JSON.stringify({ success: true }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
