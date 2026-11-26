using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KioskApp.Services;

namespace KioskApp.Views
{
    /// <summary>
    /// "אזור הדפסות": ההדפסות שממתינות (נבחרו "הוספה לאזור ההדפסות") - בחירה, מחיקה (פח) והדפסה
    /// של הנבחרות דרך חלון הסיכום והחיוב הרגיל; והיסטוריית ההדפסות של החשבון.
    /// </summary>
    public class PrintAreaWindow : Window
    {
        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        private readonly StackPanel _list = new();
        private readonly Dictionary<Guid, CheckBox> _checks = new();
        private readonly Button _printButton;
        private readonly Button _waitingTab;
        private readonly Button _historyTab;
        private readonly Border _waitingView;
        private readonly Border _historyView;
        private readonly StackPanel _historyList = new();
        private bool _historyLoaded;
        private bool _busy;

        public PrintAreaWindow()
        {
            Title = "אזור הדפסות";
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

            var panel = new StackPanel { Width = 720 };
            panel.Children.Add(new TextBlock
            {
                Text = "אזור הדפסות",
                FontFamily = Res<FontFamily>("KioskHeadingFont"),
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            });

            // לשוניות: ממתינות / היסטוריה
            _waitingTab = MakeTab("ממתינות להדפסה", () => ShowTab(history: false));
            _historyTab = MakeTab("היסטוריית הדפסות", () => ShowTab(history: true));
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12),
                Children = { _waitingTab, _historyTab }
            });

            // ===== ממתינות =====
            var waiting = new StackPanel();
            var selectAll = new CheckBox { Content = "בחירת הכול", FontSize = 15, Margin = new Thickness(4, 0, 0, 8), IsChecked = true };
            selectAll.Click += (_, _) =>
            {
                foreach (var c in _checks.Values) c.IsChecked = selectAll.IsChecked == true;
                UpdatePrintButton();
            };
            // הדפסה מדיסק-און-קי: קובץ PDF נבחר מרשימה של הקיוסק ומתווסף לאזור ההדפסות
            var fromUsb = new Button
            {
                Content = "הדפסה מ-USB",
                Style = Res<Style>("KioskSecondaryButton"),
                MinHeight = 42,
                FontSize = 15,
                Padding = new Thickness(16, 0, 16, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            fromUsb.Click += async (_, _) => await AddFromUsbAsync(fromUsb);
            waiting.Children.Add(fromUsb);
            waiting.Children.Add(selectAll);
            waiting.Children.Add(new Border
            {
                Background = Res<Brush>("KioskInputBrush"),
                BorderBrush = Res<Brush>("KioskBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = new ScrollViewer { Height = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list }
            });
            _printButton = new Button
            {
                Style = Res<Style>("KioskPrimaryButton"),
                MinHeight = 50,
                FontSize = 17,
                Margin = new Thickness(0, 14, 0, 0)
            };
            _printButton.Click += async (_, _) => await PrintSelectedAsync();
            waiting.Children.Add(_printButton);
            _waitingView = new Border { Child = waiting };
            panel.Children.Add(_waitingView);

            // ===== היסטוריה =====
            _historyView = new Border
            {
                Visibility = Visibility.Collapsed,
                Background = Res<Brush>("KioskInputBrush"),
                BorderBrush = Res<Brush>("KioskBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = new ScrollViewer { Height = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _historyList }
            };
            panel.Children.Add(_historyView);

            var close = new Button
            {
                Content = "סגירה",
                Style = Res<Style>("KioskSecondaryButton"),
                MinHeight = 44,
                MinWidth = 140,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0),
                IsCancel = true
            };
            close.Click += (_, _) => Close();
            panel.Children.Add(close);

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
            PrintArea.Changed += Render;
            Closed += (_, _) => PrintArea.Changed -= Render;
            Render();
            ShowTab(history: false);
        }

        private Button MakeTab(string text, Action onClick)
        {
            var b = new Button
            {
                Content = text,
                Style = Res<Style>("KioskSecondaryButton"),
                MinHeight = 40,
                FontSize = 15,
                Padding = new Thickness(18, 0, 18, 0),
                Margin = new Thickness(4, 0, 4, 0)
            };
            b.Click += (_, _) => onClick();
            return b;
        }

        private void ShowTab(bool history)
        {
            _waitingView.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
            _historyView.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
            SetTabActive(_waitingTab, !history);
            SetTabActive(_historyTab, history);
            if (history && !_historyLoaded) _ = LoadHistoryAsync();
        }

        private void SetTabActive(Button tab, bool active)
        {
            tab.Background = active ? Res<Brush>("KioskAccentBrush") : Brushes.Transparent;
            tab.Foreground = active ? Brushes.White : Res<Brush>("KioskSecondaryTextBrush");
        }

        private static decimal Price => App.Session.Config?.PricePerPage ?? 0;

        /// <summary>רשימת הממתינות (נבנית מחדש כשהאזור משתנה); הבחירות הקיימות נשמרות</summary>
        private void Render()
        {
            var previous = _checks.ToDictionary(kv => kv.Key, kv => kv.Value.IsChecked == true);
            _checks.Clear();
            _list.Children.Clear();
            var items = PrintArea.Items;
            _waitingTab.Content = $"ממתינות להדפסה ({items.Count})";
            if (items.Count == 0)
            {
                _list.Children.Add(new TextBlock
                {
                    Text = "אין הדפסות ממתינות.\nכשמדפיסים מתוכנה אפשר לבחור \"הוספה לאזור ההדפסות\", ולהדפיס כאן מתי שרוצים.\nאפשר גם להוסיף קובץ PDF או Word מדיסק-און-קי.",
                    FontSize = 15,
                    Foreground = Res<Brush>("KioskMutedBrush"),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20, 40, 20, 40)
                });
            }
            var odd = false;
            foreach (var item in items)
            {
                _list.Children.Add(MakeRow(item, previous.TryGetValue(item.Id, out var was) ? was : true, odd));
                odd = !odd;
            }
            UpdatePrintButton();
        }

        private UIElement MakeRow(PrintAreaItem item, bool isChecked, bool odd)
        {
            var check = new CheckBox { IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            check.Click += (_, _) => UpdatePrintButton();
            _checks[item.Id] = check;

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(item.Title) ? item.Source : item.Title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            texts.Children.Add(new TextBlock
            {
                Text = $"{item.Source} · {item.AddedAt:HH:mm}",
                FontSize = 13,
                Foreground = Res<Brush>("KioskMutedBrush")
            });

            var pagesText = item.Kind == PrintAreaItemKind.PdfFile && item.SelectedPages != null
                ? $"{item.Pages} מתוך {item.TotalPages} עמודים"
                : $"{item.Pages} {(item.Pages == 1 ? "עמוד" : "עמודים")}";
            var info = new TextBlock
            {
                Text = $"{pagesText} · {item.Pages * Price:0.00} ₪",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0)
            };

            var trash = new Button
            {
                Content = "🗑",
                ToolTip = "מחיקה מאזור ההדפסות",
                Style = Res<Style>("KioskSecondaryButton"),
                MinHeight = 38,
                MinWidth = 44,
                FontSize = 16,
                Padding = new Thickness(6, 0, 6, 0)
            };
            trash.Click += (_, _) =>
            {
                var sure = KioskMessageDialog.ShowChoice(this, KioskMessageKind.Info, "מחיקה מאזור ההדפסות",
                    $"למחוק את \"{(string.IsNullOrWhiteSpace(item.Title) ? item.Source : item.Title)}\"?", "מחיקה", "ביטול");
                if (sure) PrintArea.Delete(item);
            };

            var row = new DockPanel();
            DockPanel.SetDock(check, Dock.Left);   // RTL: ימין
            DockPanel.SetDock(trash, Dock.Right);  // RTL: שמאל
            DockPanel.SetDock(info, Dock.Right);
            row.Children.Add(check);
            row.Children.Add(trash);
            // קובץ (PDF / Word): תצוגה ובחירת עמודים
            if (item.Kind == PrintAreaItemKind.PdfFile && item.DocumentData != null)
            {
                var preview = new Button
                {
                    Content = "תצוגה ובחירת עמודים",
                    Style = Res<Style>("KioskSecondaryButton"),
                    MinHeight = 38,
                    FontSize = 14,
                    Padding = new Thickness(10, 0, 10, 0),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                preview.Click += (_, _) => new PdfPagesWindow(item) { Owner = this }.ShowDialog();
                DockPanel.SetDock(preview, Dock.Right);
                row.Children.Add(preview);
            }
            row.Children.Add(info);
            row.Children.Add(texts);
            return new Border
            {
                Background = odd ? Res<Brush>("KioskCardBrush") : Brushes.Transparent,
                Padding = new Thickness(14, 10, 14, 10),
                Child = row
            };
        }

        private List<PrintAreaItem> Selected() =>
            PrintArea.Items.Where(i => _checks.TryGetValue(i.Id, out var c) && c.IsChecked == true).ToList();

        private void UpdatePrintButton()
        {
            var selected = Selected();
            var pages = selected.Sum(i => i.Pages);
            _printButton.IsEnabled = selected.Count > 0 && !_busy;
            _printButton.Content = selected.Count == 0
                ? "הדפסת הנבחרות"
                : $"הדפסת הנבחרות ({selected.Count}) · {pages} עמודים · {pages * Price:0.00} ₪";
        }

        private async Task PrintSelectedAsync()
        {
            var monitor = App.PrintMonitor;
            var selected = Selected();
            if (monitor == null || selected.Count == 0 || _busy) return;
            _busy = true;
            UpdatePrintButton();
            try
            {
                await monitor.PrintFromAreaAsync(selected);
                _historyLoaded = false; // ההיסטוריה התעדכנה
            }
            catch (Exception ex)
            {
                App.LogError("אזור הדפסות: הדפסה נכשלה", ex);
            }
            finally
            {
                _busy = false;
                Render();
            }
        }

        private async Task AddFromUsbAsync(Button button)
        {
            var usb = FileDialogAssistant.FindUsbDrive();
            if (usb == null)
            {
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "לא נמצא דיסק-און-קי",
                    "יש לחבר דיסק-און-קי לעמדה, ולנסות שוב.");
                return;
            }
            var picker = new UsbPdfPickerDialog(usb) { Owner = this };
            picker.ShowDialog();
            var path = picker.SelectedFile;
            if (path == null) return;

            button.IsEnabled = false;
            try
            {
                var info = new System.IO.FileInfo(path);
                if (info.Length > UsbPdfPickerDialog.MaxFileBytes)
                {
                    KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "הקובץ גדול מדי",
                        "אפשר להדפיס קבצים עד 100MB.");
                    return;
                }
                byte[] data;
                if (WordConverter.IsWordFile(path))
                {
                    // Word: ממירים ל-PDF (Word מוסתר) - ומשם כמו כל קובץ PDF
                    var progress = KioskMessageDialog.ShowProgress(this, "מכינים את הקובץ להדפסה", "נא להמתין מספר שניות...");
                    try
                    {
                        data = await WordConverter.ConvertToPdfAsync(path, TimeSpan.FromSeconds(90));
                    }
                    finally
                    {
                        progress.CloseProgress();
                    }
                }
                else
                {
                    data = await System.IO.File.ReadAllBytesAsync(path);
                }
                var pages = await PdfPrinter.CountPagesAsync(data);
                if (pages <= 0)
                {
                    KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "הקובץ ריק", "אין בקובץ עמודים להדפסה.");
                    return;
                }
                PrintArea.Add(new PrintAreaItem
                {
                    Kind = PrintAreaItemKind.PdfFile,
                    Title = System.IO.Path.GetFileNameWithoutExtension(path),
                    Source = "דיסק-און-קי",
                    Pages = pages,
                    TotalPages = pages,
                    DocumentData = data
                });
            }
            catch (Exception ex)
            {
                App.LogError("אזור הדפסות: הוספת קובץ מדיסק-און-קי נכשלה", ex);
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "לא ניתן להוסיף את הקובץ",
                    ex is InvalidOperationException ? ex.Message : "קריאת הקובץ מהדיסק-און-קי נכשלה. אפשר לנסות שוב.");
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private async Task LoadHistoryAsync()
        {
            _historyLoaded = true;
            _historyList.Children.Clear();
            _historyList.Children.Add(Note("טוען..."));
            var userId = App.Session.CurrentUser?.Id;
            if (string.IsNullOrEmpty(userId)) return;
            try
            {
                var info = await App.ApiClient.GetAccountInfoAsync(userId);
                var prints = info.Transactions
                    .Where(t => t.Description.Contains("הדפס"))
                    .ToList();
                _historyList.Children.Clear();
                if (prints.Count == 0)
                {
                    _historyList.Children.Add(Note("אין עדיין הדפסות בחשבון."));
                    return;
                }
                var odd = false;
                foreach (var t in prints)
                {
                    var row = new DockPanel();
                    var date = new TextBlock { Text = t.Timestamp.ToLocalTime().ToString("dd/MM/yy HH:mm"), FontSize = 14, Foreground = Res<Brush>("KioskMutedBrush"), Width = 120 };
                    var amount = new TextBlock
                    {
                        FlowDirection = FlowDirection.LeftToRight,
                        Text = $"{(t.IsCharge ? "-" : "+")}{t.Amount:0.00} ₪",
                        FontSize = 15,
                        FontWeight = FontWeights.Bold,
                        Foreground = t.IsCharge ? Res<Brush>("KioskDangerBrush") : Res<Brush>("KioskSuccessBrush")
                    };
                    DockPanel.SetDock(date, Dock.Left);
                    DockPanel.SetDock(amount, Dock.Right);
                    row.Children.Add(date);
                    row.Children.Add(amount);
                    row.Children.Add(new TextBlock { Text = t.Description, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 8, 0), ToolTip = t.Description });
                    _historyList.Children.Add(new Border { Background = odd ? Res<Brush>("KioskCardBrush") : Brushes.Transparent, Padding = new Thickness(14, 9, 14, 9), Child = row });
                    odd = !odd;
                }
            }
            catch (Exception ex)
            {
                App.LogError("אזור הדפסות: כשל בטעינת ההיסטוריה", ex);
                _historyList.Children.Clear();
                _historyList.Children.Add(Note("לא ניתן לטעון את ההיסטוריה כרגע."));
                _historyLoaded = false;
            }
        }

        private TextBlock Note(string text) => new()
        {
            Text = text,
            FontSize = 15,
            Foreground = Res<Brush>("KioskMutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(20, 30, 20, 30)
        };
    }
}
