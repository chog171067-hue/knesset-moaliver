// שכבת גישה יחידה לרשימת חברי הקהילה הזכאים ולחסימות.
// כל פונקציה אחרת (verify-tz, identity-signup, bind-tz, וכל פעולות האזור האישי)
// חייבת להשתמש בקובץ הזה ולא לממש בדיקה משלה - כך יש מקור אמת אחד בלבד.
//
// שלושה מקורות, כולם ב-Netlify Blobs (store "admin-data"):
// 1. רשימת התורמים (community-roster.json) - קובץ שהמנהל מייצא מכרטיס "תורמים"
//    בנדרים פלוס ומעלה בדף הניהול (ראו admin-community.js). כל העלאה מחליפה את
//    הרשימה כולה.
// 2. רשימה משלימה ידנית (extra-eligible-tz.json) - ת.ז שהמנהל מוסיף אחת-אחת
//    (ראו admin-eligible-list.js).
// 3. חסימות (blocked-tz.json) - ת.ז חסומות. חסימה מונעת פתיחת חשבון, ומשהה חשבון
//    קיים (בלי למחוק אותו). העלאת קובץ שמופיעה בו ת.ז חסומה מבטלת את החסימה שלה.
//
// אימות בעלות: בהרשמה חובה להשתמש במייל שרשום לת.ז ברשימה, ונטליפיי שולחת אליו
// מייל אימות - כך שמי שיודע רק ת.ז של חבר אחר לא יכול לפתוח חשבון על שמו.
const { getAdminStore } = require('./blobs-store');
const { ADMIN_TZ } = require('./admin-auth');

const ROSTER_KEY = 'community-roster.json';
const EXTRA_KEY = 'extra-eligible-tz.json';
const BLOCKED_KEY = 'blocked-tz.json';

// מפתח השוואה לת.ז: ספרות בלבד וללא אפסים מובילים - כדי ש-012345678 ו-12345678
// ייחשבו אותה ת.ז (אקסל נוטה להשמיט אפסים מובילים מעמודה מספרית).
function tzKey(tz) {
  return String(tz == null ? '' : tz).replace(/\D/g, '').replace(/^0+/, '');
}

function cleanPhone(phone) {
  return phone ? String(phone).trim().replace(/[-\s]/g, '') : '';
}

function normalizeEmail(email) {
  return String(email || '').trim().toLowerCase();
}

// "yossi@gmail.com" -> "yo***@gmail.com" - רמז למשתמש באיזה מייל להירשם, בלי לחשוף אותו
function maskEmail(email) {
  const clean = normalizeEmail(email);
  const at = clean.indexOf('@');
  if (at < 1) return '';
  return clean.slice(0, Math.min(2, at)) + '***' + clean.slice(at);
}

async function readJson(event, key, fallback) {
  const data = await getAdminStore(event).get(key, { type: 'json' });
  return data == null ? fallback : data;
}

async function getRoster(event) {
  return readJson(event, ROSTER_KEY, { list: [], uploadedAt: null, uploadedBy: '', fileName: '' });
}

async function getExtraList(event) {
  return readJson(event, EXTRA_KEY, []);
}

async function getBlockedList(event) {
  return readJson(event, BLOCKED_KEY, []);
}

async function saveRoster(event, roster) {
  await getAdminStore(event).setJSON(ROSTER_KEY, roster);
}

async function saveBlockedList(event, list) {
  await getAdminStore(event).setJSON(BLOCKED_KEY, list);
}

async function isTzBlocked(tz, event) {
  const key = tzKey(tz);
  if (!key || key === tzKey(ADMIN_TZ)) return false; // המנהל לעולם לא נחסם - אחרת אין דרך לבטל
  const blocked = await getBlockedList(event);
  return blocked.some(b => tzKey(b.tz) === key);
}

// מחזיר את רשומת הזכאות של ת.ז (מהקובץ, ואם אין - מהרשימה הידנית), או null.
// לא בודק חסימה - לזה יש את isTzBlocked.
async function getCommunityRecord(tz, event) {
  const key = tzKey(tz);
  if (!key) return null;

  const roster = await getRoster(event);
  const fromRoster = roster.list.find(r => tzKey(r.tz) === key);
  if (fromRoster) {
    return {
      tz: fromRoster.tz,
      name: [fromRoster.firstName, fromRoster.lastName].filter(Boolean).join(' '),
      email: normalizeEmail(fromRoster.email),
      phone: (fromRoster.phones || [])[0] || '',
      source: 'roster'
    };
  }

  const extra = await getExtraList(event);
  const fromExtra = extra.find(r => tzKey(r.tz) === key);
  if (fromExtra) {
    return {
      tz: fromExtra.tz,
      name: fromExtra.name || '',
      email: normalizeEmail(fromExtra.email),
      phone: fromExtra.phone || '',
      source: 'extra'
    };
  }

  return null;
}

// בדיקה משותפת לכל פעולה באזור האישי: משתמש מחובר, עם ת.ז מקושרת, שאינה חסומה.
// מחזיר { ok: true, user, tz, phone } או { ok: false, statusCode, error, suspended }.
async function requireActiveMember(event, context) {
  const user = context.clientContext && context.clientContext.user;
  const tz = user && user.app_metadata && String(user.app_metadata.verified_tz || '').trim();
  if (!tz) {
    return { ok: false, statusCode: 401, error: 'עליך להתחבר עם חשבון מאומת כדי לצפות בנתונים' };
  }
  let blocked;
  try {
    blocked = await isTzBlocked(tz, event);
  } catch (e) {
    console.error('[community] כשל בבדיקת חסימה:', e.message);
    return { ok: false, statusCode: 500, error: 'שגיאת שרת, נסה שוב מאוחר יותר' };
  }
  if (blocked) {
    return { ok: false, statusCode: 403, suspended: true, error: 'החשבון שלך באזור האישי מושהה. לפרטים יש לפנות לגבאי.' };
  }
  return { ok: true, user, tz, phone: String(user.app_metadata.verified_phone || '').trim() };
}

// ===================== חברות זמנית מהקיוסק (הוראת קבע חדשה) =====================
//
// מי שמקים הוראת קבע "דמי חבר" דרך הקיוסק נחשב חבר קהילה מיד - עד העלאת קובץ
// התורמים הבא. מאותו רגע הקובץ קובע: אם הוא שם - נשאר חבר, ואם לא - כבר לא.
// הרשימה נפרדת מהרשימה הידנית (extra), ומשמשת רק את הקיוסק (לא את הרשמת האזור האישי).
const KEVA_JOINED_KEY = 'kiosk-keva-members.json';

async function getKevaJoinedList(event) {
  return readJson(event, KEVA_JOINED_KEY, []);
}

async function addKevaJoinedMember(event, { tz, kevaId, amount, groupe, mosadId }) {
  const key = tzKey(tz);
  const list = (await getKevaJoinedList(event)).filter(e => tzKey(e.tz) !== key);
  list.push({ tz: String(tz), kevaId: String(kevaId || ''), amount: Number(amount) || 0, groupe: groupe || '', mosadId: String(mosadId || ''), addedAt: new Date().toISOString() });
  await getAdminStore(event).setJSON(KEVA_JOINED_KEY, list);
}

// מזהי כל הוראות הקבע של ת"ז (לפני ההצטרפות) - כדי שרק הוראה חדשה תיחשב
function kevaIdsForTz(kevot, tz) {
  const key = tzKey(tz);
  return kevot.filter(k => tzKey(k.Zeout) === key).map(k => String(k.KevaId));
}

// הוראת קבע פעילה שעומדת בתנאי החברות (קטגוריה + סכום מינימלי), לפי ת"ז.
// excludeKevaIds: הוראות שהיו קיימות כבר לפני ההצטרפות - לא נחשבות (הקובץ קובע לגביהן)
function findMembershipKeva(kevot, tz, { groupe, minAmount, excludeKevaIds = [] }) {
  const key = tzKey(tz);
  const wantedGroupe = String(groupe || '').trim();
  const excluded = new Set(excludeKevaIds.map(String));
  return kevot.find(k =>
    tzKey(k.Zeout) === key &&
    !excluded.has(String(k.KevaId)) &&
    String(k.Enabled) === '1' &&
    (!wantedGroupe || String(k.Groupe || '').trim().includes(wantedGroupe)) &&
    Number(k.Amount) >= Number(minAmount || 0)
  ) || null;
}

// חבר קהילה לעניין הקיוסק: בקובץ / ברשימה הידנית, או הקים הוראת קבע דרך הקיוסק אחרי
// העלאת הקובץ האחרונה. fetchKevot (אופציונלי, רק אחרי "הצטרפות"): פונקציה שמחזירה את כל
// הוראות הקבע - אם נשלחה ואין חברות, מחפשים הוראה חדשה (שאינה ב-excludeKevaIds) ושומרים
// חברות זמנית. מחזיר { member, source, joinedNow }.
async function getKioskMembership(event, tz, { fetchKevot, groupe, minAmount, excludeKevaIds } = {}) {
  if (await getCommunityRecord(tz, event)) return { member: true, source: 'list' };

  const key = tzKey(tz);
  const roster = await getRoster(event);
  const rosterTime = roster.uploadedAt ? Date.parse(roster.uploadedAt) : 0;
  const joined = (await getKevaJoinedList(event)).find(e => tzKey(e.tz) === key);
  if (joined && Date.parse(joined.addedAt) > rosterTime) return { member: true, source: 'keva' };

  if (!fetchKevot) return { member: false, source: null };
  const keva = findMembershipKeva(await fetchKevot(), tz, { groupe, minAmount, excludeKevaIds });
  if (!keva) return { member: false, source: null };
  await addKevaJoinedMember(event, { tz, kevaId: keva.KevaId, amount: keva.Amount, groupe: keva.Groupe, mosadId: keva.mosadId });
  return { member: true, source: 'keva', joinedNow: true };
}

module.exports = {
  tzKey,
  cleanPhone,
  normalizeEmail,
  maskEmail,
  getRoster,
  getExtraList,
  getBlockedList,
  saveRoster,
  saveBlockedList,
  isTzBlocked,
  getCommunityRecord,
  requireActiveMember,
  getKevaJoinedList,
  findMembershipKeva,
  kevaIdsForTz,
  getKioskMembership
};
