// אזור אישי - טאב "הודעות לגבאי": שליפת השרשור האישי של המשתמש המחובר.
// markRead=1 (בקריאה שנעשית כשהטאב נפתח בפועל) מסמן שהתשובות מהגבאי נצפו,
// כדי שתג "הודעה חדשה" בתפריט הצד יתעדכן. קריאה בלי markRead (למשל בטעינת
// הדף, לבדיקת התג בלבד) לא נוגעת בסטטוס הנקרא/לא-נקרא.
const { getAdminStore } = require('./lib/blobs-store');

const THREADS_INDEX_KEY = 'gabai-threads-index.json';

function threadKey(userId) {
  return `gabai-thread-${userId}.json`;
}

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const authedUser = context.clientContext && context.clientContext.user;
  const authedTz = authedUser && authedUser.app_metadata && String(authedUser.app_metadata.verified_tz || '').trim();
  if (!authedTz) {
    return { statusCode: 401, headers, body: JSON.stringify({ success: false, error: 'עליך להתחבר עם חשבון מאומת' }) };
  }

  try {
    const store = getAdminStore(event);
    const userId = authedUser.sub || authedUser.id;
    const markRead = (event.queryStringParameters && event.queryStringParameters.markRead) === '1';

    const messages = (await store.get(threadKey(userId), { type: 'json' })) || [];

    let hasUnread = false;
    const index = (await store.get(THREADS_INDEX_KEY, { type: 'json' })) || [];
    const entry = index.find(t => t.userId === userId);
    if (entry) {
      hasUnread = !!entry.unreadByUser;
      if (markRead && entry.unreadByUser) {
        entry.unreadByUser = false;
        await store.setJSON(THREADS_INDEX_KEY, index);
        hasUnread = false;
      }
    }

    return { statusCode: 200, headers, body: JSON.stringify({ success: true, messages, hasUnread }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
