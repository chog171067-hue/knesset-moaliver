// שכבת גישה ל-GoTrue Admin API - הדרך המובנית והרשמית: משתמשים ב-identity.url +
// identity.token שנטליפיי מספקת אוטומטית בכל Function (context.clientContext.identity),
// ולא ב-api.netlify.com + טוקן אישי + SITE_ID (התברר כלא אמין - 404 חוזר).

function assertIdentity(identity) {
  if (!identity || !identity.url || !identity.token) {
    throw new Error('אין גישה ל-Identity API - ודא ש-Identity מופעל באתר');
  }
}

// שולף את כל המשתמשים הרשומים ב-Identity, עם דפדוף מלא
async function listAllUsers(identity) {
  assertIdentity(identity);
  let allUsers = [];
  let page = 1;
  const perPage = 100;

  while (true) {
    const res = await fetch(`${identity.url}/admin/users?per_page=${perPage}&page=${page}`, {
      headers: { Authorization: `Bearer ${identity.token}` }
    });
    if (!res.ok) {
      throw new Error(`Identity Admin API החזיר שגיאה: ${res.status}`);
    }
    const data = await res.json();
    const users = Array.isArray(data) ? data : (data.users || []);
    allUsers = allUsers.concat(users);
    if (users.length < perPage) break;
    page++;
    if (page > 50) break; // רשת ביטחון מפני לולאה אינסופית
  }

  return allUsers;
}

// מחפש האם קיים כבר משתמש עם ת"ז מאומתת ומקושרת (ב-app_metadata - השדה המוגן מפני עריכת לקוח)
async function findUserByBoundTz(identity, tz) {
  const users = await listAllUsers(identity);
  const cleanTz = String(tz).trim();
  return users.find(u => u.app_metadata && String(u.app_metadata.verified_tz) === cleanTz) || null;
}

// מעדכן את ה-app_metadata של משתמש ספציפי - הדרך היחידה שצריכה לשמש לקישור ת"ז לחשבון
async function setUserVerifiedTz(identity, userId, tz, phone) {
  assertIdentity(identity);
  const baseUrl = `${identity.url}/admin/users/${userId}`;

  const currentRes = await fetch(baseUrl, {
    headers: { Authorization: `Bearer ${identity.token}` }
  });
  if (!currentRes.ok) {
    throw new Error(`כשל בשליפת פרטי המשתמש: ${currentRes.status}`);
  }
  const currentData = await currentRes.json();
  const existingAppMetadata = currentData.app_metadata || {};

  const putRes = await fetch(baseUrl, {
    method: 'PUT',
    headers: {
      Authorization: `Bearer ${identity.token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      app_metadata: {
        ...existingAppMetadata,
        roles: existingAppMetadata.roles && existingAppMetadata.roles.length ? existingAppMetadata.roles : ['user'],
        verified_tz: String(tz),
        verified_phone: phone || null
      }
    })
  });
  if (!putRes.ok) {
    throw new Error(`כשל בעדכון פרטי המשתמש: ${putRes.status}`);
  }
}

module.exports = { listAllUsers, findUserByBoundTz, setUserVerifiedTz };
