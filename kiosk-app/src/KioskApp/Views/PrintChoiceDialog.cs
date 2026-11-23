using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KioskApp.Views
{
    public enum PrintChoice { Cancel, PrintNow, AddToArea, Topup }

    /// <summary>
    /// "סיכום הדפסה": מה מודפס, כמה עמודים, שחור-לבן · צד אחד, וסכום לחיוב - ושלוש אפשרויות:
    /// "הדפסה מיידית", "הוספה לאזור ההדפסות" (בלי חיוב עכשיו), "ביטול". כשהיתרה לא מספיקה
    /// להדפסה מיידית - במקומה "להטענה" (ההוספה לאזור עדיין אפשרית).
    /// </summary>
    public class PrintChoiceDialog : Window
    {
        public PrintChoice Choice { get; private set; } = PrintChoice.Cancel;

        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        /// <param name="allowArea">false - בלי "הוספה לאזור ההדפסות" (למשל כשמדפיסים מתוך האזור)</param>
        public PrintChoiceDialog(string what, int pages, decimal cost, decimal balance, bool allowArea, string title = "סיכום הדפסה")
        {
            Title = title;
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

            var enough = balance >= cost;
            var pagesText = pages == 1 ? "עמוד אחד" : $"{pages} עמודים";

            var panel = new StackPanel { Width = 440 };
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontFamily = Res<FontFamily>("KioskHeadingFont"),
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            });
            if (!string.IsNullOrWhiteSpace(what))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = what,
                    FontSize = 15,
                    Foreground = Res<Brush>("KioskMutedBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxHeight = 44,
                    Margin = new Thickness(0, 0, 0, 10)
                });
            }

            var summary = new Border
            {
                Background = Res<Brush>("KioskSoftBrush"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 16),
                Child = new StackPanel
                {
                    Children =
                    {
                        Line("כמות", pagesText),
                        Line("סוג", "שחור-לבן · צד אחד"),
                        Line("סכום לחיוב", $"{cost:0.00} ₪", bold: true),
                        Line("יתרה בארנק", $"{balance:0.00} ₪")
                    }
                }
            };
            panel.Children.Add(summary);

            if (!enough)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "היתרה בארנק אינה מספיקה להדפסה מיידית.",
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Res<Brush>("KioskDangerBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 10)
                });
            }

            var buttons = new StackPanel();
            buttons.Children.Add(enough
                ? MakeButton("הדפסה מיידית", "KioskPrimaryButton", PrintChoice.PrintNow)
                : MakeButton("להטענה", "KioskPrimaryButton", PrintChoice.Topup));
            if (allowArea)
            {
                buttons.Children.Add(MakeButton("הוספה לאזור ההדפסות", "KioskSoftButton", PrintChoice.AddToArea));
            }
            var cancel = MakeButton("ביטול", "KioskSecondaryButton", PrintChoice.Cancel);
            cancel.IsCancel = true;
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

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
            };
        }

        private static UIElement Line(string label, string value, bool bold = false)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var v = new TextBlock { Text = value, FontSize = bold ? 19 : 16, FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold };
            DockPanel.SetDock(v, Dock.Right); // ב-RTL: שמאל
            row.Children.Add(v);
            row.Children.Add(new TextBlock { Text = label, FontSize = 16, Foreground = Res<Brush>("KioskSecondaryTextBrush") });
            return row;
        }

        private Button MakeButton(string text, string style, PrintChoice choice)
        {
            var b = new Button
            {
                Content = text,
                Style = Res<Style>(style),
                MinHeight = 48,
                FontSize = 17,
                Margin = new Thickness(0, 0, 0, 8)
            };
            b.Click += (_, _) => { Choice = choice; Close(); };
            return b;
        }
    }
}
