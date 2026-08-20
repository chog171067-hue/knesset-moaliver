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
