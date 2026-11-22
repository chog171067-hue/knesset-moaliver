using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KioskApp.Views
{
    /// <summary>
    /// "שמירה להתקן USB" מעורך הטקסט: שם הקובץ, ובחירת פורמט - PDF או Word.
    /// Result: ".pdf" / ".docx", או null אם בוטל.
    /// </summary>
    public class SaveToUsbDialog : Window
    {
        public string? Result { get; private set; }
        public string FileName => _name.Text.Trim();

        private readonly TextBox _name;

        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        public SaveToUsbDialog(string defaultName)
        {
            Title = "שמירה להתקן USB";
            FlowDirection = FlowDirection.RightToLeft;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            FontFamily = Res<FontFamily>("KioskBodyFont");
            Foreground = Res<Brush>("KioskTextBrush");

            var panel = new StackPanel { Width = 420 };
            panel.Children.Add(new TextBlock
            {
                Text = "שמירה להתקן USB",
                FontFamily = Res<FontFamily>("KioskHeadingFont"),
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 18)
            });

            panel.Children.Add(new TextBlock { Text = "שם הקובץ", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
            _name = new TextBox
            {
                Text = defaultName,
                Style = Res<Style>("KioskTextBox"),
                MaxLength = 80,
                Margin = new Thickness(0, 0, 0, 20)
            };
            panel.Children.Add(_name);

            panel.Children.Add(new TextBlock
            {
                Text = "באיזה פורמט לשמור?",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var formats = new UniformGrid2();
            formats.Children.Add(MakeFormatButton("PDF", "לקריאה ולהדפסה", ".pdf"));
            formats.Children.Add(MakeFormatButton("Word", "להמשך עריכה", ".docx"));
            panel.Children.Add(formats);

            var cancel = new Button
            {
                Content = "ביטול",
                Style = Res<Style>("KioskSecondaryButton"),
                MinHeight = 42,
                MinWidth = 120,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16, 0, 0),
                IsCancel = true
            };
            cancel.Click += (_, _) => Close();
            panel.Children.Add(cancel);

            Content = new Border
            {
                Background = Res<Brush>("KioskCardBrush"),
                BorderBrush = Res<Brush>("KioskTanBrush"),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(30, 24, 30, 24),
                Margin = new Thickness(24),
                Child = panel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 36,
                    ShadowDepth = 8,
                    Opacity = 0.22,
                    Color = Color.FromRgb(0x3E, 0x2F, 0x23)
                }
            };

            Loaded += (_, _) =>
            {
                var area = SystemParameters.WorkArea;
                Left = area.Left + (area.Width - ActualWidth) / 2;
                Top = area.Top + (area.Height - ActualHeight) / 2;
                _name.Focus();
                _name.SelectAll();
            };
        }

        private Button MakeFormatButton(string title, string subtitle, string extension)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(new TextBlock { Text = subtitle, FontSize = 13, Opacity = 0.85, HorizontalAlignment = HorizontalAlignment.Center });
            var b = new Button
            {
                Content = content,
                Style = Res<Style>("KioskPrimaryButton"),
                MinHeight = 70,
                Margin = new Thickness(6, 0, 6, 0)
            };
            b.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_name.Text))
                {
                    _name.Focus();
                    return;
                }
                Result = extension;
                Close();
            };
            return b;
        }

        /// <summary>שני כפתורים ברוחב שווה</summary>
        private class UniformGrid2 : System.Windows.Controls.Primitives.UniformGrid
        {
            public UniformGrid2() { Columns = 2; Rows = 1; }
        }
    }
}
