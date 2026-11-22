using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KioskApp.Views
{
    public enum KioskMessageKind { Info, Success, Error, Progress }

    /// <summary>
    /// חלונית הודעה קופצת בעיצוב הקיוסק (כרטיס שמנת מעוגל עם סמל) - במקום הודעות טקסט
    /// בתוך החלונות ובמקום MessageBox של Windows. שלושה שימושים:
    /// ShowMessage - הודעה עם כפתור "אישור" (ממתינה ללחיצה);
    /// ShowTimedAsync - הודעה שנסגרת לבד אחרי כמה שניות (למשל "התשלום אושר");
    /// ShowProgress - "נא להמתין..." בלי כפתורים, שהקוד סוגר בעצמו (CloseProgress).
    /// </summary>
    public class KioskMessageDialog : Window
    {
        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        private const string IconCheckCircle = "M22,11.1 L22,12 A10,10 0 1 1 16.1,2.9 M22,4 L12,14 L9,11";
        private const string IconAlert = "M12,9 L12,13 M12,17 L12.01,17 M10.3,3.9 L1.8,18 A2,2 0 0 0 3.5,21 L20.5,21 A2,2 0 0 0 22.2,18 L13.7,3.9 A2,2 0 0 0 10.3,3.9 Z";
        private const string IconInfo = "M12,2 A10,10 0 1 1 11.99,2 Z M12,16 L12,12 M12,8 L12.01,8";
        private const string IconClock = "M12,2 A10,10 0 1 1 11.99,2 Z M12,6 L12,12 L16,14";

        private KioskMessageDialog(Window? owner, KioskMessageKind kind, string title, string message, bool withButton,
            string buttonText = "אישור")
        {
            Title = title;
            FlowDirection = FlowDirection.RightToLeft;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            FontFamily = Res<FontFamily>("KioskBodyFont");
            Foreground = Res<Brush>("KioskTextBrush");
            if (owner != null && owner.IsVisible) Owner = owner;

            var (iconData, iconBrush) = kind switch
            {
                KioskMessageKind.Success => (IconCheckCircle, Res<Brush>("KioskSuccessBrush")),
                KioskMessageKind.Error => (IconAlert, Res<Brush>("KioskDangerBrush")),
                KioskMessageKind.Progress => (IconClock, Res<Brush>("KioskAccentBrush")),
                _ => (IconInfo, Res<Brush>("KioskAccentBrush"))
            };

            var panel = new StackPanel { Width = 400 };
            panel.Children.Add(new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(32),
                Background = Res<Brush>("KioskSoftBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14),
                Child = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(iconData),
                    Style = Res<Style>("KioskIcon"),
                    Width = 32,
                    Height = 32,
                    Stroke = iconBrush,
                    StrokeThickness = 2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontFamily = Res<FontFamily>("KioskHeadingFont"),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            if (!string.IsNullOrWhiteSpace(message))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = message,
                    FontSize = 16,
                    Foreground = Res<Brush>("KioskSecondaryTextBrush"),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 24
                });
            }

            if (withButton)
            {
                var ok = new Button
                {
                    Content = buttonText,
                    Style = Res<Style>(kind == KioskMessageKind.Error ? "KioskSecondaryButton" : "KioskPrimaryButton"),
                    MinHeight = 46,
                    MinWidth = 160,
                    FontSize = 17,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 22, 0, 0),
                    IsDefault = true,
                    IsCancel = true
                };
                ok.Click += (_, _) => Close();
                panel.Children.Add(ok);
            }

            Content = new Border
            {
                Background = Res<Brush>("KioskCardBrush"),
                BorderBrush = Res<Brush>("KioskTanBrush"),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(32, 28, 32, 28),
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
        }

        /// <summary>הודעה עם כפתור "אישור" - חוזרת אחרי שנסגרה</summary>
        public static void ShowMessage(Window? owner, KioskMessageKind kind, string title, string message)
        {
            new KioskMessageDialog(owner, kind, title, message, withButton: true).ShowDialog();
        }

        /// <summary>הודעה עם כפתור בטקסט אחר (למשל "המשך") - חוזרת אחרי שנסגרה</summary>
        public static void ShowMessage(Window? owner, KioskMessageKind kind, string title, string message, string buttonText)
        {
            new KioskMessageDialog(owner, kind, title, message, withButton: true, buttonText).ShowDialog();
        }

        /// <summary>הודעה שנסגרת לבד אחרי כמה שניות</summary>
        public static async Task ShowTimedAsync(Window? owner, KioskMessageKind kind, string title, string message, double seconds = 2)
        {
            var dialog = new KioskMessageDialog(owner, kind, title, message, withButton: false);
            dialog.Show();
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            if (dialog.IsVisible) dialog.Close();
        }

        /// <summary>"נא להמתין" בלי כפתורים - נסגר ב-CloseProgress</summary>
        public static KioskMessageDialog ShowProgress(Window? owner, string title, string message)
        {
            var dialog = new KioskMessageDialog(owner, KioskMessageKind.Progress, title, message, withButton: false);
            dialog.Show();
            return dialog;
        }

        public void CloseProgress()
        {
            if (IsVisible) Close();
        }
    }
}
