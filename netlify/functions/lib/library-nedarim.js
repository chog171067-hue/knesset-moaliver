// אינטגרציית נדרים פלוס ייעודית להטענת כסף בקיוסק הספרייה - מוסד/מפתח נפרדים
// מ-lib/nedarim.js (ששייך לתרומות בית הכנסת), כי ה-ApiValid כאן הוא "טקסט
// אימות" לביצוע עסקאות (מסך מפתחות API), לא ApiPassword למשיכת דוחות.
// אם בפועל הספרייה תשתמש באותו מספר מוסד כמו בית הכנסת - אפשר להצביע את
// LIBRARY_NEDARIM_MOSAD על אותו מספר; זו עדיין הגדרת סביבה נפרדת בכוונה.
//
// ⚠️ לפני שימוש בפועל: יש לוודא מול נדרים פלוס (office@nedar.im) שהשימוש
// הזה (הקמת עסקה בצד שרת + אייפרם בתוך אפליקציית Windows) מאושר, ולקבל את
// טקסט האימות (ApiValid) הרלוונטי - ראו סעיף 3.5 במסמך האפיון.
function getLibraryNedarimCredentials() {
  const mosad = process.env.LIBRARY_NEDARIM_MOSAD;
  const apiValid = process.env.LIBRARY_NEDARIM_API_VALID;
  if (!mosad || !apiValid) {
    throw new Error('שגיאת שרת: פרטי ההתחברות לנדרים פלוס עבור הקיוסק אינם מוגדרים');
  }
  return { mosad, apiValid };
}

// יוצר עסקה בצד השרת מול נדרים פלוס (DebitIframe.aspx?Action=CreateTransaction) -
// הסכום נקבע כאן, בשרת, ולא ניתן לשינוי מצד הלקוח באייפרם עצמו. מחזיר את
// מזהה העסקה (ID) שיש להעביר לאייפרם בהודעת FinishTransaction.
async function createServerTransaction({ amount, param1, callbackUrl }) {
  const { mosad, apiValid } = getLibraryNedarimCredentials();

  const body = new URLSearchParams({
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
  });

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
