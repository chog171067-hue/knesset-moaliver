exports.handler = async function(event, context) {
    if (event.httpMethod !== "POST") {
        return { statusCode: 405, body: "Method Not Allowed" };
    }

    try {
        const body = JSON.parse(event.body);
        const API_PASSWORD_1 = process.env.NEDARIM_API_PASSWORD; 
        const API_PASSWORD_2 = process.env.NEDARIM_API_PASSWORD_2; 

        if (!API_PASSWORD_1 || !API_PASSWORD_2) {
            return { statusCode: 500, body: JSON.stringify({ error: "שגיאת שרת: מפתחות ה-API של המוסדות אינם מוגדרים במלואם" }) };
        }

        const MOSAD_1 = "7012851"; 
        const MOSAD_2 = "7005583"; // מוסד קרן הבנין 'גדול יהיה' מעודכן

        // שמות התצוגה של המוסדות
        const MOSAD_1_NAME = "בית הכנסת המרכזי מוהליבר";
        const MOSAD_2_NAME = "קרן הבנין 'גדול יהיה'";

        // מצב ב': המשתמש ביקש היסטוריית חיובים מפורטת עבור הוראת קבע ספציפית (KevaId)
        if (body.kevaId) {
            let historyResponse = await fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaId&MosadId=${MOSAD_1}&ApiPassword=${API_PASSWORD_1}&KevaId=${body.kevaId}`);
            let kevaDetails = await historyResponse.json();

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

        // מצב א': כניסה ראשונית (שליפת רשימת ההוראות קבע)
        const { phone, tz } = body;
        if (!phone || !tz) {
            return { statusCode: 400, body: JSON.stringify({ error: "מספר טלפון ותעודת זהות הם שדות חובה" }) };
        }

        const cleanPhone = phone.trim().replace(/[- ]/g, '');
        const cleanTz = tz.trim();

        const [res1, res2] = await Promise.all([
            fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_1}&ApiPassword=${API_PASSWORD_1}`),
            fetch(`https://matara.pro/nedarimplus/Reports/Manage3.aspx?Action=GetKevaJson&MosadId=${MOSAD_2}&ApiPassword=${API_PASSWORD_2}`)
        ]);

        const allKevot1 = res1.ok ? await res1.json() : [];
        const allKevot2 = res2.ok ? await res2.json() : [];

        // הוספת סימון מזהה מוסד לכל הוראה לפני האיחוד
        const markedKevot1 = allKevot1.map(k => ({ ...k, belongsToMosad: MOSAD_1_NAME }));
        const markedKevot2 = allKevot2.map(k => ({ ...k, belongsToMosad: MOSAD_2_NAME }));

        const combinedKevot = [...markedKevot1, ...markedKevot2];

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
                    mosadName: k.belongsToMosad,
                    name: k.ClientName,
                    amount: k.Amount,
                    nextDate: k.NextDate,
                    lastNum: k.LastNum,
                    category: k.Groupe,
                    comments: k.Comments,
                    enabled: k.Enabled === "1",
                    errorText: k.ErrorText
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
