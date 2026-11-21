// Webhook "עדכוני עסקאות" של נדרים פלוס (הגדרות > API > Webhook במוסד) - נדרים פלוס
// שולחים לכאן הודעה על כל עסקה מוצלחת במוסד. מטפלים רק בעסקאות בקטגוריית הקיוסק
// ("עמדת מחשב"): תשלום מהאזור האישי בנדרים שנפתח מהעמדה, תרומה בעמדת נדרים פלוס,
// וחיוב חודשי של הוראת קבע - וטוענים את הסכום ליתרה (ראו processNedarimWebhookTransaction
// ב-lib/kiosk-store.js). כל שאר העסקאות של המוסד (תרומות לבית הכנסת) - מתעלמים.
//
// אבטחה: חתימת HMAC-SHA256 של נדרים פלוס (מפתח במשתנה NEDARIM_WEBHOOK_SECRET בנטליפיי).
// בלי חתימה תקינה - לא טוענים כלום. אם עדיין לא הוגדר מפתח, נבדקת לפחות כתובת ה-IP.
// תמיד מחזירים 200: נדרים פלוס לא שולחים שוב, ותקלה כאן נשמרת לטיפול ידני.
const crypto = require('crypto');
const { isFromNedarim } = require('./lib/nedarim-security');
const { processNedarimWebhookTransaction, recordOrphanTopup } = require('./lib/kiosk-store');
const { KIOSK_NEDARIM_GROUPE } = require('./lib/library-nedarim');

function header(event, name) {
  const headers = event.headers || {};
  return headers[name] || headers[name.toLowerCase()] || '';
}

function verifySignature(event, rawBody, secret) {
  const ts = String(header(event, 'x-nedarim-timestamp'));
  const sig = String(header(event, 'x-nedarim-signature')).replace(/^v1=/, '');
  if (!ts || !sig) return { ok: false, reason: 'חסרה חתימה' };
  if (Math.abs(Date.now() / 1000 - Number(ts)) > 300) return { ok: false, reason: 'חותמת זמן מחוץ לחלון של 5 דקות' };
  const expected = crypto.createHmac('sha256', secret).update(ts + '.' + rawBody).digest('hex');
  const ok = expected.length === sig.length && crypto.timingSafeEqual(Buffer.from(expected), Buffer.from(sig));
  return ok ? { ok: true } : { ok: false, reason: 'חתימה לא תואמת' };
}

exports.handler = async function (event) {
  if (event.httpMethod !== 'POST') {
    return { statusCode: 405, body: 'Method Not Allowed' };
  }

  // הגוף הגולמי בדיוק כפי שהתקבל - החתימה מחושבת עליו
  const rawBody = event.isBase64Encoded ? Buffer.from(event.body || '', 'base64').toString('utf8') : (event.body || '');

  const secret = process.env.NEDARIM_WEBHOOK_SECRET;
  if (secret) {
    const check = verifySignature(event, rawBody, secret);
    if (!check.ok) {
      console.warn(`[nedarim-webhook] נדחה: ${check.reason} (IP: ${isFromNedarim(event).sourceIp})`);
      return { statusCode: 401, body: 'invalid signature' };
    }
  } else {
    const { sourceIp, verified } = isFromNedarim(event);
    if (!verified) {
      console.warn(`[nedarim-webhook] נדחה: אין מפתח חתימה, וה-IP אינו של נדרים פלוס (${sourceIp})`);
      return { statusCode: 401, body: 'unknown source' };
    }
    console.warn('[nedarim-webhook] אזהרה: NEDARIM_WEBHOOK_SECRET לא מוגדר - האימות לפי IP בלבד');
  }

  let data;
  try {
    data = JSON.parse(rawBody || '{}');
  } catch (e) {
    console.error('[nedarim-webhook] גוף לא תקין');
    return { statusCode: 200, body: 'bad json' };
  }

  // עדכון סירוב / הקמת הוראת קבע (אם הוגדרו לאותה כתובת) - לא רלוונטי לטעינת יתרה
  if (data.Status === 'Error') return { statusCode: 200, body: 'ignored: error update' };

  try {
    const result = await processNedarimWebhookTransaction(event, data, { groupe: KIOSK_NEDARIM_GROUPE });
    if (!String(result.outcome).startsWith('ignored')) {
      console.log(`[nedarim-webhook] עסקה ${data.TransactionId}: ${result.outcome}${result.reason ? ' - ' + result.reason : ''}`);
    }
    return { statusCode: 200, body: JSON.stringify({ ok: true }) };
  } catch (error) {
    console.error('[nedarim-webhook] שגיאה בעיבוד:', error.message);
    try {
      await recordOrphanTopup(event, {
        reason: 'Webhook: ' + error.message,
        amount: data.Amount || null,
        nedarimTransactionId: data.TransactionId || null,
        confirmation: data.Confirmation || null,
        clientName: data.ClientName || null,
        zeout: data.Zeout || null,
        phone: data.Phone || null,
        raw: data
      });
    } catch (recordError) {
      console.error('[nedarim-webhook] גם שמירת התשלום לטיפול ידני נכשלה:', recordError.message);
    }
    return { statusCode: 200, body: 'error logged' };
  }
};
