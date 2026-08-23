// טעינת פרופיל ערב ההתרמה (שם וסף סכום) עבור control.html ו-display.html כאחד.
// הנתונים מגיעים מ-get-donation-config.js (ציבורי, לפי מזהה event בקישור)
// ואינם ניתנים לעריכה מתוך שני הדפים האלה - רק דרך טאב "ערב התרמה" בדף הניהול
// הראשי של האתר (/admin.html). שימו לב: הלוגו/סטריפ *לא* חלק מהתשובה כאן בכלל -
// display.html טוען אותם בנפרד, ישירות מהמחשב של המשתמש (ראו שם למה).
//
// הכלל: קודם מנסים רשת (כדי לקבל עדכון, אם המנהל שינה משהו), ורק אם זה נכשל
// (אין אינטרנט) נופלים חזרה להעתק המקומי ששמור מהפעם הקודמת שהדף נטען בהצלחה.
// כך העמדה עובדת בלי שום רשת לאורך כל האירוע עצמו, כל עוד היא נפתחה בהצלחה
// לפחות פעם אחת קודם לכן כשהיה חיבור (למשל בתרגול לפני האירוע).
const DONATION_EVENT_ID_KEY = 'donationAppEventId';

function donationConfigCacheKey(id) {
  return 'donationAppConfig_' + id;
}

async function loadDonationConfig() {
  const params = new URLSearchParams(location.search);
  const urlId = params.get('event');
  if (urlId) {
    localStorage.setItem(DONATION_EVENT_ID_KEY, urlId);
    history.replaceState(null, '', location.pathname);
  }

  const id = urlId || localStorage.getItem(DONATION_EVENT_ID_KEY);
  if (!id) return null;

  try {
    const res = await fetch('/.netlify/functions/get-donation-config?id=' + encodeURIComponent(id), { cache: 'no-store' });
    if (res.ok) {
      const data = await res.json();
      if (data.success) {
        localStorage.setItem(donationConfigCacheKey(id), JSON.stringify(data.config));
        return data.config;
      }
    }
  } catch (e) {
    // אין רשת / שגיאת רשת - ננסה את ההעתק המקומי למטה
  }

  const cached = localStorage.getItem(donationConfigCacheKey(id));
  return cached ? JSON.parse(cached) : null;
}

// לכידת אירוע ההתקנה כאפליקציה נפרדת (רק כרום/edge תומכים) - כדי שנוכל להציג
// כפתור "התקן כאפליקציה" גלוי בדף עצמו במקום לסמוך על כך שמישהו ישים לב לסמל
// הקטן בשורת הכתובת. זו בדיוק הדרך לתת לכל דף סמל נפרד בשורת המשימות.
//
// חשוב: הדפדפן לא תמיד מפעיל את האירוע הזה - יש לו קריטריוני "מעורבות" פנימיים
// (בין השאר: בביקור ממש ראשון באתר, או אחרי שהמשתמש כבר סגר הצעת התקנה בעבר)
// שאתר לא יכול לעקוף. לכן אי אפשר להבטיח שהכפתור תמיד יופיע - אבל כן אפשר
// להבטיח שתמיד תהיה דרך פעילה להתקין: אם אחרי כמה שניות האירוע לא הופעל,
// מוצגת במקום הכפתור הנחיה קבועה להתקנה ידנית דרך תפריט הדפדפן.
let deferredInstallPrompt = null;

function isRunningStandalone() {
  return window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
}

function showInstallButton() {
  document.querySelectorAll('.installAppBtn').forEach(btn => { btn.style.display = 'inline-block'; });
  document.querySelectorAll('.installFallbackHint').forEach(hint => { hint.style.display = 'none'; });
}

function hideInstallUi() {
  document.querySelectorAll('.installAppBtn').forEach(btn => { btn.style.display = 'none'; });
  document.querySelectorAll('.installFallbackHint').forEach(hint => { hint.style.display = 'none'; });
}

window.addEventListener('beforeinstallprompt', (e) => {
  e.preventDefault();
  deferredInstallPrompt = e;
  showInstallButton();
});

window.addEventListener('appinstalled', hideInstallUi);

async function triggerInstallPrompt() {
  if (!deferredInstallPrompt) return;
  deferredInstallPrompt.prompt();
  await deferredInstallPrompt.userChoice;
  deferredInstallPrompt = null;
  hideInstallUi();
}

if (!isRunningStandalone()) {
  setTimeout(() => {
    if (!deferredInstallPrompt) {
      document.querySelectorAll('.installFallbackHint').forEach(hint => { hint.style.display = 'block'; });
    }
  }, 2500);
}

// חשוב: לא בולעים כאן שגיאות בשקט - אם ה-Service Worker לא נרשם/מופעל בהצלחה,
// העמדה *תיראה* תקינה כשיש רשת אבל תיכשל לגמרי כשאין (כי אין מי שיגיש את
// control.html/display.html עצמם מהמטמון). לכן כל שלב מדווח למסוף (F12 → Console)
// כדי שאפשר יהיה לאבחן בעיה בלי ניחושים.
if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('sw.js').then(reg => {
    console.log('[donation-app] Service Worker נרשם, scope:', reg.scope);
    reg.addEventListener('updatefound', () => {
      const installing = reg.installing;
      if (!installing) return;
      installing.addEventListener('statechange', () => {
        console.log('[donation-app] מצב Service Worker:', installing.state);
      });
    });
  }).catch(err => {
    console.error('[donation-app] רישום Service Worker נכשל - העמדה לא תעבוד בלי רשת:', err);
  });
} else {
  console.error('[donation-app] הדפדפן הזה לא תומך ב-Service Worker - העמדה לא תעבוד בלי רשת.');
}
