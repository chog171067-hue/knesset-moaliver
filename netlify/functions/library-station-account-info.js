// עמדת הקיוסק - הלשונית הצפה של המשתמש המחובר: האם הוא חבר קהילה (לתגית), היתרה
// העדכנית ו-30 העסקאות האחרונות שלו (הטענות, חיובי הדפסה, החזרים) - ל"היסטוריית תשלומים".
//
// גוף הבקשה: { userId }
// תשובה: { success, isCommunityMember, balance, transactions: [{ timestamp, type, amount, description }] }
const { requireStation } = require('./lib/station-auth');
const { getUserById, listUserTransactions } = require('./lib/kiosk-store');
const { getKioskMembership } = require('./lib/community');

const MAX_TRANSACTIONS = 30;

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
    const { userId } = JSON.parse(event.body || '{}');
    const user = userId ? await getUserById(event, String(userId)) : null;
    if (!user || !user.isActive) {
      return { statusCode: 404, headers, body: JSON.stringify({ success: false, error: 'החשבון לא נמצא' }) };
    }

    let isCommunityMember = false;
    try {
      isCommunityMember = (await getKioskMembership(event, user.username)).member;
    } catch (e) {
      // תקלה בבדיקת החברות לא מפילה את הלשונית - רק בלי תגית
      console.error('[account-info] כשל בבדיקת חברות קהילה:', e.message);
    }

    const transactions = (await listUserTransactions(event, user.id))
      .slice(0, MAX_TRANSACTIONS)
      .map(t => ({ timestamp: t.timestamp, type: t.type, amount: t.amount, description: t.description || '' }));

    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({ success: true, isCommunityMember, balance: user.balance, transactions })
    };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
