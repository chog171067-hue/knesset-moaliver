// עמדת הקיוסק - יתרה מהמערכת הישנה (סייפר): בכניסה לחשבון העמדה בודקת אם יש ברשימה
// יתרה שלא מומשה עם אותו שם פרטי ומשפחה (action: 'check'), ואם המשתמש לוחץ "מימוש
// היתרה" - היא נוספת ליתרה שלו, פעם אחת בלבד (action: 'claim').
//
// גוף הבקשה: { userId, action: 'check' | 'claim', legacyId? }
// תשובה: check - { success, found, legacyId, amount }; claim - { success, balance, amount }
const { requireStation } = require('./lib/station-auth');
const { findLegacyBalanceForUser, claimLegacyBalance } = require('./lib/kiosk-store');

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
  if (!body.userId) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסר מזהה משתמש' }) };
  }

  try {
    if (body.action === 'claim') {
      const result = await claimLegacyBalance(event, { userId: String(body.userId), legacyId: body.legacyId });
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, ...result }) };
    }
    const match = await findLegacyBalanceForUser(event, String(body.userId));
    return {
      statusCode: 200,
      headers,
      body: JSON.stringify(match ? { success: true, found: true, ...match } : { success: true, found: false })
    };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
