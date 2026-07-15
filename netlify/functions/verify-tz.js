exports.handler = async (event, context) => {
  // חסימת קריאות שאינן POST
  if (event.httpMethod !== "POST") {
    return { statusCode: 405, body: "Method Not Allowed" };
  }

  try {
    const { tz } = JSON.parse(event.body);

    if (!tz) {
      return {
        statusCode: 400,
        body: JSON.stringify({ success: false, error: "Missing identity number" })
      };
    }

    // ----------------- רשימת הלבנה (Whitelist) -----------------
    // כאן תכניס את רשימת תעודות הזהות המאושרות שלך בתוך מערך הכתוב בסגנון הבא.
    // לצורך הדוגמה הכנסתי פה מספר תעודות זהות מדומות. תוכל להחליף אותן בתעודות הזהות שלכם:
    const approvedTzList = [
      "123456789",
      "987654321",
      "034567891",
      "055667788"
    ];
    // ------------------------------------------------------------

    // בדיקה האם ה-tz שהמשתמש הזין קיים ברשימה המאושרת
    const isAllowed = approvedTzList.includes(tz.trim());

    return {
      statusCode: 200,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ allowed: isAllowed })
    };

  } catch (error) {
    return {
      statusCode: 500,
      body: JSON.stringify({ success: false, error: error.message })
    };
  }
};
