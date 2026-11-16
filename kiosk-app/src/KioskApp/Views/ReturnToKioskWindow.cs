using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KioskApp.Views
{
    /// <summary>
    /// כפתור קטן וצף "חזרה לקיוסק" בפינה השמאלית-תחתונה של המסך, שמוצג בזמן "יציאה למחשב".
    /// תמיד מעל כל החלונות, בלי מסגרת ובלי אפשרות לסגור אותו אלא בלחיצה עליו.
    /// </summary>
    public class ReturnToKioskWindow : Window
    {
        public ReturnToKioskWindow()
        {
            Title = "חזרה לקיוסק";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Topmost = true;
            ShowInTaskbar = false;
            Background = Brushes.Transparent;
            AllowsTransparency = true;
            FlowDirection = FlowDirection.RightToLeft;

            var button = new Button
            {
                Content = "↩ חזרה לקיוסק",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(16, 10, 16, 10),
                Background = (Brush)Application.Current.Resources["KioskPrimaryBrush"],
                Foreground = Brushes.White,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += (_, _) =>
            {
                var answer = MessageBox.Show(
                    "לחזור לקיוסק?\nחלונות ששייכים לשולחן העבודה ייסגרו - שמרו קודם עבודה פתוחה.",
                    "חזרה לקיוסק", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;

                App.ReturnToKiosk();
                Close();
            };
            Content = button;

            Loaded += (_, _) =>
            {
                var area = SystemParameters.WorkArea;
                Left = area.Left + 16;
                Top = area.Bottom - ActualHeight - 16;
            };
        }
    }
}
