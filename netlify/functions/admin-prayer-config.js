// דף ניהול - טאב "הגדרת תפילות": קריאה ושמירה של הגדרות זמני התפילות האוטומטיים.
// GET: ההגדרות הנוכחיות. POST: שמירת ההגדרות (כל המסמך).
const { readPrayerConfig, savePrayerConfig } = require('./lib/prayer-config-store');
const { requireAdmin } = require('./lib/admin-auth');

exports.handler = async function (event, context) {
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type, Authorization',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
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
      const config = await readPrayerConfig(event);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, config }) };
    }

    if (event.httpMethod === 'POST') {
      const body = JSON.parse(event.body || '{}');
      const savedBy = (context.clientContext.user && context.clientContext.user.email) || null;
      const config = await savePrayerConfig(event, body.config, savedBy);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, config }) };
    }

    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
