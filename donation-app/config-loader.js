// טעינת פרופיל ערב ההתרמה (לוגו ימין, לוגו שמאל, סטריפ, סף סכום) עבור
// control.html ו-display.html כאחד. הנתונים מגיעים מ-get-donation-config.js
// (ציבורי, לפי מזהה event בקישור) ואינם ניתנים לעריכה מתוך שני הדפים האלה -
// רק דרך טאב "ערב התרמה" בדף הניהול הראשי של האתר (/admin.html).
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

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('sw.js').catch(() => {});
}
