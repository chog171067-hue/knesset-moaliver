// לוגיקת אימות מקור משותפת לכל ה-CallBack-ים שמגיעים מנדרים פלוס (תרומות
// בית הכנסת וגם הטענות הקיוסק) - ראו תיעוד ה-API, "אימות מקור העדכון":
// העדכונים מגיעים תמיד מאחת משתי כתובות ה-IP הבאות בלבד.
const NEDARIM_IPS = ['18.196.146.117', '18.194.219.73'];

function getSourceIp(event) {
  // נטליפיי מעביר את שרשרת ה-IP האמיתית ב-x-nf-client-connection-ip,
  // ובמידה וזה חסר - fallback לכותרת הסטנדרטית x-forwarded-for
  const headers = event.headers || {};
  const direct = headers['x-nf-client-connection-ip'];
  if (direct) return direct.trim();

  const forwarded = headers['x-forwarded-for'];
  if (forwarded) return forwarded.split(',')[0].trim();

  return null;
}

function isFromNedarim(event) {
  const sourceIp = getSourceIp(event);
  return { sourceIp, verified: !!sourceIp && NEDARIM_IPS.includes(sourceIp) };
}

module.exports = { NEDARIM_IPS, getSourceIp, isFromNedarim };
