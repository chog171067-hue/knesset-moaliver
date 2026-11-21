// אינטגרציית נדרים פלוס ייעודית להטענת כסף בקיוסק הספרייה - מוסד/מפתח נפרדים
// מ-lib/nedarim.js (ששייך לתרומות בית הכנסת), כי ה-ApiValid כאן הוא "טקסט
// אימות" לביצוע עסקאות (מסך מפתחות API), לא ApiPassword למשיכת דוחות.
// הקטגוריה בנדרים פלוס של כל תשלום שנכנס ליתרה בקיוסק: הטענות מהעמדה, מהאזור האישי
// באתר, מהאזור האישי בנדרים פלוס, ותרומות בעמדות נדרים פלוס / הוראות קבע בקטגוריה הזו
// (את האחרונות מזהה ה-Webhook לפי הקטגוריה - ראו nedarim-webhook.js)
const KIOSK_NEDARIM_GROUPE = 'עמדת מחשב';

function getLibraryNedarimCredentials() {
  const mosad = process.env.LIBRARY_NEDARIM_MOSAD;
  const apiValid = process.env.LIBRARY_NEDARIM_API_VALID;
  if (!mosad || !apiValid) {
    throw new Error('שגיאת שרת: פרטי ההתחברות לנדרים פלוס עבור הקיוסק אינם מוגדרים');
  }
  return { mosad, apiValid };
}

// מפצל שם מלא ("ישראל ישראלי") ל-FirstName/LastName כפי שנדרים פלוס מצפים
// (ראו תיעוד ה-API, Action=CreateTransaction). אם יש רק מילה אחת, היא הופכת
// ל-FirstName והשם משפחה נשאר ריק - עדיף מאשר לשלוח שם ריק לגמרי, שהמוסד
// עלול לדחות אם הוגדר אצלו כשדה חובה.
function splitFullName(fullName) {
  const trimmed = String(fullName || '').trim();
  if (!trimmed) return { firstName: '', lastName: '' };
  const parts = trimmed.split(/\s+/);
  if (parts.length === 1) return { firstName: parts[0], lastName: '' };
  return { firstName: parts.slice(0, -1).join(' '), lastName: parts[parts.length - 1] };
}

// יוצר עסקה בצד השרת מול נדרים פלוס (DebitIframe.aspx?Action=CreateTransaction) -
// הסכום נקבע כאן, בשרת, ולא ניתן לשינוי מצד הלקוח באייפרם עצמו. מחזיר את
// מזהה העסקה (ID) שיש להעביר לאייפרם בהודעת FinishTransaction.
//
// fullName / tz הם אופציונליים, אך מומלץ מאוד לספק אותם: חלק מהמוסדות בנדרים
// פלוס מוגדרים כך שהם דורשים שם משלם (ולפעמים גם תעודת זהות) לפני שמאשרים
// עסקה - ראו התיעוד, Zeout/FirstName/LastName תחת Action=CreateTransaction.
async function createServerTransaction({ amount, param1, callbackUrl, fullName, tz, phone }) {
  const { mosad, apiValid } = getLibraryNedarimCredentials();
  const { firstName, lastName } = splitFullName(fullName);

  const params = {
    Mosad: mosad,
    ApiValid: apiValid,
    PaymentType: 'Ragil',
    Amount: String(amount),
    Currency: '1',
    Tashlumim: '1',
    Groupe: KIOSK_NEDARIM_GROUPE,
    Param1: param1,
    CallBack: callbackUrl,
    AjaxId: `${Date.now()}-${Math.random().toString(36).slice(2)}`
  };
  if (firstName) params.FirstName = firstName;
  if (lastName) params.LastName = lastName;
  // נדרים פלוס דוחה את כל העסקה אם ת"ז אינה ספרות בלבד (למשל משתמש בדיקה בשם "test2"),
  // ולכן שולחים ת"ז רק כשהיא תקינה - היא אופציונלית בכל מקרה.
  if (tz && /^\d{5,9}$/.test(String(tz))) params.Zeout = String(tz);
  if (phone) params.Phone = String(phone).replace(/[-\s]/g, '');

  const body = new URLSearchParams(params);

  const response = await fetch('https://matara.pro/nedarimplus/V6/Files/WebServices/DebitIframe.aspx?Action=CreateTransaction', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: body.toString()
  });

  if (!response.ok) {
    throw new Error(`נדרים פלוס החזיר שגיאה (${response.status}) בהקמת עסקת ההטענה`);
  }

  const data = await response.json();
  if (data.Status !== 'OK' || !data.ID) {
    throw new Error(data.Message || 'נדרים פלוס דחה את הקמת עסקת ההטענה');
  }

  return { transactionId: data.ID };
}

// קישור לדף התשלום הרגיל של נדרים פלוס (לא אייפרם - רק שם עובד האזור האישי עם הכרטיסים
// השמורים). הסכום, הקטגוריה ופרטי המשלם ננעלים; Analytic (האסימון שלנו) נרשם בתחילת
// ההערות של העסקה, ולפיו ה-Webhook יודע לאיזה חשבון להטעין. Redirect - כדי שהעמדה
// תדע מיד שהתשלום הסתיים (היא לא באמת טוענת את הדף הזה, רק מזהה את המעבר אליו).
function buildOnlinePaymentUrl({ amount, token, fullName, tz, phone, redirectUrl }) {
  const mosad = process.env.LIBRARY_NEDARIM_MOSAD;
  if (!mosad) throw new Error('שגיאת שרת: מספר המוסד בנדרים פלוס עבור הקיוסק אינו מוגדר');
  const params = new URLSearchParams({
    mosad,
    Amount: String(amount),
    AmountLock: '1',
    Groupe: KIOSK_NEDARIM_GROUPE,
    GroupeLock: '1',
    OnlyNormal: '1',
    OnlyShekel: '1',
    Analytic: token,
    HideParameters: '1'
  });
  if (fullName) params.set('ClientName', String(fullName));
  if (phone) params.set('Phone', String(phone).replace(/[-\s]/g, ''));
  if (tz && /^\d{5,9}$/.test(String(tz))) params.set('Zeout', String(tz));
  if (redirectUrl) params.set('Redirect', redirectUrl);
  // URLSearchParams מקודד רווח כ-"+", אבל הדף של נדרים פלוס מציג את ה-"+" כמו שהוא
  // ("ישראל+ישראלי") - לכן רווח נשלח כ-%20. ("+" אמיתי בערך מקודד ממילא כ-%2B.)
  return `https://www.matara.pro/nedarimplus/online/?${params.toString().replace(/\+/g, '%20')}`;
}

module.exports = { KIOSK_NEDARIM_GROUPE, getLibraryNedarimCredentials, createServerTransaction, buildOnlinePaymentUrl };
