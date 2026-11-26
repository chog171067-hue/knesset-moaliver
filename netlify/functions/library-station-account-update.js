// עמדת הקיוסק - "פרטי החשבון": המשתמש המחובר מעדכן את השם, הטלפון והסיסמה שלו.
// תעודת הזהות (שם המשתמש) לא ניתנת לשינוי. כל שמירה דורשת את הסיסמה הנוכחית - כדי שמי
// שניגש לעמדה שנשארה מחוברת לא יוכל לשנות טלפון (שמשמש לשחזור סיסמה) או סיסמה.
//
// גוף הבקשה: { userId, currentPassword, displayName, phone, newPassword? }
// תשובה: { success: true, user }
const { requireStation } = require('./lib/station-auth');
const { getUserById, authenticateKioskUser, updateOwnAccount } = require('./lib/kiosk-store');

exports.handler = async function (event) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireStation(event);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  try {
    const { userId, currentPassword, displayName, phone, newPassword } = JSON.parse(event.body || '{}');
    if (!userId || !currentPassword) {
      return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'יש להקליד את הסיסמה הנוכחית' }) };
    }
    const existing = await getUserById(event, userId);
    if (!existing) throw new Error('משתמש לא נמצא');

    const check = await authenticateKioskUser(event, { username: existing.username, password: currentPassword });
    if (!check.ok || check.user.id !== userId) {
      return { statusCode: 401, headers, body: JSON.stringify({ success: false, error: 'הסיסמה הנוכחית שגויה' }) };
    }

    const user = await updateOwnAccount(event, { userId, displayName, phone, newPassword });
    return { statusCode: 200, headers, body: JSON.stringify({ success: true, user }) };
  } catch (error) {
    return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: error.message }) };
  }
};
