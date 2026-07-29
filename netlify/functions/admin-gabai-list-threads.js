// דף ניהול - טאב "הודעות לגבאי": מחזיר את רשימת כל השיחות (שרשור אחד לכל
// משתמש ששלח אי-פעם הודעה), ממוינות לפי ההודעה האחרונה, עם סימון "לא נקרא".
const { getAdminStore } = require('./lib/blobs-store');
const { requireAdmin } = require('./lib/admin-auth');

const THREADS_INDEX_KEY = 'gabai-threads-index.json';

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireAdmin(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  try {
    const store = getAdminStore(event);
    const index = (await store.get(THREADS_INDEX_KEY, { type: 'json' })) || [];
    index.sort((a, b) => String(b.lastMessageAt || '').localeCompare(String(a.lastMessageAt || '')));

    return { statusCode: 200, headers, body: JSON.stringify({ success: true, threads: index }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
