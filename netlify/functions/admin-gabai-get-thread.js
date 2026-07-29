// דף ניהול - טאב "הודעות לגבאי": שליפת השרשור המלא מול משתמש ספציפי, ומסמן
// אותו כנקרא ע"י המנהל.
const { getAdminStore } = require('./lib/blobs-store');
const { requireAdmin } = require('./lib/admin-auth');

const THREADS_INDEX_KEY = 'gabai-threads-index.json';

function threadKey(userId) {
  return `gabai-thread-${userId}.json`;
}

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireAdmin(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  const userId = event.queryStringParameters && event.queryStringParameters.userId;
  if (!userId) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסר מזהה משתמש' }) };
  }

  try {
    const store = getAdminStore(event);
    const messages = (await store.get(threadKey(userId), { type: 'json' })) || [];

    const index = (await store.get(THREADS_INDEX_KEY, { type: 'json' })) || [];
    const entry = index.find(t => t.userId === userId);
    if (entry && entry.unreadByAdmin) {
      entry.unreadByAdmin = false;
      await store.setJSON(THREADS_INDEX_KEY, index);
    }

    return { statusCode: 200, headers, body: JSON.stringify({ success: true, messages, thread: entry || null }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
