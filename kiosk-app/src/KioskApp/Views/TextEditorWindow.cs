using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KioskApp.Services;

namespace KioskApp.Views
{
    /// <summary>
    /// עורך טקסט פשוט בתוך הקיוסק: מדביקים טקסט (למשל מאוצריא) ושומרים אותו כקובץ Word
    /// ישירות לדיסק-און-קי - בלי חלון "שמירה בשם" של Windows ובלי עותק על המחשב.
    ///
    /// חלון אחד לכל ההתחברות: "חזרה" רק מסתיר אותו (הטקסט נשמר בזיכרון, כדי שאפשר יהיה
    /// להעתיק-להדביק הלוך ושוב), והוא נסגר ונמחק בניתוק (EndSession).
    /// </summary>
    public class TextEditorWindow : Window
    {
        private static TextEditorWindow? _instance;

        private readonly TextBox _editor;
        private readonly TextBox _fileName;
        private readonly TextBlock _status;
        private readonly TextBlock _counter;
        private readonly TextBlock _balance;
        private readonly Button _backButton;
        private bool _closingForSessionEnd;

        /// <summary>פותח את העורך (או מציג אותו שוב עם הטקסט שהיה בו).</summary>
        /// <param name="backLabel">טקסט כפתור החזרה, למשל "⬅ חזרה לתוכנה" או "⬅ חזרה לבחירת תוכנה"</param>
        public static void ShowEditor(string backLabel)
        {
            _instance ??= new TextEditorWindow();
            _instance._backButton.Content = backLabel;
            _instance.Show();
            App.BringToFront(_instance);
            _instance._editor.Focus();
        }

        /// <summary>בניתוק: סוגר את העורך ומוחק את הטקסט - המשתמש הבא מתחיל בעמוד ריק.</summary>
        public static void EndSession()
        {
            if (_instance == null) return;
            _instance._closingForSessionEnd = true;
            App.Session.BalanceChanged -= _instance.OnBalanceChanged;
            _instance._editor.Clear();
            _instance.Close();
            _instance = null;
        }

        private TextEditorWindow()
        {
            Title = "עורך טקסט";
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            FlowDirection = FlowDirection.RightToLeft;
            Background = (Brush)Application.Current.Resources["KioskPrimaryBrush"];
            App.KeepFullScreen(this);

            // --- סרגל עליון ---
            var bar = new DockPanel { Margin = new Thickness(20, 10, 20, 10), LastChildFill = false };

            bar.Children.Add(new TextBlock
            {
                Text = "📝 עורך טקסט",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0)
            });

            // במצב מימין לשמאל, Dock.Left = צד ימין במסך. הסדר מימין: כותרת | שם הקובץ | שמירה | ניקוי,
            // וכפתור החזרה בקצה השמאלי (Dock.Right).
            var nameLabel = new TextBlock
            {
                Text = "שם הקובץ:",
                Foreground = Brushes.White,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(30, 0, 6, 0)
            };
            DockPanel.SetDock(nameLabel, Dock.Left);
            bar.Children.Add(nameLabel);

            _fileName = new TextBox
            {
                Text = "טקסט מהספרייה",
                FontSize = 15,
                Width = 170,
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "שם הקובץ שיישמר בדיסק-און-קי"
            };
            DockPanel.SetDock(_fileName, Dock.Left);
            bar.Children.Add(_fileName);

            var saveButton = MakeButton("💾 Word לדיסק-און-קי",
                (Brush)Application.Current.Resources["KioskAccentBrush"], OnSaveClick);
            DockPanel.SetDock(saveButton, Dock.Left);
            bar.Children.Add(saveButton);

            var pdfButton = MakeButton("📄 PDF לדיסק-און-קי",
                (Brush)Application.Current.Resources["KioskAccentBrush"], OnSavePdfClick);
            DockPanel.SetDock(pdfButton, Dock.Left);
            bar.Children.Add(pdfButton);

            var printButton = MakeButton("🖨 הדפסה",
                (Brush)Application.Current.Resources["KioskAccentBrush"], OnPrintClick);
            DockPanel.SetDock(printButton, Dock.Left);
            bar.Children.Add(printButton);

            var clearButton = MakeButton("🗑 ניקוי", Brushes.Transparent, OnClearClick);
            clearButton.BorderBrush = Brushes.White;
            clearButton.BorderThickness = new Thickness(1);
            DockPanel.SetDock(clearButton, Dock.Left);
            bar.Children.Add(clearButton);

            _backButton = MakeButton("⬅ חזרה", Brushes.Transparent, OnBackClick);
            _backButton.BorderBrush = Brushes.White;
            _backButton.BorderThickness = new Thickness(1);
            _backButton.Margin = new Thickness(0);
            DockPanel.SetDock(_backButton, Dock.Right);
            bar.Children.Add(_backButton);

            _balance = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 20, 0)
            };
            DockPanel.SetDock(_balance, Dock.Right);
            bar.Children.Add(_balance);
            UpdateBalance(App.Session.CurrentUser?.Balance ?? 0);
            App.Session.BalanceChanged += OnBalanceChanged;

            // --- שורת מצב ---
            _status = new TextBlock
            {
                Text = "הקלידו או הדביקו כאן טקסט (Ctrl+V). אפשר להדפיס, או לשמור כ-Word / PDF לדיסק-און-קי. הטקסט נמחק בניתוק.",
                Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE0)),
                FontSize = 14,
                Margin = new Thickness(20, 0, 20, 10),
                TextWrapping = TextWrapping.Wrap
            };

            // --- אזור הכתיבה ---
            _editor = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontSize = 20,
                FontFamily = new FontFamily("David, Arial"),
                Padding = new Thickness(16),
                FlowDirection = FlowDirection.RightToLeft,
                BorderThickness = new Thickness(0)
            };
            SpellCheck.SetIsEnabled(_editor, false);

            var page = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(20, 0, 20, 20),
                Child = _editor
            };

            // --- מונה מילים בתחתית ---
            _counter = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE0)),
                FontSize = 14,
                Margin = new Thickness(20, -12, 20, 10)
            };
            _editor.TextChanged += (_, _) => UpdateCounter();
            UpdateCounter();

            var layout = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            DockPanel.SetDock(_status, Dock.Top);
            DockPanel.SetDock(_counter, Dock.Bottom);
            layout.Children.Add(bar);
            layout.Children.Add(_status);
            layout.Children.Add(_counter);
            layout.Children.Add(page);
            Content = layout;

            // Alt+F4 / סגירה אחרת - רק מסתיר (הטקסט נשמר עד הניתוק)
            Closing += (_, e) =>
            {
                if (_closingForSessionEnd) return;
                e.Cancel = true;
                Hide();
            };
        }

        private Button MakeButton(string text, Brush background, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = text,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 10, 0),
                Background = background,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            b.Click += onClick;
            return b;
        }

        // הערכה: כ-400 מילים בעמוד Word (גופן David בגודל 14, כמו בקובץ שנשמר)
        private const int WordsPerPage = 400;

        private void UpdateCounter()
        {
            var words = _editor.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            if (words == 0)
            {
                _counter.Text = "";
                return;
            }
            var pages = Math.Max(1, (int)Math.Ceiling(words / (double)WordsPerPage));
            _counter.Text = $"{words:N0} מילים · בערך {pages} {(pages == 1 ? "עמוד" : "עמודים")}";
        }

        private void OnBalanceChanged(object? sender, decimal balance) => Dispatcher.Invoke(() => UpdateBalance(balance));

        private void UpdateBalance(decimal balance) => _balance.Text = $"יתרה: {balance:0.00} ₪";

        private void OnPrintClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_editor.Text))
            {
                ShowStatus("אין טקסט להדפסה.", isError: true);
                return;
            }

            var printer = TextPrinter.DefaultPhysicalPrinter();
            if (printer == null)
            {
                ShowStatus("לא מוגדרת מדפסת בעמדה. נא לפנות לצוות הספרייה.", isError: true);
                return;
            }

            try
            {
                var pages = TextPrinter.CountPages(_editor.Text, printer);
                var price = App.Session.Config?.PricePerPage ?? 0;
                var cost = pages * price;
                var balance = App.Session.CurrentUser?.Balance ?? 0;

                if (cost > balance)
                {
                    ShowStatus($"ההדפסה ({pages} עמודים) עולה {cost:0.00} ₪, והיתרה {balance:0.00} ₪. יש להטעין כסף.", isError: true);
                    return;
                }

                var answer = MessageBox.Show(this,
                    $"להדפיס {pages} {(pages == 1 ? "עמוד" : "עמודים")}?\nעלות: {cost:0.00} ₪",
                    "הדפסה", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;

                // החיוב עצמו - דרך מעקב ההדפסות הרגיל (כולל החזר אם ההדפסה נכשלת)
                TextPrinter.Print(_editor.Text, printer);
                App.LogInfo($"עורך טקסט: נשלחו להדפסה {pages} עמודים ({printer})");
                ShowStatus($"🖨 נשלח להדפסה: {pages} {(pages == 1 ? "עמוד" : "עמודים")}.", isError: false);
            }
            catch (Exception ex)
            {
                App.LogError("עורך טקסט: הדפסה נכשלה", ex);
                ShowStatus("ההדפסה נכשלה: " + ex.Message, isError: true);
            }
        }

        private void OnSavePdfClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_editor.Text))
            {
                ShowStatus("אין טקסט לשמירה.", isError: true);
                return;
            }

            var usb = FileDialogAssistant.FindUsbDrive();
            if (usb == null)
            {
                ShowStatus("לא נמצא דיסק-און-קי. חברו דיסק-און-קי לעמדה ונסו שוב.", isError: true);
                return;
            }
            if (!TextPrinter.PdfPrinterAvailable())
            {
                ShowStatus("שמירה כ-PDF לא זמינה בעמדה זו (חסר \"Microsoft Print to PDF\"). אפשר לשמור כ-Word.", isError: true);
                return;
            }

            try
            {
                var path = FileDialogAssistant.UniquePath(usb, SafeFileName(_fileName.Text) + ".pdf");
                TextPrinter.Print(_editor.Text, TextPrinter.PdfPrinterName, path);
                App.LogInfo($"עורך טקסט: נשמר PDF בדיסק-און-קי ({path})");
                ShowStatus($"✅ נשמר בדיסק-און-קי: {path}", isError: false);
            }
            catch (Exception ex)
            {
                App.LogError("עורך טקסט: שמירת PDF נכשלה", ex);
                ShowStatus("השמירה נכשלה: " + ex.Message, isError: true);
            }
        }

        private void OnBackClick(object sender, RoutedEventArgs e) => Hide();

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_editor.Text)) return;
            var answer = MessageBox.Show(this, "למחוק את כל הטקסט?", "ניקוי", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) _editor.Clear();
            _editor.Focus();
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_editor.Text))
            {
                ShowStatus("אין טקסט לשמירה.", isError: true);
                return;
            }

            var usb = FileDialogAssistant.FindUsbDrive();
            if (usb == null)
            {
                ShowStatus("לא נמצא דיסק-און-קי. חברו דיסק-און-קי לעמדה ונסו שוב.", isError: true);
                return;
            }

            var name = SafeFileName(_fileName.Text);
            try
            {
                var path = FileDialogAssistant.UniquePath(usb, name + ".docx");
                SimpleDocxWriter.Write(path, _editor.Text);
                App.LogInfo($"עורך טקסט: נשמר קובץ בדיסק-און-קי ({path})");
                ShowStatus($"✅ נשמר בדיסק-און-קי: {path}", isError: false);
            }
            catch (Exception ex)
            {
                App.LogError("עורך טקסט: שמירה לדיסק-און-קי נכשלה", ex);
                ShowStatus("השמירה נכשלה: " + ex.Message, isError: true);
            }
        }

        /// <summary>שם קובץ חוקי: בלי תווים אסורים, לא ריק, לא ארוך מדי</summary>
        private static string SafeFileName(string input)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string((input ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(cleaned)) cleaned = "טקסט מהספרייה";
            return cleaned.Length > 80 ? cleaned.Substring(0, 80) : cleaned;
        }

        private void ShowStatus(string text, bool isError)
        {
            _status.Text = text;
            _status.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(0xFE, 0xB2, 0xB2))
                : new SolidColorBrush(Color.FromRgb(0x9A, 0xE6, 0xB4));
        }
    }
}
