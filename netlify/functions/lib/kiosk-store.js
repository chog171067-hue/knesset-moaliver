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
const MAX_WRITE_ATTEMPTS = 5;

const DEFAULT_CONFIG = { pricePerPage: 0.2, allowedApps: [] };

function getKioskStore(event) {
  connectLambda(event);
  return getStore('library-kiosk');
}

// כתיבה בטוחה מול עדכונים מקבילים: קוראים את המסמך הנוכחי (עם ה-ETag שלו),
// מפעילים עליו את מוטציית הקריאה (mutateFn יכולה לזרוק כדי לבטל את הכתיבה -
// למשל "שם משתמש כבר תפוס"), וכותבים בחזרה רק אם ה-ETag לא השתנה בינתיים.
// אם כן השתנה (modified: false) - קוראים מחדש ומנסים שוב, עד MAX_WRITE_ATTEMPTS.
async function writeWithRetry(store, key, defaultValue, mutateFn) {
  for (let attempt = 0; attempt < MAX_WRITE_ATTEMPTS; attempt++) {
    const existing = await store.getWithMetadata(key, { type: 'json' });
    const data = (existing && existing.data) || defaultValue;
    const etag = existing && existing.etag;

    const result = mutateFn(data);

    const writeResult = await store.setJSON(key, data, etag ? { onlyIfMatch: etag } : { onlyIfNew: true });
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

function toPublicUser(user) {
  if (!user) return null;
  const { passwordSalt, passwordHash, ...publicFields } = user;
  return publicFields;
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

async function createUser(event, { username, password, displayName }) {
  const cleanUsername = String(username || '').trim();
  if (!cleanUsername) throw new Error('שם משתמש הוא שדה חובה');
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

async function resetPassword(event, { userId, newPassword }) {
  if (!newPassword || String(newPassword).length < 4) throw new Error('הסיסמה חייבת להכיל לפחות 4 תווים');

  const store = getKioskStore(event);
  const { salt, hash } = hashPassword(String(newPassword));

  return writeWithRetry(store, USERS_KEY, {}, (users) => {
    const user = users[userId];
    if (!user) throw new Error('משתמש לא נמצא');
    user.passwordSalt = salt;
    user.passwordHash = hash;
    user.updatedAt = new Date().toISOString();
    return toPublicUser(user);
  });
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

async function claimPendingTopupForProcessing(event, token) {
  const store = getKioskStore(event);
  return writeWithRetry(store, PENDING_TOPUPS_KEY, {}, (pending) => {
    const entry = pending[token];
    if (!entry) throw new Error('אסימון תשלום לא מוכר');
    if (entry.status !== 'pending') {
      return { alreadyHandled: true, entry: { ...entry } };
    }
    entry.status = 'processing';
    return { alreadyHandled: false, entry: { ...entry } };
  });
}

async function markPendingTopupOutcome(event, token, patch) {
  const store = getKioskStore(event);
  await writeWithRetry(store, PENDING_TOPUPS_KEY, {}, (pending) => {
    const entry = pending[token];
    if (!entry) throw new Error('אסימון תשלום לא מוכר');
    Object.assign(entry, patch);
    return entry;
  });
}

// נקרא ע"י ה-CallBack בלבד, אחרי אימות ה-IP מול נדרים פלוס. הסכום שמזוכה הוא
// הסכום שנדרים פלוס בפועל מדווחת עליו (לא מה שביקשנו ביצירת האסימון) - כך
// שאם משום מה יש פער, היתרה תמיד תשקף את מה שבאמת חויב בכרטיס האשראי.
async function confirmPendingTopup(event, { token, nedarimAmount, nedarimTransactionId, confirmation, lastNum }) {
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
      confirmation: confirmation || null
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
  if (!entry) throw new Error('אסימון תשלום לא מוכר');
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
  hashPassword,
  verifyPassword,
  toPublicUser,
  listUsers,
  getUserById,
  findUserByUsername,
  createUser,
  resetPassword,
  setUserActive,
  deleteUser,
  addManualBalance,
  appendTransaction,
  listUserTransactions,
  getConfig,
  updateConfig,
  chargeForPrint,
  createPendingTopup,
  confirmPendingTopup,
  getPendingTopupStatus
};
