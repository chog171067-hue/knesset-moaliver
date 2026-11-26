using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KioskApp.Services;

namespace KioskApp.Views
{
    /// <summary>
    /// "הדפסה מדיסק-און-קי": רשימת קובצי ה-PDF וה-Word שבדיסק-און-קי (כולל תיקיות שבתוכו), בחלון של
    /// הקיוסק - לא חלון הקבצים של Windows, כך שאי אפשר לשוטט משם בקבצי המחשב.
    /// SelectedFile: הקובץ שנבחר, או null אם בוטל.
    /// </summary>
    public class UsbPdfPickerDialog : Window
    {
        private const int MaxFiles = 500;
        public const long MaxFileBytes = 100L * 1024 * 1024; // 100MB

        public string? SelectedFile { get; private set; }

        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        public UsbPdfPickerDialog(string usbRoot)
        {
            Title = "הדפסה מדיסק-און-קי";
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

            var panel = new StackPanel { Width = 640 };
            panel.Children.Add(new TextBlock
            {
                Text = "הדפסה מדיסק-און-קי",
                FontFamily = Res<FontFamily>("KioskHeadingFont"),
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "בוחרים קובץ (PDF או Word). הוא יתווסף לאזור ההדפסות, ומשם מדפיסים.",
                FontSize = 15,
                Foreground = Res<Brush>("KioskMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var list = new StackPanel();
            var files = FindPdfFiles(usbRoot);
            if (files.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "לא נמצאו קובצי PDF או Word בדיסק-און-קי.",
                    FontSize = 16,
                    Foreground = Res<Brush>("KioskMutedBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(20, 40, 20, 40)
                });
            }
            var odd = false;
            foreach (var file in files)
            {
                list.Children.Add(MakeRow(usbRoot, file, odd));
                odd = !odd;
            }
            panel.Children.Add(new Border
            {
                Background = Res<Brush>("KioskInputBrush"),
                BorderBrush = Res<Brush>("KioskBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = new ScrollViewer { Height = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list }
            });

            var cancel = new Button
            {
                Content = "ביטול",
                Style = Res<Style>("KioskSecondaryButton"),
                MinHeight = 44,
                MinWidth = 140,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 0),
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
                Padding = new Thickness(28, 22, 28, 22),
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

        private UIElement MakeRow(string usbRoot, FileInfo file, bool odd)
        {
            var folder = Path.GetRelativePath(usbRoot, file.DirectoryName ?? usbRoot);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(file.Name),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            texts.Children.Add(new TextBlock
            {
                Text = (WordConverter.IsWordFile(file.Name) ? "Word · " : "PDF · ") +
                       (folder == "." ? "" : folder + " · ") + $"{file.Length / 1024.0 / 1024.0:0.0} MB",
                FontSize = 13,
                Foreground = Res<Brush>("KioskMutedBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var choose = new Button
            {
                Content = "בחירה",
                Style = Res<Style>("KioskPrimaryButton"),
                MinHeight = 38,
                MinWidth = 90,
                FontSize = 15,
                Margin = new Thickness(10, 0, 0, 0)
            };
            choose.Click += (_, _) =>
            {
                SelectedFile = file.FullName;
                Close();
            };

            var row = new DockPanel();
            DockPanel.SetDock(choose, Dock.Right); // RTL: שמאל
            row.Children.Add(choose);
            row.Children.Add(texts);
            return new Border
            {
                Background = odd ? Res<Brush>("KioskCardBrush") : Brushes.Transparent,
                Padding = new Thickness(14, 9, 14, 9),
                Child = row
            };
        }

        /// <summary>קובצי PDF ו-Word בדיסק-און-קי (גם בתיקיות), בלי קבצים מוסתרים / של המערכת / זמניים של Word</summary>
        private static List<FileInfo> FindPdfFiles(string usbRoot)
        {
            var result = new List<FileInfo>();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 6,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };
            try
            {
                foreach (var path in Directory.EnumerateFiles(usbRoot, "*", options))
                {
                    var name = Path.GetFileName(path);
                    if (name.StartsWith("~$")) continue; // קובץ נעילה זמני של Word
                    var isPdf = Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
                    if (!isPdf && !WordConverter.IsWordFile(path)) continue;
                    result.Add(new FileInfo(path));
                    if (result.Count >= MaxFiles) break;
                }
            }
            catch (Exception ex)
            {
                App.LogError("דיסק-און-קי: קריאת רשימת הקבצים נכשלה", ex);
            }
            return result.OrderBy(f => f.DirectoryName).ThenBy(f => f.Name).ToList();
        }
    }
}
