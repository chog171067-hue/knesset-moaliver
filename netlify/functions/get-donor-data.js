exports.handler = async function(event, context) {
    if (event.httpMethod !== "POST") {
        return { statusCode: 405, body: "Method Not Allowed" };
    }

    try {
        const body = JSON.parse(event.body);
        
        // שליפת שתי הסיסמאות מנטליפיי
        const API_PASSWORD_1 = process.env.NEDARIM_API_PASSWORD; 
        const API_PASSWORD_2 = process.env.NEDARIM_API_PASSWORD_2; 

        if (!API_PASSWORD_1 || !API_PASSWORD_2) {
            return { statusCode: 500, body: JSON.stringify({ error: "שגיאת שרת: מפתחות ה-API של המוסדות אינם מוגדרים במלואם" }) };
        }

        const MOSAD_1 = "7012851"; 
        const MOSAD_2 = "7005583"; // <--- שנה למספר המוסד השני שלכם!

        // מצב ב': המשתמש ביקש היסטוריית חיובים מפורטת עבור הוראת קבע ספציפית (KevaId)
        if (body.kevaId) {
            // ננסה קודם למשוך מהמוסד הראשון עם הסיסמה שלו
            let historyResponse = await fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaId&MosadId=${MOSAD_1}&ApiPassword=${API_PASSWORD_1}&KevaId=${body.kevaId}`);
            let kevaDetails = await historyResponse.json();

            // אם המוסד הראשון מחזיר שגיאה או לא מוצא, ננסה למשוך מהמוסד השני עם הסיסמה שלו
            if (kevaDetails.Result === "Error" || !kevaDetails.KevaName) {
                historyResponse = await fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaId&MosadId=${MOSAD_2}&ApiPassword=${API_PASSWORD_2}&KevaId=${body.kevaId}`);
                kevaDetails = await historyResponse.json();
            }

            return {
                statusCode: 200,
                headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" },
                body: JSON.stringify({
                    success: true,
                    successCount: kevaDetails.KevaSuccess,
                    totalAmount: kevaDetails.TotalHistoryAmount,
                    history: (kevaDetails.HistoryData || []).map(h => ({
                        status: h.ID,
                        amount: h.Amount,
                        date: h.Date,
                        transactionId: h.TransactionId
                    }))
                })
            };
        }

        // מצב א': כניסה ראשונית (שליפת רשימת ההוראות קבע משני המוסדות)
        const { phone, tz } = body;
        if (!phone || !tz) {
            return { statusCode: 400, body: JSON.stringify({ error: "מספר טלפון ותעודת זהות הם שדות חובה" }) };
        }

        const cleanPhone = phone.trim().replace(/[- ]/g, '');
        const cleanTz = tz.trim();

        // פנייה לשני המוסדות במקביל, כל אחד עם הסיסמה הייעודית שלו
        const [res1, res2] = await Promise.all([
            fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_1}&ApiPassword=${API_PASSWORD_1}`),
            fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_2}&ApiPassword=${API_PASSWORD_2}`)
        ]);

        const allKevot1 = res1.ok ? await res1.json() : [];
        const allKevot2 = res2.ok ? await res2.json() : [];

        const combinedKevot = [...allKevot1, ...allKevot2];

        const donorKevot = combinedKevot.filter(keva => {
            const kevaPhone = (keva.Phone || "").trim().replace(/[- ]/g, '');
            const kevaTz = (keva.Zeout || "").trim();
            return kevaPhone === cleanPhone && kevaTz === cleanTz;
        });

        return {
            statusCode: 200,
            headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" },
            body: JSON.stringify({
                success: true,
                count: donorKevot.length,
                data: donorKevot.map(k => ({
                    kevaId: k.KevaId,
                    name: k.ClientName,
                    amount: k.Amount,
                    nextDate: k.NextDate,
                    lastNum: k.LastNum,
                    category: k.Groupe,
                    comments: k.Comments,
                    enabled: k.Enabled === "1"
                }))
            })
        };

    } catch (error) {
        return {
            statusCode: 500,
            body: JSON.stringify({ error: "שגיאה בעיבוד הנתונים: " + error.message })
        };
    }
};
