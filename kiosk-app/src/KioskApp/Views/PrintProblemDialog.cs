using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KioskApp.Views
{
    /// <summary>
    /// חלון "בעיה בהדפסה" עם שני כפתורים: "בטל הדפסה" (מבטל ומחזיר את הכסף) ו"המשך לחכות".
    /// נבנה בקוד ולא ב-XAML כי זה חלון פשוט מאוד. MessageBox הרגיל של Windows לא מאפשר
    /// לקבוע טקסט משלנו לכפתורים. העיצוב - מהמשאבים המשותפים (App.xaml).
    /// </summary>
    public class PrintProblemDialog : Window
    {
        /// <summary>true אם המשתמש בחר "בטל הדפסה".</summary>
        public bool CancelRequested { get; private set; }

        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        public PrintProblemDialog(string reason)
        {
            Title = "בעיה בהדפסה";
            FlowDirection = FlowDirection.RightToLeft;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true; // מעל התוכנה הפתוחה ומעל הסרגל
            ShowInTaskbar = false;
            FontFamily = Res<FontFamily>("KioskBodyFont");
            Foreground = Res<Brush>("KioskTextBrush");

            var panel = new StackPanel { MaxWidth = 480 };

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            titleRow.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M12,9 L12,13 M12,17 L12.01,17 M10.3,3.9 L1.8,18 A2,2 0 0 0 3.5,21 L20.5,21 A2,2 0 0 0 22.2,18 L13.7,3.9 A2,2 0 0 0 10.3,3.9 Z"),
                Style = Res<Style>("KioskIcon"),
                Width = 26,
                Height = 26,
                Stroke = Res<Brush>("KioskDangerBrush"),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = "בעיה במדפסת",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(titleRow);

            panel.Children.Add(new TextBlock
            {
                Text = reason,
                FontSize = 17,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "אפשר לתקן ולהמשיך, או לבטל את ההדפסה (אם כבר חויבתם - הכסף יוחזר ליתרה).",
                FontSize = 15,
                Foreground = Res<Brush>("KioskMutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };

            var cancelButton = new Button
            {
                Content = "בטל הדפסה",
                Style = Res<Style>("KioskDangerButton"),
                MinHeight = 46,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            cancelButton.Click += (_, _) => { CancelRequested = true; Close(); };

            var waitButton = new Button
            {
                Content = "טענתי - המשך",
                Style = Res<Style>("KioskSecondaryButton"),
                MinHeight = 46
            };
            waitButton.Click += (_, _) => { CancelRequested = false; Close(); };

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(waitButton);
            panel.Children.Add(buttons);

            // כרטיס שמנת עם פינות מעוגלות וצל - כמו שאר הקיוסק
            Content = new Border
            {
                Background = Res<Brush>("KioskCardBrush"),
                BorderBrush = Res<Brush>("KioskTanBrush"),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(28, 26, 28, 26),
                Margin = new Thickness(20),
                Child = panel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 30,
                    ShadowDepth = 6,
                    Opacity = 0.18,
                    Color = Color.FromRgb(0x5B, 0x46, 0x36)
                }
            };
        }
    }
}
