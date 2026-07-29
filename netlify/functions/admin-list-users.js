// דף ניהול - טאב "משתמשים רשומים": מחזיר את כל בעלי החשבון באזור האישי
// (Netlify Identity) עם מלוא הפרטים ששמורים עליהם, כולל כרטיס חבר הקהילה.
const { listAllUsers } = require('./lib/identity-admin');
const { requireAdmin } = require('./lib/admin-auth');

exports.handler = async function (event, context) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

  if (event.httpMethod !== 'GET') {
    return { statusCode: 405, headers, body: 'Method Not Allowed' };
  }

  const auth = requireAdmin(context);
  if (!auth.authorized) {
    return { statusCode: auth.statusCode, headers, body: JSON.stringify({ success: false, error: auth.error }) };
  }

  try {
    const identity = context.clientContext && context.clientContext.identity;
    const users = await listAllUsers(identity);

    const list = users.map(u => {
      const meta = u.app_metadata || {};
      const um = u.user_metadata || {};
      const mc = um.member_card || {};

      return {
        id: u.id,
        email: u.email,
        fullName: um.full_name || um.name || '',
        verifiedTz: meta.verified_tz || '',
        verifiedPhone: meta.verified_phone || '',
        provider: meta.provider || 'email',
        createdAt: u.created_at || null,
        confirmedAt: u.confirmed_at || null,
        lastSignInAt: u.last_sign_in_at || null,
        memberCard: {
          street: mc.address_street || '',
          city: mc.address_city || '',
          phone: mc.phone_contact || '',
          phoneExtra: mc.phone_extra || '',
          shevet: mc.shevet || '',
          fatherName: mc.father_name || '',
          yahrzeits: mc.yahrzeits || []
        }
      };
    });

    list.sort((a, b) => String(b.createdAt || '').localeCompare(String(a.createdAt || '')));

    return { statusCode: 200, headers, body: JSON.stringify({ success: true, count: list.length, users: list }) };
  } catch (error) {
    return { statusCode: 500, headers, body: JSON.stringify({ success: false, error: 'שגיאת שרת: ' + error.message }) };
  }
};
