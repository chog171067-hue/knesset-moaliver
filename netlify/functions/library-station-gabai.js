// עמדת הקיוסק - "שליחת הודעה לגבאי" מהלשונית הצפה (ראו lib/kiosk-gabai.js).
//
// גוף הבקשה: { userId, action: 'get' | 'send', text?, markRead? }
// תשובה: { success, messages: [{ from: 'user'|'admin', text, timestamp }], hasUnread }
const { requireStation } = require('./lib/station-auth');
const { getUserById } = require('./lib/kiosk-store');
const { getKioskThread, sendKioskMessage } = require('./lib/kiosk-gabai');

exports.handler = async function (event) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireStation(event);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  let body;
  try {
    body = JSON.parse(event.body || '{}');
  } catch (e) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'בקשה לא תקינה' }) };
  }

  try {
    const user = body.userId ? await getUserById(event, String(body.userId)) : null;
    if (!user || !user.isActive) {
      return { statusCode: 404, headers, body: JSON.stringify({ success: false, error: 'החשבון לא נמצא' }) };
    }

    if (body.action === 'send') {
      try {
        await sendKioskMessage(event, user, body.text);
      } catch (e) {
        return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: e.message }) };
      }
      const thread = await getKioskThread(event, user.id);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, ...thread }) };
    }

    const thread = await getKioskThread(event, user.id, { markRead: !!body.markRead });
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, ...thread }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
