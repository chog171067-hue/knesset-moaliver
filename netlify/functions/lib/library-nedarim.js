// אינטגרציית נדרים פלוס ייעודית להטענת כסף בקיוסק הספרייה - מוסד/מפתח נפרדים
// מ-lib/nedarim.js (ששייך לתרומות בית הכנסת), כי ה-ApiValid כאן הוא "טקסט
// אימות" לביצוע עסקאות (מסך מפתחות API), לא ApiPassword למשיכת דוחות.
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
async function createServerTransaction({ amount, param1, callbackUrl, fullName, tz }) {
  const { mosad, apiValid } = getLibraryNedarimCredentials();
  const { firstName, lastName } = splitFullName(fullName);

  const params = {
    Mosad: mosad,
    ApiValid: apiValid,
    PaymentType: 'Ragil',
    Amount: String(amount),
    Currency: '1',
    Tashlumim: '1',
    Groupe: 'קיוסק ספריה',
    Param1: param1,
    CallBack: callbackUrl,
    AjaxId: `${Date.now()}-${Math.random().toString(36).slice(2)}`
  };
  if (firstName) params.FirstName = firstName;
  if (lastName) params.LastName = lastName;
  if (tz) params.Zeout = tz;

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

module.exports = { getLibraryNedarimCredentials, createServerTransaction };
