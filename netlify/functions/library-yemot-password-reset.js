// שחזור סיסמה לקיוסק הספרייה בטלפון - שלוחת API במערכת ימות המשיח.
//
// איך זה עובד: ימות המשיח קוראת לכתובת הזו בכל שלב בשיחה, ומעבירה את מספר
// המתקשר (ApiPhone), מזהה השיחה (ApiCallId) וכל מה שהמתקשר הקיש עד עכשיו בשיחה
// הזו (כאן: tz ואז confirm). אנחנו מחזירים שורת טקסט עם ההוראה הבאה:
// מה להקריא, מה לבקש להקיש, או לנתק.
//
// שלבי השיחה:
//   1. אין tz       -> מבקשים להקיש תעודת זהות
//   2. יש tz        -> בודקים התאמה (ת"ז + הטלפון הרשום = המספר המתקשר).
//                      לא מתאים -> הודעה כללית וניתוק. מתאים -> מבקשים אישור
//   3. confirm = 1  -> מנפיקים סיסמה זמנית, מקריאים אותה 3 פעמים ומנתקים
//
// אבטחה:
//   - הכתובת ציבורית, ולכן כל בקשה חייבת לכלול key = YEMOT_RESET_KEY (משתנה סביבה
//     בנטליפיי). מגדירים אותו בשלוחה בימות: api_add_0=key=הערך. בלי זה, כל אחד
//     היה יכול "להתחזות" לימות ולשלוח ApiPhone מזויף.
//   - הגבלת ניסיונות לפי מספר טלפון ולפי חשבון (ראו PHONE_RESET_LIMITS ב-kiosk-store).
//   - כל ניסיון נרשם ביומן password-resets.json (מוצג בדף הניהול).
//   - הסיסמה הזמנית בתוקף 15 דקות, ולא מחליפה את הסיסמה הרגילה.
const crypto = require('crypto');
const querystring = require('querystring');
const {
  normalizeCallerPhone,
  findUserForPhoneReset,
  checkPhoneResetLimits,
  issueTemporaryPassword,
  appendPasswordResetLog
} = require('./lib/kiosk-store');

// ---- בניית תשובות בפורמט של ימות המשיח ----

// בטקסט להקראה אסור להשתמש בתווים שיש להם משמעות בפורמט של ימות
// (נקודה מפרידה בין קטעים, פסיק/שווה/& מפרידים בין שדות ופקודות)
function say(text) {
  return 't-' + String(text).replace(/[.,=&"'\-]/g, ' ').replace(/\s+/g, ' ').trim();
}

function digits(value) {
  return 'd-' + String(value).replace(/\D/g, '');
}

// הקראת הודעה (כמה קטעים) ואז ניתוק
function messageAndHangup(...parts) {
  return `id_list_message=${parts.join('.')}&go_to_folder=hangup`;
}

// בקשת הקשה. השדות אחרי שם המשתנה, לפי הסדר:
//   האם להשתמש בערך קיים (no = תמיד לשאול), מקסימום ספרות, מינימום ספרות,
//   שניות המתנה, אופן השמעת ההקשה לאישור (Digits = מקריא ומבקש אישור, No = בלי),
//   חסימת כוכבית, חסימת אפס
function askDigits(promptParts, varName, { max, min, playback, blockZero }) {
  return `read=${promptParts.join('.')}=${varName},no,${max},${min},10,${playback},yes,${blockZero ? 'yes' : 'no'}`;
}

function textResponse(body, statusCode = 200) {
  return { statusCode, headers: { 'Content-Type': 'text/plain; charset=utf-8' }, body };
}

// ---- פרמטרים ----

function readParams(event) {
  const params = { ...(event.queryStringParameters || {}) };
  // ימות יכולה לשלוח גם ב-POST (application/x-www-form-urlencoded)
  if (event.httpMethod === 'POST' && event.body) {
    const raw = event.isBase64Encoded ? Buffer.from(event.body, 'base64').toString('utf8') : event.body;
    Object.assign(params, querystring.parse(raw));
  }
  return params;
}

function isValidKey(provided) {
  const expected = process.env.YEMOT_RESET_KEY;
  if (!expected || !provided) return false;
  const a = Buffer.from(String(provided));
  const b = Buffer.from(expected);
  return a.length === b.length && crypto.timingSafeEqual(a, b);
}

// ביומן לא שומרים תעודת זהות מלאה שהוקשה (אולי של מישהו אחר) - רק 3 ספרות אחרונות
function maskTz(tz) {
  const d = String(tz || '').replace(/\D/g, '');
  return d ? '*'.repeat(Math.max(0, d.length - 3)) + d.slice(-3) : '';
}

const MSG_CONTACT_STAFF = say('לבירורים ניתן לפנות לגבאים');

exports.handler = async function (event) {
  const p = readParams(event);

  if (!isValidKey(p.key)) {
    return textResponse('Forbidden', 403);
  }

  // המתקשר ניתק - ימות מודיעה על כך, אין מה להחזיר
  if (p.hangup === 'yes') return textResponse('');

  const callId = p.ApiCallId || null;
  const phone = normalizeCallerPhone(p.ApiPhone);
  const log = (entry) => appendPasswordResetLog(event, { callId, phone: phone || String(p.ApiPhone || ''), ...entry }).catch(() => {});

  try {
    if (!phone) {
      return textResponse(messageAndHangup(
        say('לא ניתן לזהות את מספר הטלפון שלך'),
        say('יש להתקשר מהטלפון הרשום בחשבון הקיוסק כשהמספר מזוהה'),
        MSG_CONTACT_STAFF
      ));
    }

    // שלב 1: בקשת תעודת זהות
    if (!p.tz) {
      if (await checkPhoneResetLimits(event, { phone })) {
        await log({ outcome: 'blocked', note: 'חריגה ממספר הניסיונות למספר הטלפון' });
        return textResponse(messageAndHangup(say('בוצעו יותר מדי ניסיונות מהמספר הזה'), say('נא לנסות שוב מאוחר יותר'), MSG_CONTACT_STAFF));
      }
      return textResponse(askDigits(
        [say('שלום'), say('הגעתם למערכת שחזור הסיסמה של קיוסק הספרייה'), say('נא להקיש את מספר תעודת הזהות ובסיומו סולמית')],
        'tz', { max: 9, min: 5, playback: 'Digits', blockZero: false }
      ));
    }

    // שלב 2: בדיקת התאמה. בודקים שוב את המגבלה, כי כל שלב הוא בקשה נפרדת
    if (await checkPhoneResetLimits(event, { phone })) {
      await log({ outcome: 'blocked', tzEntered: maskTz(p.tz), note: 'חריגה ממספר הניסיונות למספר הטלפון' });
      return textResponse(messageAndHangup(say('בוצעו יותר מדי ניסיונות מהמספר הזה'), say('נא לנסות שוב מאוחר יותר'), MSG_CONTACT_STAFF));
    }

    const user = await findUserForPhoneReset(event, { tz: p.tz, phone });
    if (!user) {
      // הודעה כללית בכוונה: לא מגלים אם תעודת הזהות קיימת, או שרק הטלפון לא תואם
      await log({ outcome: 'mismatch', tzEntered: maskTz(p.tz), note: 'תעודת הזהות והטלפון אינם תואמים לחשבון פעיל' });
      return textResponse(messageAndHangup(
        say('הפרטים שהוקשו אינם תואמים לחשבון פעיל הרשום על מספר הטלפון הזה'),
        MSG_CONTACT_STAFF
      ));
    }

    const userLimit = await checkPhoneResetLimits(event, { phone, userId: user.id });
    if (userLimit) {
      await log({ outcome: 'blocked', userId: user.id, username: user.username, note: 'חריגה ממספר האיפוסים היומי לחשבון' });
      return textResponse(messageAndHangup(say('בוצעו היום יותר מדי איפוסים לחשבון הזה'), say('נא לנסות שוב מחר'), MSG_CONTACT_STAFF));
    }

    if (!p.confirm) {
      return textResponse(askDigits(
        [say('נמצא חשבון מתאים'), say('לאיפוס הסיסמה וקבלת סיסמה זמנית הקישו 1'), say('לביטול הקישו 2')],
        'confirm', { max: 1, min: 1, playback: 'No', blockZero: true }
      ));
    }

    if (p.confirm !== '1') {
      await log({ outcome: 'cancelled', userId: user.id, username: user.username, note: 'המתקשר ביטל' });
      return textResponse(messageAndHangup(say('האיפוס בוטל'), say('הסיסמה הקיימת לא שונתה'), say('להתראות')));
    }

    // שלב 3: הנפקה והקראה
    const issued = await issueTemporaryPassword(event, { userId: user.id, phone });
    await log({ outcome: 'issued', userId: user.id, username: user.username, expiresAt: issued.expiresAt, note: `הונפקה סיסמה זמנית ל-${issued.ttlMinutes} דקות` });

    return textResponse(messageAndHangup(
      say('הסיסמה הזמנית שלך היא'), digits(issued.tempPassword),
      say('אני חוזר'), digits(issued.tempPassword),
      say('ושוב'), digits(issued.tempPassword),
      say(`הסיסמה בתוקף ${issued.ttlMinutes} דקות`),
      say('היכנסו לקיוסק עם תעודת הזהות והסיסמה הזמנית ותתבקשו לבחור סיסמה חדשה'),
      say('הסיסמה הקודמת שלכם ממשיכה לעבוד עד שתחליפו אותה'),
      say('להתראות')
    ));
  } catch (error) {
    await log({ outcome: 'error', note: error.message });
    return textResponse(messageAndHangup(say('אירעה תקלה במערכת'), say('נא לנסות שוב מאוחר יותר'), MSG_CONTACT_STAFF));
  }
};
