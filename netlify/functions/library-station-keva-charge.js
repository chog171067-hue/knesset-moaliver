// עמדת הקיוסק - הטענת יתרה בחיוב בודד מכרטיס האשראי של הוראת קבע פעילה במוסד הראשי.
// החיוב נעשה בשרת (TashlumBodedNew) - העמדה לא רואה שום פרט של הכרטיס מלבד 4 הספרות האחרונות.
// הוראת הקבע נמצאת בשרת לפי הת"ז של החשבון (לא לפי מזהה הוראה שהעמדה שולחת).
//
// גוף הבקשה: { action, userId, amount? } - או בפתיחת חשבון: { action, signup: { tz, displayName, phone, password } }
//   (בפתיחת חשבון הסכום הוא סכום הפתיחה מההגדרות, וההסכמה ניתנת בחלון התשלום עצמו)
//   action 'check'   ← { eligible, last4, consented, maxPerCharge, maxPerDay, chargedToday }
//   action 'consent' ← שמירת ההסכמה החד-פעמית
//   action 'charge'  ← { success, amount, balance } / { success:false, error, uncertain? }
const { requireStation } = require('./lib/station-auth');
const {
  getUserById, normalizeTz, setKevaChargeConsent, setKevaChargeCache, getKevaChargeTodayTotal,
  reserveKevaCharge, completeKevaCharge, failKevaCharge, validateSignupDetails, assertTzAvailable, getConfig,
  KEVA_CHARGE_MAX_PER_CHARGE, KEVA_CHARGE_MAX_PER_DAY
} = require('./lib/kiosk-store');
const { MOSAD_1, getApiPasswordForMosad } = require('./lib/nedarim');
const { KIOSK_NEDARIM_GROUPE } = require('./lib/library-nedarim');
const { isTzBlocked } = require('./lib/community');

const MANAGE_URL = 'https://matara.pro/nedarimplus/Reports/Manage3.aspx';

async function fetchWithTimeout(url, options, ms) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), ms);
  try {
    return await fetch(url, { ...options, signal: controller.signal });
  } finally {
    clearTimeout(timer);
  }
}

// תוקף בפורמט MMYY - האם עדיין בתוקף החודש
function tokefValid(tokef) {
  const t = String(tokef || '').replace(/\D/g, '');
  if (t.length !== 4) return true; // לא ידוע - נדרים יחליטו
  const month = Number(t.slice(0, 2));
  const year = 2000 + Number(t.slice(2));
  const now = new Date();
  return year > now.getFullYear() || (year === now.getFullYear() && month >= now.getMonth() + 1);
}

// הוראת הקבע הפעילה של בעל החשבון במוסד הראשי, עם כרטיס בתוקף (או null)
async function findUserKeva(user, timeoutMs = 7000, diag = {}) {
  // לפי תעודת זהות בלבד (החלטת הגבאים). ההגנה: תקרה נמוכה לחיוב וליום, והסכמה + אישור בכל חיוב.
  const tz = normalizeTz(user.username);
  if (!tz) { diag.reason = 'שם המשתמש של החשבון אינו מספר תעודת זהות'; return null; }

  const params = new URLSearchParams({ Action: 'GetKevaNew', MosadId: MOSAD_1, ApiPassword: getApiPasswordForMosad(MOSAD_1) });
  const res = await fetchWithTimeout(`${MANAGE_URL}?${params}`, {}, timeoutMs);
  if (!res.ok) throw new Error('לא ניתן לבדוק את הוראות הקבע כרגע');
  const json = await res.json();
  const list = Array.isArray(json && json.data) ? json.data : [];

  const byTz = list.filter(k => normalizeTz(String(k.Zeout || '').replace(/\D/g, '')) === tz);
  if (byTz.length === 0) {
    diag.reason = list.length === 0
      ? `נדרים פלוס החזירו רשימת הוראות ריקה${json && json.Message ? ': ' + json.Message : ''}`
      : 'אין הוראת קבע פעילה עם תעודת הזהות הזאת במוסד הראשי';
    return null;
  }
  const withCard = byTz.filter(k => /^\d{4}$/.test(String(k['11'] || '').trim()));
  if (withCard.length === 0) { diag.reason = 'בהוראת הקבע אין כרטיס אשראי (אולי הוראה בנקאית)'; return null; }
  const mine = withCard.filter(k => tokefValid(k['12']));
  if (mine.length === 0) { diag.reason = 'תוקף הכרטיס בהוראת הקבע עבר'; return null; }
  const keva = mine[0];
  return { kevaId: String(keva.DT_RowId || '').replace(/\D/g, ''), last4: String(keva['11']).trim() };
}

async function chargeKeva({ kevaId, amount, token }, timeoutMs) {
  const form = new URLSearchParams({
    Action: 'TashlumBodedNew',
    MosadId: MOSAD_1,
    ApiPassword: getApiPasswordForMosad(MOSAD_1),
    KevaId: kevaId,
    Amount: String(amount),
    Currency: '1',
    Tashlumim: '1',
    Groupe: KIOSK_NEDARIM_GROUPE,
    Comments: `הטענת יתרה בעמדת המחשב - ${token}`,
    JoinToKevaId: 'NoJoin',
    AjaxId: token
  });
  const res = await fetchWithTimeout(MANAGE_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: form.toString()
  }, timeoutMs);
  const text = await res.text();
  try {
    return JSON.parse(text);
  } catch {
    return { Status: 'Error', Message: 'תשובה לא צפויה מנדרים פלוס', unparsed: true };
  }
}

// מבצע את החיוב שכבר נשמר כ"ממתין" ומחזיר את תוצאתו (משותף להטענה ולפתיחת חשבון)
async function runCharge(event, keva, pending, started) {
  let result;
  try {
    result = await chargeKeva({ kevaId: keva.kevaId, amount: pending.amount, token: pending.token },
      Math.max(4000, 8800 - (Date.now() - started)));
  } catch (err) {
    // אין תשובה (timeout / תקשורת) - לא יודעים אם חויב. החיוב נשאר "ממתין": אם בוצע,
    // ה-Webhook יזהה את האסימון וישלים את הטעינה.
    console.error('[keva-charge] no answer:', err.message);
    return { success: false, uncertain: true, error: 'לא התקבלה תשובה מנדרים פלוס' };
  }
  if (String(result.Status || '') !== 'OK') {
    const message = String(result.Message || 'החיוב לא בוצע');
    await failKevaCharge(event, pending.token, message).catch(() => {});
    return { success: false, error: message };
  }
  const done = await completeKevaCharge(event, {
    token: pending.token,
    nedarimAmount: result.Amount || pending.amount,
    nedarimTransactionId: result.TransactionId || result.ID,
    confirmation: result.Confirmation || 'OK',
    lastNum: result.LastNum || keva.last4,
    raw: result
  });
  return { success: true, amount: pending.amount, alreadyCredited: !!done.duplicate };
}

async function handleSignup(event, body, reply) {
  const details = validateSignupDetails(body.signup);
  if (await isTzBlocked(details.username, event)) {
    throw new Error('לא ניתן לפתוח חשבון עם תעודת הזהות הזאת. יש לפנות לגבאים.');
  }
  await assertTzAvailable(event, details.username);
  const config = await getConfig(event);
  const amount = Number(config.signupAmount);
  const person = { username: details.username, phone: details.phone };

  if (body.action === 'check') {
    let keva = null;
    const diag = {};
    try {
      if (amount > 0 && amount <= KEVA_CHARGE_MAX_PER_CHARGE) keva = await findUserKeva(person, 7000, diag);
      else diag.reason = `סכום פתיחת החשבון (${amount} ₪) גבוה מהתקרה לחיוב (${KEVA_CHARGE_MAX_PER_CHARGE} ₪)`;
    } catch (err) {
      diag.reason = 'שגיאה בבדיקה: ' + err.message;
      console.error('[keva-charge] signup check:', err.message);
    }
    return reply(200, { success: true, eligible: !!keva, reason: keva ? null : diag.reason || null, last4: keva ? keva.last4 : null, amount,
      chargedToday: keva ? await getKevaChargeTodayTotal(event, null, keva.kevaId) : 0,
      maxPerCharge: KEVA_CHARGE_MAX_PER_CHARGE, maxPerDay: KEVA_CHARGE_MAX_PER_DAY });
  }

  if (body.action === 'charge') {
    const started = Date.now();
    const keva = await findUserKeva(person, 4000);
    if (!keva) throw new Error('לא נמצאה הוראת קבע פעילה עם כרטיס אשראי בתוקף');
    const pending = await reserveKevaCharge(event, { signupDetails: details, amount, kevaId: keva.kevaId });
    return reply(200, await runCharge(event, keva, pending, started));
  }

  throw new Error('פעולה לא מוכרת');
}

exports.handler = async function (event) {
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };
  const reply = (statusCode, obj) => ({ statusCode, headers, body: JSON.stringify(obj) });

  if (event.httpMethod !== 'POST') return { statusCode: 405, headers, body: 'Method Not Allowed' };
  const auth = requireStation(event);
  if (!auth.authorized) return reply(auth.statusCode, { success: false, error: auth.error });

  try {
    const body = JSON.parse(event.body || '{}');
    if (body.signup) return await handleSignup(event, body, reply);

    const { action, userId, amount } = body;
    if (!userId) throw new Error('חסר מזהה משתמש');
    const user = await getUserById(event, userId);
    if (!user || user.isActive === false) throw new Error('משתמש לא נמצא');

    if (action === 'check') {
      let keva = null;
      const diag = {};
      try {
        keva = await findUserKeva(user, 7000, diag);
        await setKevaChargeCache(event, userId, keva);
      } catch (err) {
        diag.reason = 'שגיאה בבדיקה: ' + err.message;
        console.error('[keva-charge] check:', err.message);
      }
      return reply(200, {
        success: true,
        eligible: !!keva,
        reason: keva ? null : diag.reason || null,
        last4: keva ? keva.last4 : null,
        consented: !!user.kevaChargeConsentAt,
        maxPerCharge: KEVA_CHARGE_MAX_PER_CHARGE,
        maxPerDay: KEVA_CHARGE_MAX_PER_DAY,
        chargedToday: keva ? await getKevaChargeTodayTotal(event, userId) : 0
      });
    }

    if (action === 'consent') {
      await setKevaChargeConsent(event, userId);
      return reply(200, { success: true });
    }

    if (action === 'charge') {
      // מגבלת הזמן של הפונקציה היא 10 שניות: משתמשים בהוראה שנמצאה בבדיקה מלפני פחות מחצי שעה,
      // ורק אם אין כזו - שולפים שוב (בזמן קצר יותר)
      const started = Date.now();
      const cache = user.kevaChargeCache;
      const cacheFresh = cache && cache.kevaId && Date.now() - Date.parse(cache.checkedAt) < 30 * 60 * 1000;
      const keva = cacheFresh ? { kevaId: cache.kevaId, last4: cache.last4 } : await findUserKeva(user, 4000);
      if (!keva) throw new Error('לא נמצאה הוראת קבע פעילה עם כרטיס אשראי בתוקף');

      const pending = await reserveKevaCharge(event, { userId, amount, kevaId: keva.kevaId });
      const outcome = await runCharge(event, keva, pending, started);
      if (outcome.success) {
        const fresh = await getUserById(event, userId);
        outcome.balance = fresh ? fresh.balance : null;
      }
      return reply(200, outcome);
    }

    throw new Error('פעולה לא מוכרת');
  } catch (error) {
    return reply(400, { success: false, error: error.message });
  }
};
