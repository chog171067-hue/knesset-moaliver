// דף ניהול - טאב "הוספת ת.ז לרשימת הזכאים": ניהול הרשימה המשלימה (בנוסף
// לגיליון הגוגל הראשי, שהוא לקריאה בלבד) של תעודות זהות זכאיות לפתיחת חשבון
// באזור האישי. community.js בודק גם מול הרשימה הזו (ראו getExtraEligibleRecord).
const { getAdminStore } = require('./lib/blobs-store');
const { requireAdmin } = require('./lib/admin-auth');

const STORE_KEY = 'extra-eligible-tz.json';

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

  const store = getAdminStore();

  try {
    if (event.httpMethod === 'GET') {
      const list = (await store.get(STORE_KEY, { type: 'json' })) || [];
      list.sort((a, b) => String(b.addedAt || '').localeCompare(String(a.addedAt || '')));
      return { statusCode: 200, headers, body: JSON.stringify({ success: true, list }) };
    }

    if (event.httpMethod === 'POST') {
      const { tz, phone, name } = JSON.parse(event.body || '{}');
      const cleanTz = String(tz || '').trim();

      if (!/^[0-9]{4,9}$/.test(cleanTz)) {
        return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'מספר תעודת זהות לא תקין' }) };
      }

      const list = (await store.get(STORE_KEY, { type: 'json' })) || [];
      if (list.some(r => r.tz === cleanTz)) {
        return { statusCode: 400, headers, body: JSON.stringify({ success: false, error: 'תעודת הזהות כבר קיימת ברשימה' }) };
      }

      list.push({
        tz: cleanTz,
        phone: phone ? String(phone).trim().replace(/[-\s]/g, '') : '',
        name: name ? String(name).trim() : '',
        addedAt: new Date().toISOString(),
        addedBy: (context.clientContext.user && context.clientContext.user.email) || ''
      });

      await store.setJSON(STORE_KEY, list);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true }) };
    }

    if (event.httpMethod === 'DELETE') {
      const { tz } = JSON.parse(event.body || '{}');
      const cleanTz = String(tz || '').trim();

      const list = (await store.get(STORE_KEY, { type: 'json' })) || [];
      const filtered = list.filter(r => r.tz !== cleanTz);

      await store.setJSON(STORE_KEY, filtered);
      return { statusCode: 200, headers, body: JSON.stringify({ success: true }) };
    }

    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
