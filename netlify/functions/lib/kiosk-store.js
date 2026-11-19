// שכבת גישה לנתונים של "עמדת הקיוסק לספרייה" (משתמשים, יתרות, עסקאות) - ראו
// מסמך האפיון: users.json ו-transactions.json הם מקור האמת היחיד, וכל שאר
// הקוד (עמוד ניהול, עמדת הקיוסק בעתיד) עובר דרך הפונקציות כאן ולא נוגע
// ב-Blobs ישירות. כשירצו בעתיד להחליף את האחסון (למשל ל-Postgres, כדי לתמוך
// בכמה עמדות עם עומס גבוה יותר), רק הקובץ הזה אמור להשתנות.
//
// שני המסמכים (users.json, transactions.json) נשמרים כ-blob יחיד כל אחד -
// סביר לגמרי לעמדת ספרייה בודדת עם מאות משתמשים לכל היותר, ומאפשר עדכון
// אטומי-בפועל (optimistic concurrency לפי ETag, ראו writeWithRetry) בלי
// לנהל טרנזקציות אמיתיות שה-Blobs לא תומך בהן.
const { getStore, connectLambda } = require('@netlify/blobs');
const crypto = require('crypto');

const USERS_KEY = 'users.json';
const TRANSACTIONS_KEY = 'transactions.json';
const CONFIG_KEY = 'config.json';
const PENDING_TOPUPS_KEY = 'pending-topups.json';
// אישורי תשלום מנדרים פלוס שלא הצלחנו לשייך לחשבון - נשמרים לטיפול ידני, כדי
// שתשלום אמיתי לעולם לא "ייעלם" בשקט (נדרים פלוס לא שולחים אישור פעם שנייה)
const ORPHAN_TOPUPS_KEY = 'orphan-topups.json';
// יומן שחזורי סיסמה בטלפון (ימות המשיח) - כל ניסיון נרשם כאן, גם כושל. מאותו
// יומן מחושבות גם מגבלות הניסיונות (ראו PHONE_RESET_LIMITS), כך שאין מקור אמת נוסף.
const PASSWORD_RESETS_KEY = 'password-resets.json';
const MAX_PASSWORD_RESET_LOG = 3000;
const MAX_WRITE_ATTEMPTS = 8;

const DEFAULT_CONFIG = { pricePerPage: 0.2, allowedApps: [], textEditorEnabled: false };

// ⚠️ עקביות הנתונים - קריטי כאן כי מדובר בכסף:
// כברירת מחדל, קריאה מ-Netlify Blobs עוברת דרך מטמון (CDN) ועלולה להחזיר נתונים
// ישנים של כמה שניות. קריאה ישנה + כתיבה = דריסת עדכון שנעשה בינתיים (למשל החזר
// שקרא יתרה מלפני החיוב). לכן מבקשים "עקביות חזקה" (קריאה ישירות מהמקור, בלי מטמון)
// בכל פעם שהסביבה מספקת את הכתובת הדרושה לכך. בכל מקרה, הכתיבות מותנות ב-ETag
// (ראו writeWithRetry), כך שגם קריאה ישנה לא יכולה לדרוס נתונים - רק לגרום לניסיון חוזר.
let lastStoreMode = 'unknown';

function findUncachedEdgeURL(event) {
  try {
    const data = JSON.parse(Buffer.from(event.blobs, 'base64').toString('utf8'));
    if (data.uncachedEdgeURL) return data.uncachedEdgeURL;
    if (data.uncached_edge_url) return data.uncached_edge_url;
  } catch (e) { /* אין event.blobs תקין - ננסה מקור אחר */ }
  try {
    const raw = globalThis.netlifyBlobsContext || process.env.NETLIFY_BLOBS_CONTEXT;
    if (raw) {
      const ctx = JSON.parse(Buffer.from(raw, 'base64').toString('utf8'));
      if (ctx.uncachedEdgeURL) return ctx.uncachedEdgeURL;
    }
  } catch (e) { /* אין הקשר סביבה */ }
  return null;
}

function getKioskStore(event) {
  connectLambda(event);
  const uncachedEdgeURL = findUncachedEdgeURL(event);
  if (uncachedEdgeURL) {
    lastStoreMode = 'strong';
    return getStore({ name: 'library-kiosk', consistency: 'strong', uncachedEdgeURL });
  }
  lastStoreMode = 'eventual';
  return getStore('library-kiosk');
}

function getStoreMode(event) {
  getKioskStore(event);
  return lastStoreMode;
}

// כתיבה בטוחה מול עדכונים מקבילים: קוראים את המסמך הנוכחי (עם ה-ETag שלו),
// מפעילים עליו את מוטציית הקריאה (mutateFn יכולה לזרוק כדי לבטל את הכתיבה -
// למשל "שם משתמש כבר תפוס"), וכותבים בחזרה רק אם ה-ETag לא השתנה בינתיים.
// אם כן השתנה (modified: false) - קוראים מחדש ומנסים שוב, עד MAX_WRITE_ATTEMPTS.
//
// ⚠️ משתמשים ב-store.set ולא ב-store.setJSON: ב-@netlify/blobs 10.x יש באג שבו setJSON
// לא שולח את תנאי ה-ETag לשרת (הכתיבה תמיד "מצליחה" ודורסת). זה גרם בפועל לאובדן
// עסקאות ולחישובי יתרה שגויים. store.set מעביר את התנאי כראוי.
async function writeWithRetry(store, key, defaultValue, mutateFn) {
  for (let attempt = 0; attempt < MAX_WRITE_ATTEMPTS; attempt++) {
    if (attempt > 0) {
      // המתנה הולכת וגדלה - נותנת למטמון זמן להתעדכן אם הקריאה הקודמת הייתה ישנה
      await new Promise(resolve => setTimeout(resolve, 250 * attempt));
    }
    const existing = await store.getWithMetadata(key, { type: 'json' });
    const data = (existing && existing.data) || defaultValue;
    const etag = existing && existing.etag;

    const result = mutateFn(data);

    const writeResult = await store.set(key, JSON.stringify(data), etag ? { onlyIfMatch: etag } : { onlyIfNew: true });
    if (writeResult.modified) return result;
  }
  throw new Error('שגיאת שרת: לא ניתן לשמור את השינוי כרגע עקב עומס, נסה שוב');
}

function hashPassword(password) {
  const salt = crypto.randomBytes(16).toString('hex');
  const hash = crypto.scryptSync(password, salt, 64).toString('hex');
  return { salt, hash };
}

function verifyPassword(password, salt, hash) {
  const candidate = crypto.scryptSync(password, salt, 64);
  const expected = Buffer.from(hash, 'hex');
  return candidate.length === expected.length && crypto.timingSafeEqual(candidate, expected);
}

// טלפון ישראלי: ספרות בלבד (מקפים/רווחים מוסרים), 9-10 ספרות, מתחיל ב-0
function normalizePhone(phone) {
  const clean = String(phone || '').replace(/[-\s]/g, '');
  return /^0\d{8,9}$/.test(clean) ? clean : null;
}

function toPublicUser(user) {
  if (!user) return null;
  const { passwordSalt, passwordHash, tempPasswordSalt, tempPasswordHash, tempFailedAttempts, ...publicFields } = user;
  return publicFields;
}

// שם המשתמש בקיוסק הוא תעודת זהות. בטלפון אנשים מקלידים לפעמים בלי אפס מוביל,
// לכן משווים ספרות בלבד, בלי אפסים מובילים, ורק כששם המשתמש עצמו הוא מספר.
function normalizeTz(value) {
  const digits = String(value || '').trim();
  if (!/^\d{5,9}$/.test(digits)) return null;
  return digits.replace(/^0+/, '') || null;
}

// מספר המתקשר כפי שימות המשיח מעבירה אותו (ApiPhone) - לפעמים בפורמט בינלאומי
function normalizeCallerPhone(phone) {
  let clean = String(phone || '').replace(/[^\d]/g, '');
  if (clean.startsWith('972')) clean = '0' + clean.slice(3);
  return normalizePhone(clean);
}

async function listUsers(event) {
  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  return Object.values(users).map(toPublicUser).sort((a, b) => a.username.localeCompare(b.username, 'he'));
}

async function getUserById(event, userId) {
  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  return toPublicUser(users[userId]) || null;
}

async function findUserByUsername(event, username) {
  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  const needle = String(username || '').trim().toLowerCase();
  return Object.values(users).find(u => u.username.toLowerCase() === needle) || null;
}

async function createUser(event, { username, password, displayName, phone }) {
  const cleanUsername = String(username || '').trim();
  if (!cleanUsername) throw new Error('שם משתמש הוא שדה חובה');
  if (String(displayName || '').trim().length < 2) throw new Error('שם מלא הוא שדה חובה (נדרש לתשלומים בנדרים פלוס)');
  const cleanPhone = normalizePhone(phone);
  if (!cleanPhone) throw new Error('יש להזין מספר טלפון תקין (נדרש לתשלומים בנדרים פלוס)');
  if (!password || String(password).length < 4) throw new Error('הסיסמה חייבת להכיל לפחות 4 תווים');

  const store = getKioskStore(event);
  const { salt, hash } = hashPassword(String(password));

  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    const clash = Object.values(users).some(u => u.username.toLowerCase() === cleanUsername.toLowerCase());
    if (clash) throw new Error('שם המשתמש כבר תפוס');

    const id = crypto.randomUUID();
    const now = new Date().toISOString();
    users[id] = {
      id,
      username: cleanUsername,
      // שם מלא, לתצוגה בממשק הניהול ולשליחה כ"שם משלם" לנדרים פלוס בהטענת
      // כסף (ראו library-topup-init.js) - אופציונלי כדי לא לשבור משתמשים
      // ישנים שנוצרו בלי השדה הזה.
      displayName: String(displayName || '').trim(),
      phone: cleanPhone,
      passwordSalt: salt,
      passwordHash: hash,
      balance: 0,
      isActive: true,
      createdAt: now,
      updatedAt: now
    };
    return toPublicUser(users[id]);
  });
}

// עדכון שם מלא + טלפון (שניהם נשלחים לנדרים פלוס בכל הטענה - המוסד מחייב אותם)
async function setUserDetails(event, { userId, displayName, phone }) {
  const name = String(displayName || '').trim();
  if (name.length < 2) throw new Error('יש להזין שם מלא');
  const cleanPhone = normalizePhone(phone);
  if (!cleanPhone) throw new Error('יש להזין מספר טלפון תקין (למשל 0501234567)');

  const store = getKioskStore(event);
  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.displayName = name;
    user.phone = cleanPhone;
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });
}

async function resetPassword(event, { userId, newPassword }) {
  if (!newPassword || String(newPassword).length < 4) throw new Error('הסיסמה חייבת להכיל לפחות 4 תווים');

  const store = getKioskStore(event);
  const { salt, hash } = hashPassword(String(newPassword));

  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.passwordSalt = salt;
    user.passwordHash = hash;
    clearTempPassword(user);
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });
}

// ===================== שחזור סיסמה בטלפון (ימות המשיח) =====================
//
// העיקרון: הסיסמה הזמנית נשמרת בשדות נפרדים (tempPassword*), ולא מחליפה את
// הסיסמה הרגילה. כך, אם מישהו אחר ביקש איפוס (למשל התחזה למספר הטלפון), בעל
// החשבון עדיין נכנס עם הסיסמה הרגילה שלו, והסיסמה הזמנית פשוט פגה.
// כניסה עם הסיסמה הזמנית לא פותחת את הקיוסק - היא מאפשרת רק לקבוע סיסמה חדשה.

const TEMP_PASSWORD_DIGITS = 6;
const TEMP_PASSWORD_TTL_MINUTES = 15;
// כמה ניסיונות כניסה שגויים מותרים בזמן שיש סיסמה זמנית פעילה. אחרי זה היא נמחקת,
// כדי שאי אפשר יהיה לנחש אותה בעמדה (מיליון אפשרויות, אבל בלי הגבלה זה עדיין סיכון).
const TEMP_PASSWORD_MAX_FAILED_LOGINS = 5;

const PHONE_RESET_LIMITS = {
  // ניסיונות כושלים (תעודת זהות לא תואמת) מאותו מספר טלפון, בשעה האחרונה
  failedPerPhonePerHour: 3,
  // סיסמאות זמניות שהונפקו לאותו חשבון, ב-24 השעות האחרונות
  issuedPerUserPerDay: 3,
  // כל הניסיונות (מוצלחים וכושלים) מאותו מספר טלפון, ב-24 השעות האחרונות
  attemptsPerPhonePerDay: 10
};

function clearTempPassword(user) {
  delete user.tempPasswordSalt;
  delete user.tempPasswordHash;
  delete user.tempPasswordExpiresAt;
  delete user.tempFailedAttempts;
}

function hasActiveTempPassword(user, now = Date.now()) {
  return !!(user.tempPasswordHash && user.tempPasswordExpiresAt && Date.parse(user.tempPasswordExpiresAt) > now);
}

function generateTempPassword() {
  // crypto.randomInt - אקראיות קריפטוגרפית, לא Math.random
  let digits = '';
  for (let i = 0; i < TEMP_PASSWORD_DIGITS; i++) digits += String(crypto.randomInt(0, 10));
  return digits;
}

async function readPasswordResetLog(store) {
  return (await store.get(PASSWORD_RESETS_KEY, { type: 'json' })) || [];
}

async function appendPasswordResetLog(event, entry) {
  const store = getKioskStore(event);
  const record = { id: crypto.randomUUID(), timestamp: new Date().toISOString(), ...entry };
  await writeWithRetry(store, PASSWORD_RESETS_KEY, [], (log) => {
    log.push(record);
    if (log.length > MAX_PASSWORD_RESET_LOG) log.splice(0, log.length - MAX_PASSWORD_RESET_LOG);
    return record;
  });
  return record;
}

async function listPasswordResetLog(event) {
  const store = getKioskStore(event);
  const log = await readPasswordResetLog(store);
  return log.slice().sort((a, b) => b.timestamp.localeCompare(a.timestamp));
}

// בודק אם מותר למספר הטלפון הזה (ולחשבון, אם כבר זוהה) לבקש איפוס כרגע.
// מחזיר null אם מותר, או סיבת חסימה ('phone-locked' / 'user-limit').
async function checkPhoneResetLimits(event, { phone, userId }) {
  const store = getKioskStore(event);
  const log = await readPasswordResetLog(store);
  const now = Date.now();
  const since = (ms) => log.filter(e => Date.parse(e.timestamp) > now - ms);
  const lastHour = since(60 * 60 * 1000);
  const lastDay = since(24 * 60 * 60 * 1000);

  const failedThisHour = lastHour.filter(e => e.phone === phone && e.outcome === 'mismatch').length;
  if (failedThisHour >= PHONE_RESET_LIMITS.failedPerPhonePerHour) return 'phone-locked';

  const attemptsToday = lastDay.filter(e => e.phone === phone && (e.outcome === 'mismatch' || e.outcome === 'issued')).length;
  if (attemptsToday >= PHONE_RESET_LIMITS.attemptsPerPhonePerDay) return 'phone-locked';

  if (userId) {
    const issuedToday = lastDay.filter(e => e.userId === userId && e.outcome === 'issued').length;
    if (issuedToday >= PHONE_RESET_LIMITS.issuedPerUserPerDay) return 'user-limit';
  }
  return null;
}

// מחפש חשבון פעיל שתעודת הזהות שלו היא tz והטלפון הרשום בו הוא phone.
// מחזיר את המשתמש (בלי שדות סודיים) או null - בלי לפרט מה בדיוק לא התאים.
async function findUserForPhoneReset(event, { tz, phone }) {
  const cleanTz = normalizeTz(tz);
  if (!cleanTz || !phone) return null;
  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  const user = Object.values(users).find(u => normalizeTz(u.username) === cleanTz);
  if (!user || !user.isActive || user.phone !== phone) return null;
  return toPublicUser(user);
}

// מנפיק סיסמה זמנית ומחזיר אותה (כטקסט גלוי - פעם אחת בלבד, להקראה בטלפון).
// בודק שוב את התאמת הטלפון בתוך הכתיבה עצמה, למקרה שהפרטים השתנו בינתיים.
async function issueTemporaryPassword(event, { userId, phone }) {
  const tempPassword = generateTempPassword();
  const { salt, hash } = hashPassword(tempPassword);
  const expiresAt = new Date(Date.now() + TEMP_PASSWORD_TTL_MINUTES * 60 * 1000).toISOString();

  const store = getKioskStore(event);
  const user = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const u = users[userId];
    if (!u || !u.isActive || u.phone !== phone) throw new Error('החשבון אינו זמין לאיפוס');
    u.tempPasswordSalt = salt;
    u.tempPasswordHash = hash;
    u.tempPasswordExpiresAt = expiresAt;
    u.tempFailedAttempts = 0;
    u.updatedAt = new Date().toISOString();
    return toPublicUser(u);
  });

  return { tempPassword, expiresAt, user, ttlMinutes: TEMP_PASSWORD_TTL_MINUTES };
}

// אימות כניסה בעמדה. מחזיר:
//   { ok: false }                         - פרטים שגויים
//   { ok: true, user, viaTemp: false }    - סיסמה רגילה
//   { ok: true, user, viaTemp: true }     - סיסמה זמנית בתוקף: חובה להחליף סיסמה
// בכישלון כשיש סיסמה זמנית פעילה - סופרים ניסיון שגוי, ואחרי כמה ניסיונות מוחקים אותה.
async function authenticateKioskUser(event, { username, password }) {
  const user = await findUserByUsername(event, username);
  if (!user) return { ok: false };
  const pwd = String(password);

  if (verifyPassword(pwd, user.passwordSalt, user.passwordHash)) {
    return { ok: true, user, viaTemp: false };
  }

  if (!hasActiveTempPassword(user)) return { ok: false };

  if (verifyPassword(pwd, user.tempPasswordSalt, user.tempPasswordHash)) {
    return { ok: true, user, viaTemp: true };
  }

  // ניסיון שגוי בזמן שיש סיסמה זמנית - נספר
  const store = getKioskStore(event);
  const burned = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const u = users[user.id];
    if (!u || !hasActiveTempPassword(u)) return false;
    u.tempFailedAttempts = (u.tempFailedAttempts || 0) + 1;
    if (u.tempFailedAttempts >= TEMP_PASSWORD_MAX_FAILED_LOGINS) {
      clearTempPassword(u);
      return true;
    }
    return false;
  });
  if (burned) {
    await appendPasswordResetLog(event, {
      userId: user.id, username: user.username, phone: user.phone,
      outcome: 'temp-burned', note: `הסיסמה הזמנית בוטלה אחרי ${TEMP_PASSWORD_MAX_FAILED_LOGINS} ניסיונות כניסה שגויים`
    }).catch(() => {});
  }
  return { ok: false };
}

// החלפת סיסמה ע"י המשתמש עצמו בעמדה (אחרי כניסה עם סיסמה זמנית, או בכלל).
// האימות של הסיסמה הנוכחית נעשה בתוך הכתיבה, כך שאין חלון זמן בין בדיקה לשינוי.
async function changeOwnPassword(event, { username, currentPassword, newPassword }) {
  const next = String(newPassword || '');
  if (next.length < 4) throw new Error('הסיסמה החדשה חייבת להכיל לפחות 4 תווים');

  const existing = await findUserByUsername(event, username);
  if (!existing) throw new Error('שם משתמש או סיסמה שגויים');

  const { salt, hash } = hashPassword(next);
  const store = getKioskStore(event);
  let usedTemp = false;

  const user = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const u = users[existing.id];
    if (!u) throw new Error('שם משתמש או סיסמה שגויים');
    const cur = String(currentPassword || '');
    const viaMain = verifyPassword(cur, u.passwordSalt, u.passwordHash);
    const viaTemp = !viaMain && hasActiveTempPassword(u) && verifyPassword(cur, u.tempPasswordSalt, u.tempPasswordHash);
    if (!viaMain && !viaTemp) throw new Error('הסיסמה הזמנית שגויה או שפג תוקפה - יש להתקשר שוב לקבלת סיסמה חדשה');
    if (!u.isActive) throw new Error('החשבון מושבת - יש לפנות לצוות הספרייה');
    if (next === cur) throw new Error('הסיסמה החדשה חייבת להיות שונה מהסיסמה הזמנית');
    usedTemp = viaTemp;
    u.passwordSalt = salt;
    u.passwordHash = hash;
    clearTempPassword(u);
    u.updatedAt = new Date().toISOString();
    return toPublicUser(u);
  });

  if (usedTemp) {
    await appendPasswordResetLog(event, {
      userId: user.id, username: user.username, phone: user.phone,
      outcome: 'completed', note: 'המשתמש קבע סיסמה חדשה בעמדה'
    }).catch(() => {});
  }
  return user;
}

async function setUserActive(event, { userId, isActive }) {
  const store = getKioskStore(event);
  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.isActive = !!isActive;
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });
}

// הרשאה לצאת מהקיוסק לשולחן העבודה של Windows (כפתור "יציאה למחשב" בעמדה).
// רק מנהל באתר יכול להעניק אותה - משתמש קיוסק לא יכול לשנות אותה לעצמו.
async function setUserCanExit(event, { userId, canExitKiosk }) {
  const store = getKioskStore(event);
  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.canExitKiosk = !!canExitKiosk;
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });
}

async function deleteUser(event, { userId }) {
  const store = getKioskStore(event);
  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    if (!users[userId]) throw new Error('משתמש לא נמצא');
    delete users[userId];
    return { deleted: true };
  });
}

async function appendTransaction(event, entry) {
  const store = getKioskStore(event);
  const record = {
    id: crypto.randomUUID(),
    timestamp: new Date().toISOString(),
    ...entry
  };
  await writeWithRetry(store, TRANSACTIONS_KEY, [], (transactions) => {
    transactions.push(record);
    return record;
  });
  return record;
}

// הוספת יתרה ידנית ע"י איש צוות (למשל תשלום במזומן בדלפק) - מעדכן את היתרה
// ורושם עסקה מסוג topup, שניהם באותה קריאה כדי למנוע מצב של יתרה מעודכנת
// בלי רישום מקביל בהיסטוריה (או להפך).
async function addManualBalance(event, { userId, amount, description, performedBy }) {
  const numericAmount = Number(amount);
  if (!Number.isFinite(numericAmount) || numericAmount <= 0) {
    throw new Error('סכום הטעינה חייב להיות מספר חיובי');
  }

  const store = getKioskStore(event);
  const updatedUser = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.balance = Number((user.balance + numericAmount).toFixed(2));
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });

  const transaction = await appendTransaction(event, {
    userId,
    type: 'topup',
    source: 'manual',
    amount: numericAmount,
    description: description || 'הטענת כסף ידנית ע"י הצוות',
    performedBy: performedBy || null
  });

  return { user: updatedUser, transaction };
}

// חיוב הדפסה: בודק יתרה ומחייב באותה כתיבה אטומית (לא "בדוק ואז חייב" בשני
// שלבים נפרדים) - כדי שלא יהיה חלון זמן שבו שתי בקשות חיוב מקבילות לאותו
// משתמש יעברו את הבדיקה שתיהן על סמך אותה יתרה ישנה. במקרה של יתרה לא
// מספיקה זורקים שגיאה מסומנת (err.insufficientFunds) לפני כל כתיבה בפועל -
// כך שגם לא מתבזבזת כתיבה מיותרת על "אין שינוי".
async function chargeForPrint(event, { userId, pages }) {
  const numericPages = Number(pages);
  if (!Number.isInteger(numericPages) || numericPages <= 0) {
    throw new Error('מספר עמודים לא תקין');
  }

  const config = await getConfig(event);
  const cost = Number((numericPages * config.pricePerPage).toFixed(2));

  const store = getKioskStore(event);
  const chargedUser = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    if (!user.isActive) throw new Error('החשבון מושבת');
    if (user.balance < cost) {
      const err = new Error('היתרה אינה מספיקה לביצוע ההדפסה');
      err.insufficientFunds = true;
      err.balance = user.balance;
      err.cost = cost;
      throw err;
    }
    user.balance = Number((user.balance - cost).toFixed(2));
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });

  const transaction = await appendTransaction(event, {
    userId,
    type: 'charge',
    source: 'print',
    amount: cost,
    description: `הדפסת ${numericPages} עמודים`,
    pages: numericPages
  });

  return { balance: chargedUser.balance, cost, transaction };
}

// החזר על הדפסה שנכשלה (מדפסת מנותקת, נגמר נייר וכו'): עמדת הקיוסק מחייבת מראש
// ברגע שעבודת ההדפסה נכנסת לתור, ואם העבודה נכשלה ובוטלה - מבקשת כאן החזר.
// ההחזר מותר פעם אחת בלבד לכל חיוב (מסומן על עסקת החיוב עצמה), רק על חיוב
// הדפסה של אותו משתמש, ובדיוק בסכום שחויב - כך שאי אפשר "לייצר" כסף דרכו.
async function refundPrintCharge(event, { userId, transactionId }) {
  const store = getKioskStore(event);

  // אם הקריאה עדיין לא "רואה" חיוב שנרשם לפני רגע (מטמון) - ממתינים ומנסים שוב
  let charge;
  for (let attempt = 0; ; attempt++) {
    try {
      charge = await markChargeRefunded(store, { userId, transactionId });
      break;
    } catch (err) {
      if (!err.chargeNotFound || attempt >= 5) throw err;
      await new Promise(resolve => setTimeout(resolve, 500 * (attempt + 1)));
    }
  }

  const updatedUser = await writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.balance = Number((user.balance + charge.amount).toFixed(2));
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });

  const transaction = await appendTransaction(event, {
    userId,
    type: 'topup',
    source: 'print-refund',
    amount: charge.amount,
    description: `החזר על הדפסה שנכשלה (${charge.pages || 1} עמודים)`,
    refundOf: transactionId
  });

  return { balance: updatedUser.balance, amount: charge.amount, transaction };
}

function markChargeRefunded(store, { userId, transactionId }) {
  return writeWithRetry(store, TRANSACTIONS_KEY, [], (transactions) => {
    const tx = transactions.find(t => t.id === transactionId);
    if (!tx || tx.userId !== userId || tx.type !== 'charge' || tx.source !== 'print') {
      const err = new Error('עסקת החיוב לא נמצאה');
      err.chargeNotFound = true;
      throw err;
    }
    if (tx.refunded) {
      const err = new Error('החיוב הזה כבר הוחזר');
      err.alreadyRefunded = true;
      throw err;
    }
    tx.refunded = true;
    tx.refundedAt = new Date().toISOString();
    return { amount: tx.amount, pages: tx.pages };
  });
}

// תעריף עמוד הדפסה ורשימת התוכנות המאושרות - נקרא ע"י עמדת הקיוסק (לבניית
// מסך בחירת התוכנה ולחישוב עלות הדפסה) וע"י פאנל הניהול (לעריכה).
async function getConfig(event) {
  const store = getKioskStore(event);
  const config = (await store.get(CONFIG_KEY, { type: 'json' })) || {};
  return { ...DEFAULT_CONFIG, ...config };
}

// עדכון מלא: allowedApps (אם נשלח) מחליף את כל הרשימה הקיימת, לא ממזג איתה -
// כך שמסך הניהול פשוט שולח בחזרה את הרשימה השלמה אחרי הוספה/הסרה/סידור מחדש.
async function updateConfig(event, patch) {
  if (patch.pricePerPage !== undefined) {
    const price = Number(patch.pricePerPage);
    if (!Number.isFinite(price) || price < 0) throw new Error('תעריף עמוד ההדפסה חייב להיות מספר לא שלילי');
  }
  if (patch.allowedApps !== undefined && !Array.isArray(patch.allowedApps)) {
    throw new Error('רשימת התוכנות המאושרות חייבת להיות מערך');
  }

  const store = getKioskStore(event);
  return writeWithRetry(store, CONFIG_KEY, { ...DEFAULT_CONFIG }, (config) => {
    if (patch.pricePerPage !== undefined) config.pricePerPage = Number(patch.pricePerPage);
    if (patch.allowedApps !== undefined) config.allowedApps = patch.allowedApps;
    if (patch.textEditorEnabled !== undefined) config.textEditorEnabled = !!patch.textEditorEnabled;
    config.updatedAt = new Date().toISOString();
    return { ...DEFAULT_CONFIG, ...config };
  });
}

// הטענת כסף מקוונת מול נדרים פלוס: זרימת "אישור שרתי בלבד" (ראו תיעוד ה-API,
// "אימות תשלום ואבטחה") - לעולם לא סומכים על מה שהדפדפן/העמדה "מספרים" לנו
// שהתשלום הצליח, אלא רק על ה-CallBack שמגיע ישירות משרתי נדרים פלוס.
//
// pending-topups.json הוא מקור האמת לזיהוי "לאיזה משתמש ואיזה סכום מיועד
// אסימון (token) ששלחנו ב-Param1" - כי Param1 עצמו לא נשמר אצל נדרים פלוס
// (ראו התיעוד: "טקסט חופשי מיועד לקאלבק בלבד, לא נשמר").
//
// שני מסמכים נפרדים (pending-topups.json, users.json) לא יכולים להיכתב
// כטרנזקציה אחת אמיתית ב-Blobs - לכן מטפלים בזה בשני שלבים מסודרים:
// 1) "תפיסת" האסימון (pending -> processing) באופן אטומי, כדי שקריאת קאלבק
//    כפולה (או race) לא תזכה את היתרה פעמיים.
// 2) זיכוי היתרה בפועל, ואז סימון האסימון כ-confirmed. אם שלב 2 נכשל
//    (למשל המשתמש נמחק בינתיים), מסמנים failed במקום להשאיר תקוע ב-processing
//    לצמיתות - מקרה קצה נדיר שדורש בדיקה ידנית, לא זיכוי אוטומטי כפול.
async function createPendingTopup(event, { userId, amount }) {
  const numericAmount = Number(amount);
  if (!Number.isFinite(numericAmount) || numericAmount <= 0) {
    throw new Error('סכום ההטענה חייב להיות מספר חיובי');
  }

  const store = getKioskStore(event);
  const users = (await store.get(USERS_KEY, { type: 'json' })) || {};
  if (!users[userId]) throw new Error('משתמש לא נמצא');

  const token = crypto.randomUUID();
  return writeWithRetry(store, PENDING_TOPUPS_KEY, {}, (pending) => {
    pending[token] = { token, userId, amount: numericAmount, status: 'pending', createdAt: new Date().toISOString() };
    return { token, amount: numericAmount };
  });
}

// הרשומה "תשלום ממתין" נוצרת שניות ספורות לפני שהאישור מנדרים פלוס מגיע, והקריאה
// עלולה לבוא מהמטמון ולא לראות אותה עדיין. במקום לדחות מיד - ממתינים ומנסים שוב.
async function retryWhileTokenUnknown(fn) {
  const delaysMs = [400, 800, 1200, 1600, 2000]; // סה"כ כ-6 שניות - בתוך מגבלת 10 השניות של פונקציה
  for (let attempt = 0; ; attempt++) {
    try {
      return await fn();
    } catch (err) {
      if (!err.unknownToken || attempt >= delaysMs.length) throw err;
      await new Promise(resolve => setTimeout(resolve, delaysMs[attempt]));
    }
  }
}

function unknownTokenError() {
  const err = new Error('אסימון תשלום לא מוכר');
  err.unknownToken = true;
  return err;
}

async function claimPendingTopupForProcessing(event, token) {
  const store = getKioskStore(event);
  return retryWhileTokenUnknown(() => writeWithRetry(store, PENDING_TOPUPS_KEY, {}, (pending) => {
    const entry = pending[token];
    if (!entry) throw unknownTokenError();
    if (entry.status !== 'pending') {
      return { alreadyHandled: true, entry: { ...entry } };
    }
    entry.status = 'processing';
    return { alreadyHandled: false, entry: { ...entry } };
  }));
}

// שמירת אישור תשלום שלא שויך לחשבון, לטיפול ידני (ראו ORPHAN_TOPUPS_KEY)
async function recordOrphanTopup(event, details) {
  const store = getKioskStore(event);
  await writeWithRetry(store, ORPHAN_TOPUPS_KEY, [], (orphans) => {
    orphans.push({ ...details, receivedAt: new Date().toISOString() });
    return null;
  });
}

async function markPendingTopupOutcome(event, token, patch) {
  const store = getKioskStore(event);
  await retryWhileTokenUnknown(() => writeWithRetry(store, PENDING_TOPUPS_KEY, {}, (pending) => {
    const entry = pending[token];
    if (!entry) throw unknownTokenError();
    Object.assign(entry, patch);
    return entry;
  }));
}

// נקרא ע"י ה-CallBack בלבד, אחרי אימות ה-IP מול נדרים פלוס. הסכום שמזוכה הוא
// הסכום שנדרים פלוס בפועל מדווחת עליו (לא מה שביקשנו ביצירת האסימון) - כך
// שאם משום מה יש פער, היתרה תמיד תשקף את מה שבאמת חויב בכרטיס האשראי.
async function confirmPendingTopup(event, { token, nedarimAmount, nedarimTransactionId, confirmation, lastNum, raw }) {
  // זיכוי רק על עסקה שאושרה בפועל: לעסקת אשראי מאושרת יש תמיד מספר אישור.
  // הודעה בלי מספר אישור (למשל עסקה שנדחתה) לא מזכה - והקוראת שומרת אותה לבדיקה ידנית.
  if (!confirmation || !String(confirmation).trim()) {
    throw new Error('התקבלה הודעה מנדרים פלוס ללא מספר אישור - לא זוכה (ייתכן שהעסקה נדחתה)');
  }

  const claim = await claimPendingTopupForProcessing(event, token);
  if (claim.alreadyHandled) {
    return { duplicate: true, status: claim.entry.status };
  }

  const creditAmount = Number(nedarimAmount) > 0 ? Number(nedarimAmount) : claim.entry.amount;

  try {
    const store = getKioskStore(event);
    const updatedUser = await writeWithRetry(store, USERS_KEY, {}, (users) => {
      const user = users[claim.entry.userId];
      if (!user) throw new Error('משתמש לא נמצא (נמחק בזמן התשלום)');
      user.balance = Number((user.balance + creditAmount).toFixed(2));
      user.updatedAt = new Date().toISOString();
      return toPublicUser(user);
    });

    const transaction = await appendTransaction(event, {
      userId: claim.entry.userId,
      type: 'topup',
      source: 'nedarim',
      amount: creditAmount,
      description: 'הטענת כסף באשראי דרך נדרים פלוס',
      nedarimTransactionId: nedarimTransactionId || null,
      confirmation: confirmation || null,
      nedarimRaw: raw || null
    });

    await markPendingTopupOutcome(event, token, {
      status: 'confirmed',
      confirmedAt: new Date().toISOString(),
      nedarimTransactionId: nedarimTransactionId || null,
      confirmation: confirmation || null,
      lastNum: lastNum || null
    });

    return { duplicate: false, user: updatedUser, transaction };
  } catch (err) {
    await markPendingTopupOutcome(event, token, { status: 'failed', failReason: err.message }).catch(() => {});
    throw err;
  }
}

async function getPendingTopupStatus(event, token) {
  const store = getKioskStore(event);
  const pending = (await store.get(PENDING_TOPUPS_KEY, { type: 'json' })) || {};
  const entry = pending[token];
  // מהמטמון ייתכן שהרשומה עוד לא נראית - מבחינת העמדה זה פשוט "עדיין ממתין"
  if (!entry) return { status: 'pending', amount: 0, confirmedAt: null };
  return { status: entry.status, amount: entry.amount, confirmedAt: entry.confirmedAt || null };
}

async function listUserTransactions(event, userId) {
  const store = getKioskStore(event);
  const transactions = (await store.get(TRANSACTIONS_KEY, { type: 'json' })) || [];
  return transactions
    .filter(t => t.userId === userId)
    .sort((a, b) => b.timestamp.localeCompare(a.timestamp));
}

module.exports = {
  getStoreMode,
  hashPassword,
  verifyPassword,
  toPublicUser,
  listUsers,
  getUserById,
  findUserByUsername,
  createUser,
  resetPassword,
  normalizeCallerPhone,
  findUserForPhoneReset,
  checkPhoneResetLimits,
  issueTemporaryPassword,
  appendPasswordResetLog,
  listPasswordResetLog,
  authenticateKioskUser,
  changeOwnPassword,
  setUserDetails,
  setUserCanExit,
  setUserActive,
  deleteUser,
  addManualBalance,
  appendTransaction,
  listUserTransactions,
  getConfig,
  updateConfig,
  chargeForPrint,
  refundPrintCharge,
  createPendingTopup,
  confirmPendingTopup,
  recordOrphanTopup,
  getPendingTopupStatus
};
