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

        // קבצים שנשמרו בהתחברות הנוכחית, לפי סיומת (".docx"/".pdf"): שם הקובץ שהמשתמש הקליד והנתיב
        // בפועל. שמירה חוזרת באותו שם מעדכנת את אותו קובץ במקום ליצור "(2)", "(3)"... הקיוסק דורס
        // רק קובץ שהוא עצמו יצר בהתחברות הזו - לעולם לא קובץ אחר של המשתמש. נמחק עם החלון בניתוק.
        private readonly System.Collections.Generic.Dictionary<string, (string Name, string Path)> _savedThisSession = new();

        /// <summary>
        /// לאן לשמור: אם באותה התחברות כבר נשמר קובץ מאותו סוג ובאותו שם, והוא עדיין בדיסק-און-קי -
        /// אותו נתיב (עדכון). אחרת - נתיב חדש שלא דורס שום קובץ קיים.
        /// </summary>
        private (string Path, bool IsUpdate) TargetPath(string usb, string name, string extension)
        {
            if (_savedThisSession.TryGetValue(extension, out var prev)
                && prev.Name == name
                && prev.Path.StartsWith(usb, StringComparison.OrdinalIgnoreCase)
                && File.Exists(prev.Path))
            {
                return (prev.Path, true);
            }
            return (FileDialogAssistant.UniquePath(usb, name + extension), false);
        }

        /// <summary>פותח את העורך (או מציג אותו שוב עם הטקסט שהיה בו).</summary>
        /// <param name="backLabel">טקסט כפתור החזרה, למשל "חזרה לתוכנה" או "חזרה לבחירת תוכנה"</param>
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
            Background = Res<Brush>("KioskGroundBrush");
            FontFamily = Res<FontFamily>("KioskBodyFont");
            Foreground = Res<Brush>("KioskTextBrush");
            App.KeepFullScreen(this);

            // --- סרגל עליון (כרטיס שמנת). מימין: כותרת ושם הקובץ; משמאל: פעולות, יתרה וחזרה.
            //     WrapPanel - במסך צר הכפתורים יורדים שורה, במקום לעלות על הטקסט.
            var right = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock
            {
                Text = "עורך טקסט",
                Style = Res<Style>("KioskHeading"),
                FontSize = 22,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0)
            });
            right.Children.Add(new TextBlock
            {
                Text = "שם הקובץ",
                Foreground = Res<Brush>("KioskMutedBrush"),
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24, 0, 8, 0)
            });
            _fileName = new TextBox
            {
                Text = "טקסט מהספרייה",
                Style = Res<Style>("KioskTextBox"),
                FontSize = 15,
                MinHeight = 40,
                Width = 200,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "שם הקובץ שיישמר בדיסק-און-קי"
            };
            right.Children.Add(_fileName);

            var left = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(MakeButton("שמירה כ-Word", "KioskPrimaryButton", OnSaveClick));
            left.Children.Add(MakeButton("שמירה כ-PDF", "KioskPrimaryButton", OnSavePdfClick));
            left.Children.Add(MakeButton("הדפסה", "KioskSecondaryButton", OnPrintClick));
            left.Children.Add(MakeButton("ניקוי", "KioskSecondaryButton", OnClearClick));
            left.Children.Add(new Border
            {
                Width = 1,
                Height = 28,
                Background = Res<Brush>("KioskBorderBrush"),
                Margin = new Thickness(6, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            _balance = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold };
            left.Children.Add(new Border
            {
                Style = Res<Style>("KioskPill"),
                Child = _balance,
                Margin = new Thickness(0, 0, 12, 0)
            });
            _backButton = MakeButton("חזרה", "KioskSoftButton", OnBackClick);
            _backButton.Margin = new Thickness(0);
            left.Children.Add(_backButton);
            UpdateBalance(App.Session.CurrentUser?.Balance ?? 0);
            App.Session.BalanceChanged += OnBalanceChanged;

            var barContent = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(right, Dock.Left);
            DockPanel.SetDock(left, Dock.Right);
            barContent.Children.Add(right);
            barContent.Children.Add(left);

            var bar = new Border
            {
                Background = Res<Brush>("KioskCardBrush"),
                BorderBrush = Res<Brush>("KioskBorderBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20, 12, 20, 12),
                Child = barContent
            };

            // --- שורת מצב ---
            _status = new TextBlock
            {
                Text = "הקלידו או הדביקו כאן טקסט (Ctrl+V). אפשר להדפיס, או לשמור כ-Word / PDF לדיסק-און-קי. הטקסט נמחק בניתוק.",
                Foreground = Res<Brush>("KioskMutedBrush"),
                FontSize = 15,
                Margin = new Thickness(24, 12, 24, 10),
                TextWrapping = TextWrapping.Wrap
            };

            // --- אזור הכתיבה: דף לבן ---
            _editor = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontSize = 22,
                FontFamily = Res<FontFamily>("KioskHeadingFont"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x24, 0x1B)),
                Background = Brushes.White,
                Padding = new Thickness(28, 20, 28, 20),
                FlowDirection = FlowDirection.RightToLeft,
                BorderThickness = new Thickness(0)
            };
            SpellCheck.SetIsEnabled(_editor, false);

            var page = new Border
            {
                Background = Brushes.White,
                BorderBrush = Res<Brush>("KioskBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(8),
                Margin = new Thickness(24, 0, 24, 0),
                Child = _editor
            };

            // --- מונה מילים בתחתית ---
            _counter = new TextBlock
            {
                Foreground = Res<Brush>("KioskMutedBrush"),
                FontSize = 14,
                Margin = new Thickness(24, 10, 24, 14)
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

        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        private Button MakeButton(string text, string styleKey, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = text,
                Style = Res<Style>(styleKey),
                FontSize = 15,
                MinHeight = 40,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 4, 8, 4)
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
                ShowStatus($"נשלח להדפסה: {pages} {(pages == 1 ? "עמוד" : "עמודים")}.", isError: false);
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
                var name = SafeFileName(_fileName.Text);
                var (path, isUpdate) = TargetPath(usb, name, ".pdf");
                // "Microsoft Print to PDF" כותב את הקובץ ברקע - לעדכון מוחקים קודם את הגרסה הקודמת
                if (isUpdate) File.Delete(path);
                TextPrinter.Print(_editor.Text, TextPrinter.PdfPrinterName, path);
                _savedThisSession[".pdf"] = (name, path);
                App.LogInfo($"עורך טקסט: {(isUpdate ? "עודכן" : "נשמר")} PDF בדיסק-און-קי ({path})");
                ShowStatus($"{(isUpdate ? "הקובץ עודכן" : "נשמר")} בדיסק-און-קי: {path}", isError: false);
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
                var (path, isUpdate) = TargetPath(usb, name, ".docx");
                if (isUpdate)
                {
                    // קודם כותבים לקובץ זמני, ורק אם הצליח מחליפים - כדי שתקלה באמצע לא תמחק את הקודם
                    var temp = path + ".tmp";
                    if (File.Exists(temp)) File.Delete(temp);
                    SimpleDocxWriter.Write(temp, _editor.Text);
                    File.Copy(temp, path, overwrite: true);
                    File.Delete(temp);
                }
                else
                {
                    SimpleDocxWriter.Write(path, _editor.Text);
                }
                _savedThisSession[".docx"] = (name, path);
                App.LogInfo($"עורך טקסט: {(isUpdate ? "עודכן" : "נשמר")} קובץ בדיסק-און-קי ({path})");
                ShowStatus($"{(isUpdate ? "הקובץ עודכן" : "נשמר")} בדיסק-און-קי: {path}", isError: false);
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
            _status.Foreground = isError ? Res<Brush>("KioskDangerBrush") : Res<Brush>("KioskSuccessBrush");
            _status.FontWeight = FontWeights.SemiBold;
        }
    }
}
