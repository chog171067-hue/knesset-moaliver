// הרשאת "עמדת קיוסק": בניגוד לשאר האתר, עמדת ה-Windows אינה מתחברת דרך
// Netlify Identity (זו אפליקציית שולחן עבודה, לא דפדפן) - במקום זאת היא
// שולחת בכל בקשה מפתח סוד קבוע (LIBRARY_STATION_API_KEY) בכותרת x-station-key,
// שהוגדר מראש כמשתנה סביבה גם בנטליפיי וגם בקובץ ההגדרות המקומי של העמדה.
function requireStation(event) {
  const expectedKey = process.env.LIBRARY_STATION_API_KEY;
  if (!expectedKey) {
    return { authorized: false, statusCode: 500, error: 'שגיאת שרת: מפתח האימות של עמדת הקיוסק אינו מוגדר' };
  }

  const headers = event.headers || {};
  const providedKey = headers['x-station-key'];

  if (!providedKey || providedKey !== expectedKey) {
    return { authorized: false, statusCode: 401, error: 'מפתח אימות עמדה שגוי או חסר' };
  }

  return { authorized: true };
}

module.exports = { requireStation };
