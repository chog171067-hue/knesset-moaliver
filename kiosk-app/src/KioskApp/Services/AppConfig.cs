using System;
using System.IO;
using System.Text.Json;

namespace KioskApp.Services
{
    /// <summary>נטען פעם אחת מ-appsettings.json שיושב לצד ה-exe (ראו kiosk-app/README.md).</summary>
    public class AppConfig
    {
        public string ApiBaseUrl { get; set; } = "";
        public string StationApiKey { get; set; } = "";
        public string StationId { get; set; } = "";

        public static AppConfig Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"קובץ ההגדרות לא נמצא: {path}\nיש להעתיק את appsettings.json לצד קובץ ה-exe ולמלא בו את כתובת האתר ומפתח העמדה.");
            }

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (config == null || string.IsNullOrWhiteSpace(config.ApiBaseUrl) || string.IsNullOrWhiteSpace(config.StationApiKey))
            {
                throw new InvalidOperationException("appsettings.json קיים אך חסרים בו ApiBaseUrl ו/או StationApiKey.");
            }
            if (config.ApiBaseUrl.Contains("YOUR-SITE-NAME", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("יש לערוך את appsettings.json ולהחליף את הערכים לדוגמה בערכים האמיתיים של האתר שלכם.");
            }

            return config;
        }
    }
}
