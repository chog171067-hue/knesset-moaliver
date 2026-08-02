// האזור האישי: יתרה עדכנית של חשבון הקיוסק המקושר של המשתמש המחובר בלבד.
const { requirePersonalLibraryLink } = require('./lib/personal-library-auth');
const { getUserById } = require('./lib/kiosk-store');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requirePersonalLibraryLink(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error, linked: auth.linked }) };
  }

  try {
    const user = await getUserById(event, auth.kioskUserId);
    if (!user) {
      return { statusCode: 404, headers, body: JSON.stringify({ success: false, error: 'חשבון הקיוסק המקושר לא נמצא (יתכן ונמחק ע"י הצוות)' }) };
    }
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, user }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
