// עמדת הקיוסק (וגם האזור האישי, לאחר הטענה שיזם משם) בודקים (polling) האם
// אסימון הטענה כבר אושר בפועל ע"י ה-CallBack, לפני ששחררים ללקוח יתרה
// מעודכנת על המסך - בהתאם להנחיה המפורשת של נדרים פלוס לא לסמוך על תגובת
// הדפדפן/האייפרם בלבד. הטוקן עצמו (UUID אקראי) הוא הסוד היחיד שנדרש כאן -
// לכן מספיק שהקורא הוא עמדה, מנהל, או כל משתמש אתר מחובר (לא בהכרח בעל
// אותה הטענה) - אין דרך לזכות כסף דרך הנתיב הזה, רק לקרוא סטטוס.
const { requireStation } = require('./lib/station-auth');
const { requireAdmin } = require('./lib/admin-auth');
const { getPendingTopupStatus } = require('./lib/kiosk-store');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const stationAuth = requireStation(event);
  const adminAuth = requireAdmin(context);
  const siteUser = context.clientContext && context.clientContext.user;
  if (!stationAuth.authorized && !adminAuth.authorized && !siteUser) {
    return { statusCode: 401, headers, body: JSON.stringify({ success: false, error: 'נדרשת הרשאת עמדה, הרשאת מנהל, או התחברות לאתר' }) };
  }

  const token = event.queryStringParameters && event.queryStringParameters.token;
  if (!token) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסר אסימון הטענה' }) };
  }

  try {
    const status = await getPendingTopupStatus(event, token);
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, ...status }) };
  } catch (error) {
    return { statusCode: 404, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
