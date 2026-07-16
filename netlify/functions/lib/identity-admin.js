const axios = require('axios');

function getAdminConfig() {
  const identityToken = process.env.NETLIFY_IDENTITY_ADMIN_TOKEN;
  const siteId = process.env.SITE_ID;
  if (!identityToken || !siteId) {
    throw new Error('חסרים משתני סביבה NETLIFY_IDENTITY_ADMIN_TOKEN / SITE_ID');
  }
  return { identityToken, siteId };
}

// שולף את כל המשתמשים הרשומים ב-Identity, עם דפדוף מלא (חשוב! ברירת המחדל של ה-API
// מחזירה רק עמוד ראשון - בלי זה, מקהילה עם יותר מ-~25 חברים רשומים המערכת "לא תמצא" חלק מהם)
async function listAllUsers() {
  const { identityToken, siteId } = getAdminConfig();
  let allUsers = [];
  let page = 1;
  const perPage = 100;

  while (true) {
    const res = await axios.get(
      `https://api.netlify.com/api/v1/sites/${siteId}/identity/users`,
      {
        headers: { Authorization: `Bearer ${identityToken}` },
        params: { per_page: perPage, page }
      }
    );
    const users = Array.isArray(res.data) ? res.data : (res.data.users || []);
    allUsers = allUsers.concat(users);
    if (users.length < perPage) break;
    page++;
    if (page > 50) break; // רשת ביטחון מפני לולאה אינסופית
  }

  return allUsers;
}

// מחפש האם קיים כבר משתמש עם ת"ז מאומתת ומקושרת (ב-app_metadata - השדה המוגן מפני עריכת לקוח)
async function findUserByBoundTz(tz) {
  const users = await listAllUsers();
  const cleanTz = String(tz).trim();
  return users.find(u => u.app_metadata && String(u.app_metadata.verified_tz) === cleanTz) || null;
}

// מעדכן את ה-app_metadata של משתמש ספציפי - הדרך היחידה שצריכה לשמש לקישור ת"ז לחשבון
async function setUserVerifiedTz(userId, tz, phone) {
  const { identityToken, siteId } = getAdminConfig();

  const current = await axios.get(
    `https://api.netlify.com/api/v1/sites/${siteId}/identity/users/${userId}`,
    { headers: { Authorization: `Bearer ${identityToken}` } }
  );
  const existingAppMetadata = current.data.app_metadata || {};

  await axios.put(
    `https://api.netlify.com/api/v1/sites/${siteId}/identity/users/${userId}`,
    {
      app_metadata: {
        ...existingAppMetadata,
        roles: existingAppMetadata.roles && existingAppMetadata.roles.length ? existingAppMetadata.roles : ['user'],
        verified_tz: String(tz),
        verified_phone: phone || null
      }
    },
    { headers: { Authorization: `Bearer ${identityToken}` } }
  );
}

module.exports = { listAllUsers, findUserByBoundTz, setUserVerifiedTz };
