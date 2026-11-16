using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KioskApp.Views
{
    /// <summary>
    /// חלון "בעיה בהדפסה" עם שני כפתורים: "בטל הדפסה" (מבטל ומחזיר את הכסף) ו"המשך לחכות".
    /// נבנה בקוד ולא ב-XAML כי זה חלון פשוט מאוד. MessageBox הרגיל של Windows לא מאפשר
    /// לקבוע טקסט משלנו לכפתורים.
    /// </summary>
    public class PrintProblemDialog : Window
    {
        /// <summary>true אם המשתמש בחר "בטל הדפסה".</summary>
        public bool CancelRequested { get; private set; }

        public PrintProblemDialog(string reason)
        {
            Title = "בעיה בהדפסה";
            FlowDirection = FlowDirection.RightToLeft;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStyle = WindowStyle.ToolWindow;
            Topmost = true; // מעל התוכנה הפתוחה ומעל ה-Overlay
            ShowInTaskbar = false;

            var panel = new StackPanel { Margin = new Thickness(28), MaxWidth = 460 };

            panel.Children.Add(new TextBlock
            {
                Text = "⚠ ההדפסה לא מתבצעת",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["KioskDangerBrush"],
                Margin = new Thickness(0, 0, 0, 12)
            });
            panel.Children.Add(new TextBlock
            {
                Text = reason,
                FontSize = 17,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "אפשר לבטל את ההדפסה (הכסף יוחזר ליתרה), או לתקן את הבעיה ולהמשיך לחכות.",
                FontSize = 14,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

            var cancelButton = new Button
            {
                Content = "בטל הדפסה",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(22, 10, 22, 10),
                Margin = new Thickness(8, 0, 8, 0),
                Background = (Brush)Application.Current.Resources["KioskDangerBrush"],
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                IsDefault = true
            };
            cancelButton.Click += (_, _) => { CancelRequested = true; Close(); };

            var waitButton = new Button
            {
                Content = "המשך לחכות",
                FontSize = 16,
                Padding = new Thickness(22, 10, 22, 10),
                Margin = new Thickness(8, 0, 8, 0)
            };
            waitButton.Click += (_, _) => { CancelRequested = false; Close(); };

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(waitButton);
            panel.Children.Add(buttons);

            Content = panel;
        }
    }
}
