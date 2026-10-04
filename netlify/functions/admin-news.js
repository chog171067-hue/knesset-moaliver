// דף ניהול - טאב "מהנעשה ונשמע": ניהול אירועים (כותרת, תאריך, תיאור וקישורי
// גוגל דרייב לתמונות/סרטונים - ראו news-store.js).
// GET: רשימת כל האירועים. POST: יצירה (בלי id בגוף) או עדכון (עם id בגוף).
// DELETE: מחיקת אירוע.
const { listEvents, saveEvent, deleteEvent } = require('./lib/news-store');
const { requireAdmin } = require('./lib/admin-auth');

exports.handler = async function (event, context) {
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type, Authorization',
    'Access-Control-Allow-Methods': 'GET, POST, DELETE, OPTIONS',
    'Content-Type': 'application/json'
  };

  if (event.httpMethod === 'OPTIONS') {
    return { statusCode: 200, headers, body: '' };
  }

  const auth = requireAdmin(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  try {
    if (event.httpMethod === 'GET') {
      const list = await listEvents(event);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, list }) };
    }

    if (event.httpMethod === 'POST') {
      const body = JSON.parse(event.body || '{}');
      const savedBy = (context.clientContext.user && context.clientContext.user.email) || null;
      const record = await saveEvent(event, body, savedBy);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, event: record }) };
    }

    if (event.httpMethod === 'DELETE') {
      const { id } = JSON.parse(event.body || '{}');
      if (!id) return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסר מזהה אירוע' }) };
      await deleteEvent(event, id);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true }) };
    }

    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
