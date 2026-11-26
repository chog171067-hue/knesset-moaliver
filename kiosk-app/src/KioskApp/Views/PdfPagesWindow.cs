using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KioskApp.Services;
using Windows.Data.Pdf;

namespace KioskApp.Views
{
    /// <summary>
    /// "תצוגה ובחירת עמודים" לקובץ (PDF / Word) באזור ההדפסות: תמונה קטנה של כל עמוד עם סימון,
    /// הקלדת טווח ("1-3, 5"), ולחיצה על עמוד - תצוגה מוגדלת. "שמירה" מעדכנת את ההדפסה באזור
    /// (מספר העמודים והמחיר).
    /// </summary>
    public class PdfPagesWindow : Window
    {
        private const uint ThumbWidth = 160;

        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        private readonly PrintAreaItem _item;
        private readonly int _total;
        private readonly List<CheckBox> _checks = new();
        private readonly List<Image> _thumbs = new();
        private readonly TextBox _range;
        private readonly TextBlock _summary;
        private readonly TextBlock _rangeError;
        private PdfDocument? _pdf;
        private bool _closed;
        private bool _syncing;

        private static decimal Price => App.Session.Config?.PricePerPage ?? 0;

        public PdfPagesWindow(PrintAreaItem item)
        {
            _item = item;
            _total = Math.Max(item.TotalPages, item.Pages);
            var selected = new HashSet<int>(item.SelectedPages ?? Enumerable.Range(0, _total));

            Title = "תצוגה ובחירת עמודים";
            FlowDirection = FlowDirection.RightToLeft;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            FontFamily = Res<FontFamily>("KioskBodyFont");
            Foreground = Res<Brush>("KioskTextBrush");
            var area = SystemParameters.WorkArea;
            Width = Math.Min(1000, area.Width - 40);
            Height = Math.Min(780, area.Height - 40);

            var layout = new DockPanel();

            // ===== כותרת =====
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontFamily = Res<FontFamily>("KioskHeadingFont"),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            header.Children.Add(new TextBlock
            {
                Text = "מסמנים את העמודים להדפסה (או מקלידים טווח). לחיצה על עמוד - הגדלה.",
                FontSize = 14,
                Foreground = Res<Brush>("KioskMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            // ===== טווח עמודים =====
            var rangeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            rangeRow.Children.Add(new TextBlock { Text = "עמודים:", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _range = new TextBox { Style = Res<Style>("KioskTextBox"), Width = 220, FontSize = 16, FlowDirection = FlowDirection.LeftToRight };
            _range.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyRange(); };
            rangeRow.Children.Add(_range);
            rangeRow.Children.Add(MakeButton("החלה", "KioskSecondaryButton", ApplyRange));
            rangeRow.Children.Add(MakeButton("כל העמודים", "KioskSecondaryButton", () => SetAll(true)));
            rangeRow.Children.Add(MakeButton("ניקוי הסימון", "KioskSecondaryButton", () => SetAll(false)));
            header.Children.Add(rangeRow);
            _rangeError = new TextBlock
            {
                FontSize = 14,
                Foreground = Res<Brush>("KioskDangerBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 0)
            };
            header.Children.Add(_rangeError);
            DockPanel.SetDock(header, Dock.Top);
            layout.Children.Add(header);

            // ===== תחתית: סיכום + שמירה / ביטול =====
            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var save = MakeButton("שמירה", "KioskPrimaryButton", Save);
            save.MinWidth = 130;
            var cancel = MakeButton("ביטול", "KioskSecondaryButton", Close);
            cancel.IsCancel = true;
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Left); // RTL: ימין
            footer.Children.Add(buttons);
            _summary = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(16, 0, 0, 0) };
            footer.Children.Add(_summary);
            DockPanel.SetDock(footer, Dock.Bottom);
            layout.Children.Add(footer);

            // ===== העמודים =====
            var pages = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            for (var i = 0; i < _total; i++)
            {
                pages.Children.Add(MakePageTile(i, selected.Contains(i)));
            }
            layout.Children.Add(new Border
            {
                Background = Res<Brush>("KioskSoftBrush"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8),
                Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = pages }
            });

            Content = new Border
            {
                Background = Res<Brush>("KioskCardBrush"),
                BorderBrush = Res<Brush>("KioskTanBrush"),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(24, 18, 24, 18),
                Child = layout
            };

            UpdateSummary();
            Loaded += async (_, _) => await LoadThumbnailsAsync();
            Closed += (_, _) => _closed = true;
        }

        private Button MakeButton(string text, string style, Action onClick)
        {
            var b = new Button
            {
                Content = text,
                Style = Res<Style>(style),
                MinHeight = 40,
                FontSize = 15,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(6, 0, 0, 0)
            };
            b.Click += (_, _) => onClick();
            return b;
        }

        private UIElement MakePageTile(int index, bool isChecked)
        {
            var image = new Image { Width = ThumbWidth, Height = ThumbWidth * 1.414, Stretch = Stretch.Uniform, Cursor = Cursors.Hand };
            image.MouseLeftButtonUp += async (_, _) => await ShowLargeAsync(index);
            _thumbs.Add(image);

            var check = new CheckBox { Content = $"עמוד {index + 1}", IsChecked = isChecked, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            check.Click += (_, _) => { SyncRangeText(); UpdateSummary(); };
            _checks.Add(check);

            var tile = new StackPanel();
            tile.Children.Add(new Border
            {
                Background = Brushes.White,
                BorderBrush = Res<Brush>("KioskBorderBrush"),
                BorderThickness = new Thickness(1),
                Child = image
            });
            tile.Children.Add(check);
            return new Border { Margin = new Thickness(8), Child = tile };
        }

        private async Task LoadThumbnailsAsync()
        {
            SyncRangeText();
            try
            {
                _pdf = await PdfPrinter.OpenAsync(_item.DocumentData!);
                for (var i = 0; i < _thumbs.Count && !_closed; i++)
                {
                    _thumbs[i].Source = await PdfPrinter.RenderPreviewAsync(_pdf, i, ThumbWidth * 2);
                }
            }
            catch (Exception ex)
            {
                App.LogError("תצוגת עמודים: טעינת התמונות נכשלה", ex);
            }
        }

        private async Task ShowLargeAsync(int index)
        {
            if (_pdf == null) return;
            var area = SystemParameters.WorkArea;
            var height = area.Height - 140;
            var source = await PdfPrinter.RenderPreviewAsync(_pdf, index, (uint)(height / 1.414 * 1.5));
            var window = new Window
            {
                Owner = this,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x3E, 0x2F, 0x23)),
                Topmost = true,
                ShowInTaskbar = false,
                WindowState = WindowState.Maximized,
                FlowDirection = FlowDirection.RightToLeft
            };
            var panel = new DockPanel { Margin = new Thickness(20) };
            var close = new Button { Content = $"עמוד {index + 1} · סגירה", Style = Res<Style>("KioskSecondaryButton"), MinHeight = 44, MinWidth = 180, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0), IsCancel = true };
            close.Click += (_, _) => window.Close();
            DockPanel.SetDock(close, Dock.Bottom);
            panel.Children.Add(close);
            panel.Children.Add(new Border
            {
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new Image { Source = source, Stretch = Stretch.Uniform, MaxHeight = height }
            });
            window.Content = panel;
            window.MouseLeftButtonUp += (_, _) => window.Close();
            window.ShowDialog();
        }

        private List<int> SelectedIndexes() =>
            _checks.Select((c, i) => (c, i)).Where(x => x.c.IsChecked == true).Select(x => x.i).ToList();

        private void SetAll(bool value)
        {
            foreach (var c in _checks) c.IsChecked = value;
            SyncRangeText();
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            var n = SelectedIndexes().Count;
            _summary.Text = $"נבחרו {n} מתוך {_total} עמודים · {n * Price:0.00} ₪";
        }

        /// <summary>הסימון → טקסט הטווח ("1-3, 5")</summary>
        private void SyncRangeText()
        {
            if (_syncing) return;
            _rangeError.Visibility = Visibility.Collapsed;
            var pages = SelectedIndexes().Select(i => i + 1).ToList();
            var parts = new List<string>();
            for (var k = 0; k < pages.Count;)
            {
                var start = pages[k];
                var end = start;
                while (k + 1 < pages.Count && pages[k + 1] == end + 1) { k++; end = pages[k]; }
                parts.Add(start == end ? $"{start}" : $"{start}-{end}");
                k++;
            }
            _range.Text = string.Join(", ", parts);
        }

        /// <summary>טקסט הטווח → הסימון</summary>
        private void ApplyRange()
        {
            var parsed = ParseRange(_range.Text, _total);
            if (parsed == null)
            {
                _rangeError.Text = $"אפשר להקליד מספרי עמודים בין 1 ל-{_total}, למשל: 1-3, 5";
                _rangeError.Visibility = Visibility.Visible;
                return;
            }
            _rangeError.Visibility = Visibility.Collapsed;
            _syncing = true;
            for (var i = 0; i < _checks.Count; i++) _checks[i].IsChecked = parsed.Contains(i);
            _syncing = false;
            SyncRangeText();
            UpdateSummary();
        }

        /// <summary>"1-3, 5" → {0,1,2,4}. null = טקסט לא תקין</summary>
        private static HashSet<int>? ParseRange(string text, int total)
        {
            var result = new HashSet<int>();
            foreach (var raw in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var part = raw.Trim();
                var dash = part.IndexOfAny(new[] { '-', '–' });
                if (dash > 0)
                {
                    if (!int.TryParse(part[..dash].Trim(), out var a) || !int.TryParse(part[(dash + 1)..].Trim(), out var b)) return null;
                    if (a > b) (a, b) = (b, a);
                    if (a < 1 || b > total) return null;
                    for (var p = a; p <= b; p++) result.Add(p - 1);
                }
                else
                {
                    if (!int.TryParse(part, out var p) || p < 1 || p > total) return null;
                    result.Add(p - 1);
                }
            }
            return result.Count > 0 ? result : null;
        }

        private void Save()
        {
            var selected = SelectedIndexes();
            if (selected.Count == 0)
            {
                _rangeError.Text = "יש לבחור לפחות עמוד אחד.";
                _rangeError.Visibility = Visibility.Visible;
                return;
            }
            _item.SelectedPages = selected.Count == _total ? null : selected;
            _item.Pages = selected.Count;
            PrintArea.Updated(_item);
            Close();
        }
    }
}
