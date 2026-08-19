// דף ניהול - טאב "ערב התרמה": ניהול פרופילים (לוגו ימין, לוגו שמאל, סטריפ,
// סף סכום) לכל מקבל קישור למערכת ההתרמה (donation-app/control.html + display.html).
// GET בלי id: רשימה קלה (בלי תמונות), לטבלה בדף הניהול.
// GET עם id: פרופיל מלא כולל תמונות (לטעינה מחדש לטופס עריכה).
// POST: יצירה (בלי id בגוף) או עדכון (עם id בגוף).
// DELETE: מחיקת פרופיל.
const { listProfiles, getConfigById, saveProfile, deleteProfile } = require('./lib/donation-app-store');
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
      const id = (event.queryStringParameters || {}).id;

      if (id) {
        const record = await getConfigById(event, id);
        if (!record) return { statusCode: 404, headers, body: JSON.stringify({ success: false, error: 'הפרופיל לא נמצא' }) };
        return { statusCode: 200, headers, body: JSON.stringify({ success: true, profile: record }) };
      }

      const list = await listProfiles(event);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, list }) };
    }

    if (event.httpMethod === 'POST') {
      const body = JSON.parse(event.body || '{}');
      const savedBy = (context.clientContext.user && context.clientContext.user.email) || null;
      const record = await saveProfile(event, body, savedBy);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, profile: record }) };
    }

    if (event.httpMethod === 'DELETE') {
      const { id } = JSON.parse(event.body || '{}');
      if (!id) return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'חסר מזהה פרופיל' }) };
      await deleteProfile(event, id);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true }) };
    }

    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
