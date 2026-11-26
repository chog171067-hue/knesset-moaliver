using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KioskApp.Services;

namespace KioskApp.Views
{
    /// <summary>
    /// עורך טקסט בתוך הקיוסק: דף A4 לבן (כמו ב-Word) עם עיצוב פשוט - גודל גופן, מודגש, קו
    /// תחתון ויישור. מדביקים טקסט (למשל מאוצריא) - הוא נכנס כטקסט נקי. אפשר להדפיס (מספר
    /// העמודים מחושב מראש ומוצג בתחתית) או לשמור להתקן USB כ-PDF או Word.
    ///
    /// חלון אחד לכל ההתחברות: "סגירת העורך" רק מסתירה אותו (הטקסט נשמר בזיכרון, כדי שאפשר
    /// יהיה להעתיק-להדביק הלוך ושוב), והוא נסגר ונמחק בניתוק (EndSession). הלשונית הצפה של
    /// המשתמש מוצגת מעל העורך.
    /// </summary>
    public class TextEditorWindow : Window
    {
        private static TextEditorWindow? _instance;

        private const string DefaultFileName = "טקסט מהספרייה";
        private const double DefaultFontPt = 14;
        private static readonly double[] FontSizesPt = { 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36 };

        private readonly RichTextBox _editor;
        private readonly TextBlock _status;
        private readonly TextBlock _counter;
        private readonly TextBlock _sizeText;
        private readonly ToggleButton _boldButton;
        private readonly ToggleButton _underlineButton;
        private readonly ToggleButton[] _alignButtons;
        private readonly DispatcherTimer _countTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
        private bool _closingForSessionEnd;
        private bool _updatingToolbar;
        private string _lastFileName = DefaultFileName;

        // קבצים שנשמרו בהתחברות הנוכחית, לפי סיומת (".docx"/".pdf"): שם הקובץ והנתיב בפועל. שמירה
        // חוזרת באותו שם מעדכנת את אותו קובץ במקום ליצור "(2)", "(3)"... הקיוסק דורס רק קובץ שהוא
        // עצמו יצר בהתחברות הזו - לעולם לא קובץ אחר של המשתמש. נמחק עם החלון בניתוק.
        private readonly System.Collections.Generic.Dictionary<string, (string Name, string Path)> _savedThisSession = new();

        /// <summary>פותח את העורך (או מציג אותו שוב עם הטקסט שהיה בו).</summary>
        /// <param name="backLabel">לא בשימוש (נשמר לתאימות) - היציאה היא "סגירת העורך"</param>
        public static void ShowEditor(string backLabel)
        {
            try
            {
                // חלון שנסגר בפועל (לא רק הוסתר) אי אפשר להציג שוב - פותחים חדש
                if (_instance != null && _instance._closed)
                {
                    App.LogInfo("עורך טקסט: החלון הקודם נסגר - נפתח חלון חדש");
                    _instance = null;
                }
                // לחיצה על "עורך טקסט" כשהעורך "מוצג" אבל המשתמש לא רואה אותו (נתקע מאחורי מסך
                // בחירת התוכנות) - פותחים חלון חדש עם אותו טקסט, במקום לנסות להקפיץ את הישן
                if (_instance != null && _instance.IsVisible)
                {
                    App.LogInfo("עורך טקסט: פתיחה כשהעורך כבר מוצג - נפתח מחדש עם אותו טקסט");
                    _instance = _instance.Recreate();
                }
                _instance ??= new TextEditorWindow();
                App.LogInfo($"עורך טקסט: פתיחה (מוצג: {_instance.IsVisible}, מצב: {_instance.WindowState})");
                _instance.Show();
                App.BringToFront(_instance);
                _instance._editor.Focus();
                OverlayWindow.RaiseAllTabs(); // הלשונית הצפה - מעל העורך
            }
            catch (Exception ex)
            {
                App.LogError("עורך טקסט: פתיחה נכשלה - נפתח חלון חדש", ex);
                _instance = new TextEditorWindow();
                _instance.Show();
                App.BringToFront(_instance);
            }
        }

        private bool _closed;

        /// <summary>
        /// העורך פתוח אבל מסך בחירת התוכנות קפץ מעליו (קורה כשנסגר חלון אחר - הודעה, אישור הדפסה,
        /// חלון של Windows - ו-Windows מעביר את הפוקוס למסך הבחירה): מחזירים את העורך למעלה.
        /// נקרא ממסך הבחירה בכל פעם שהוא מקבל פוקוס.
        /// </summary>
        public static void RaiseIfShowing()
        {
            var editor = _instance;
            if (editor == null || editor._closed || !editor.IsVisible) return;
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (editor._closed || !editor.IsVisible) return;
                App.BringToFront(editor);
                OverlayWindow.RaiseAllTabs();
            }));
        }

        /// <summary>חלון עורך חדש עם הטקסט, שם הקובץ והקבצים שנשמרו - והישן נסגר</summary>
        private TextEditorWindow Recreate()
        {
            var fresh = new TextEditorWindow();
            try
            {
                var data = TextPrinter.SaveDocument(_editor.Document);
                fresh.ResetDocument();
                using var ms = new System.IO.MemoryStream(data);
                new TextRange(fresh._editor.Document.ContentStart, fresh._editor.Document.ContentEnd)
                    .Load(ms, DataFormats.XamlPackage);
            }
            catch (Exception ex)
            {
                App.LogError("עורך טקסט: העברת הטקסט לחלון החדש נכשלה", ex);
            }
            fresh._lastFileName = _lastFileName;
            foreach (var kv in _savedThisSession) fresh._savedThisSession[kv.Key] = kv.Value;
            _closingForSessionEnd = true;
            _countTimer.Stop();
            try { Close(); } catch { }
            fresh.UpdateToolbar();
            fresh.UpdateWordCount();
            fresh.UpdatePageCount();
            return fresh;
        }

        /// <summary>בניתוק: סוגר את העורך ומוחק את הטקסט - המשתמש הבא מתחיל בעמוד ריק.</summary>
        public static void EndSession()
        {
            if (_instance == null) return;
            _instance._closingForSessionEnd = true;
            _instance._countTimer.Stop();
            _instance._editor.Document.Blocks.Clear();
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

            // ===== סרגל עליון: מימין הכותרת, משמאל הפעולות (מקובצות) וסגירה =====
            var title = new TextBlock
            {
                Text = "עורך טקסט",
                Style = Res<Style>("KioskHeading"),
                FontSize = 24,
                VerticalAlignment = VerticalAlignment.Center
            };

            var actions = new Border
            {
                Background = Res<Brush>("KioskSoftBrush"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        MakeButton("הדפסה", "KioskPrimaryButton", OnPrintClick),
                        MakeButton("שמירה להתקן USB", "KioskPrimaryButton", OnSaveClick),
                        MakeButton("ניקוי", "KioskSecondaryButton", OnClearClick)
                    }
                }
            };
            var close = MakeButton("סגירת העורך", "KioskSoftButton", (_, _) => Hide());
            close.Margin = new Thickness(16, 0, 0, 0);

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(actions);
            left.Children.Add(close);

            var barContent = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(title, Dock.Left);   // ב-RTL: ימין
            DockPanel.SetDock(left, Dock.Right);   // ב-RTL: שמאל
            barContent.Children.Add(title);
            barContent.Children.Add(left);

            var bar = new Border
            {
                Background = Res<Brush>("KioskCardBrush"),
                BorderBrush = Res<Brush>("KioskAccentBrush"),
                BorderThickness = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(24, 10, 24, 10),
                Child = barContent
            };

            // ===== סרגל עיצוב (מעל הדף, במרכז) =====
            _sizeText = new TextBlock { Width = 34, TextAlignment = TextAlignment.Center, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            _boldButton = MakeToggle(new TextBlock { Text = "B", FontWeight = FontWeights.Bold, FontSize = 18 }, "מודגש (Ctrl+B)",
                () => EditingCommands.ToggleBold.Execute(null, _editor));
            _underlineButton = MakeToggle(new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline, FontSize = 18 }, "קו תחתון (Ctrl+U)",
                () => EditingCommands.ToggleUnderline.Execute(null, _editor));
            // ב-RTL: TextAlignment.Left = צמוד לימין (תחילת השורה), Right = צמוד לשמאל
            _alignButtons = new[]
            {
                MakeAlignToggle(AlignIcon(right: true), "יישור לימין", TextAlignment.Left),
                MakeAlignToggle(AlignIcon(center: true), "מרכוז", TextAlignment.Center),
                MakeAlignToggle(AlignIcon(right: false), "יישור לשמאל", TextAlignment.Right),
                MakeAlignToggle(AlignIcon(justify: true), "הצדדה (יישור לשני הצדדים)", TextAlignment.Justify)
            };

            var formatBar = new StackPanel { Orientation = Orientation.Horizontal };
            formatBar.Children.Add(new TextBlock { Text = "גודל:", FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            formatBar.Children.Add(MakeToolButton(new TextBlock { Text = "A−", FontSize = 15, FontWeight = FontWeights.Bold }, "הקטנת הגופן", () => ChangeFontSize(-1)));
            formatBar.Children.Add(_sizeText);
            formatBar.Children.Add(MakeToolButton(new TextBlock { Text = "A+", FontSize = 18, FontWeight = FontWeights.Bold }, "הגדלת הגופן", () => ChangeFontSize(+1)));
            formatBar.Children.Add(Separator());
            formatBar.Children.Add(_boldButton);
            formatBar.Children.Add(_underlineButton);
            formatBar.Children.Add(Separator());
            foreach (var b in _alignButtons) formatBar.Children.Add(b);

            var formatCard = new Border
            {
                Background = Res<Brush>("KioskCardBrush"),
                BorderBrush = Res<Brush>("KioskTanBrush"),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0),
                Child = formatBar
            };

            _status = new TextBlock
            {
                Text = "הקלידו או הדביקו כאן טקסט (Ctrl+V). אפשר לעצב, להדפיס, או לשמור להתקן USB. הטקסט נמחק בניתוק.",
                Foreground = Res<Brush>("KioskMutedBrush"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24, 8, 24, 8)
            };

            // ===== הדף: A4 לבן במרכז, עם שוליים של 2.5 ס"מ - כמו בהדפסה =====
            _editor = new RichTextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                AllowDrop = false,
                IsDocumentEnabled = false,
                Background = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(TextPrinter.PageMargin),
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, // הדף גדל, והגלילה בחלון
                Foreground = Brushes.Black,
                FlowDirection = FlowDirection.RightToLeft
            };
            SpellCheck.SetIsEnabled(_editor, false);
            ResetDocument();
            // הדבקה - כטקסט נקי (בלי צבעים/גופנים/טבלאות מהתוכנה שממנה העתיקו)
            DataObject.AddPastingHandler(_editor, OnPasting);
            _editor.SelectionChanged += (_, _) => UpdateToolbar();
            _editor.TextChanged += (_, _) => { _countTimer.Stop(); _countTimer.Start(); UpdateWordCount(); };
            _countTimer.Tick += (_, _) => { _countTimer.Stop(); UpdatePageCount(); };

            var page = new Border
            {
                Width = TextPrinter.PageWidth,
                MinHeight = TextPrinter.PageHeight,
                Background = Brushes.White,
                BorderBrush = Res<Brush>("KioskBorderBrush"),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 30),
                Child = _editor,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 3,
                    Opacity = 0.18,
                    Color = Color.FromRgb(0x5B, 0x46, 0x36)
                }
            };
            // לחיצה על השטח הריק של הדף - מעבירה את הסמן לסוף הטקסט
            page.MouseLeftButtonDown += (_, _) => { _editor.Focus(); };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = page
            };

            // ===== תחתית: מילים ועמודים =====
            _counter = new TextBlock
            {
                Foreground = Res<Brush>("KioskSecondaryTextBrush"),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var footer = new Border
            {
                Background = Res<Brush>("KioskCardBrush"),
                BorderBrush = Res<Brush>("KioskBorderBrush"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 0, 8),
                Child = _counter
            };

            var layout = new DockPanel();
            // ===== פס אישור "ניקוי" - בתוך העורך (לא חלון נפרד, שגרם לעורך להיתקע מאחורי מסך הבחירה) =====
            var confirmText = new TextBlock
            {
                Text = "למחוק את כל הטקסט?",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };
            var confirmDelete = MakeButton("מחיקה", "KioskDangerButton", (_, _) => ConfirmClear(true));
            var confirmCancel = MakeButton("ביטול", "KioskSecondaryButton", (_, _) => ConfirmClear(false));
            var confirmRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            confirmRow.Children.Add(confirmText);
            confirmRow.Children.Add(confirmDelete);
            confirmRow.Children.Add(confirmCancel);
            _clearConfirmBar = new Border
            {
                Background = Res<Brush>("KioskSoftBrush"),
                BorderBrush = Res<Brush>("KioskDangerBrush"),
                BorderThickness = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(16, 10, 16, 10),
                Child = confirmRow,
                Visibility = Visibility.Collapsed
            };

            DockPanel.SetDock(bar, Dock.Top);
            DockPanel.SetDock(_clearConfirmBar, Dock.Top);
            DockPanel.SetDock(formatCard, Dock.Top);
            DockPanel.SetDock(_status, Dock.Top);
            DockPanel.SetDock(footer, Dock.Bottom);
            layout.Children.Add(bar);
            layout.Children.Add(_clearConfirmBar);
            layout.Children.Add(formatCard);
            layout.Children.Add(_status);
            layout.Children.Add(footer);
            layout.Children.Add(scroll);
            Content = layout;

            UpdateToolbar();
            UpdateWordCount();
            UpdatePageCount();

            // הלשונית הצפה - תמיד מעל העורך (גם כשלוחצים על העורך)
            Activated += (_, _) => OverlayWindow.RaiseAllTabs();

            // Alt+F4 / סגירה אחרת - רק מסתיר (הטקסט נשמר עד הניתוק)
            Closing += (_, e) =>
            {
                if (_closingForSessionEnd) return;
                App.LogInfo("עורך טקסט: בקשת סגירה - מוסתר במקום");
                e.Cancel = true;
                Hide();
            };
            Closed += (_, _) => _closed = true;
            IsVisibleChanged += (_, _) => App.LogInfo($"עורך טקסט: {(IsVisible ? "מוצג" : "הוסתר")}");
        }

        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        /// <summary>מסמך ריק בגופן ובגודל ברירת המחדל (David 14), מימין לשמאל, צמוד לימין</summary>
        private void ResetDocument()
        {
            var doc = new FlowDocument(new Paragraph())
            {
                FontFamily = new FontFamily("David, Arial"),
                FontSize = PtToDip(DefaultFontPt),
                FlowDirection = FlowDirection.RightToLeft,
                TextAlignment = TextAlignment.Left, // ב-RTL: צמוד לימין
                PagePadding = new Thickness(0),
                Foreground = Brushes.Black
            };
            _editor.Document = doc;
        }

        /// <summary>השורה הראשונה בטקסט (לשם ההדפסה באזור ההדפסות / בהיסטוריה)</summary>
        private string FirstLine()
        {
            var line = PlainText.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            return line.Length > 40 ? line.Substring(0, 40) + "…" : line;
        }

        private string PlainText => new TextRange(_editor.Document.ContentStart, _editor.Document.ContentEnd).Text;

        private bool IsEmpty => string.IsNullOrWhiteSpace(PlainText);

        private static double PtToDip(double pt) => pt * 96.0 / 72.0;
        private static double DipToPt(double dip) => Math.Round(dip * 72.0 / 96.0, 1);

        // ===================== סרגל העיצוב =====================

        private void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            var text = e.DataObject.GetData(DataFormats.UnicodeText) as string
                       ?? e.DataObject.GetData(DataFormats.Text) as string;
            if (text == null)
            {
                e.CancelCommand(); // תמונות וכו' - לא מדביקים
                return;
            }
            var plain = new DataObject();
            plain.SetData(DataFormats.UnicodeText, text);
            e.DataObject = plain;
        }

        private void ChangeFontSize(int direction)
        {
            var current = CurrentFontPt() ?? DefaultFontPt;
            double next;
            if (direction > 0)
                next = FontSizesPt.FirstOrDefault(s => s > current + 0.1, FontSizesPt[^1]);
            else
                next = FontSizesPt.LastOrDefault(s => s < current - 0.1, FontSizesPt[0]);
            _editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, PtToDip(next));
            _editor.Focus();
            UpdateToolbar();
        }

        private double? CurrentFontPt()
        {
            var value = _editor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            return value is double d ? DipToPt(d) : null;
        }

        private void SetAlignment(TextAlignment alignment)
        {
            _editor.Selection.ApplyPropertyValue(Paragraph.TextAlignmentProperty, alignment);
            _editor.Focus();
            UpdateToolbar();
        }

        /// <summary>מצב הכפתורים לפי הטקסט שנבחר (מודגש? גודל? יישור?)</summary>
        private void UpdateToolbar()
        {
            if (_updatingToolbar) return;
            _updatingToolbar = true;
            try
            {
                var size = CurrentFontPt();
                _sizeText.Text = size.HasValue ? size.Value.ToString("0.#") : "—";

                var weight = _editor.Selection.GetPropertyValue(TextElement.FontWeightProperty);
                _boldButton.IsChecked = weight is FontWeight w && w.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight();

                var decorations = _editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
                _underlineButton.IsChecked = decorations is TextDecorationCollection tdc && tdc.Count > 0;

                var align = _editor.Selection.GetPropertyValue(Paragraph.TextAlignmentProperty);
                var alignments = new[] { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right, TextAlignment.Justify };
                for (var i = 0; i < _alignButtons.Length; i++)
                {
                    _alignButtons[i].IsChecked = align is TextAlignment a && a == alignments[i];
                }
            }
            finally
            {
                _updatingToolbar = false;
            }
        }

        private Button MakeToolButton(UIElement content, string tooltip, Action onClick)
        {
            var b = new Button
            {
                Content = content,
                ToolTip = tooltip,
                Style = Res<Style>("KioskSecondaryButton"),
                MinHeight = 38,
                MinWidth = 42,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(3, 0, 3, 0),
                Focusable = false // שהסימון בטקסט לא יאבד
            };
            b.Click += (_, _) => onClick();
            return b;
        }

        private ToggleButton MakeToggle(UIElement content, string tooltip, Action onClick)
        {
            var b = new ToggleButton(content, tooltip, Res<Brush>("KioskAccentBrush"), Res<Brush>("KioskOutlineBrush"), Res<Brush>("KioskTextBrush"));
            b.Click += (_, _) =>
            {
                if (_updatingToolbar) return;
                onClick();
                _editor.Focus();
                UpdateToolbar();
            };
            return b;
        }

        private ToggleButton MakeAlignToggle(UIElement icon, string tooltip, TextAlignment alignment) =>
            MakeToggle(icon, tooltip, () => SetAlignment(alignment));

        /// <summary>סמל יישור: ארבעה קווים</summary>
        private static UIElement AlignIcon(bool right = false, bool center = false, bool justify = false)
        {
            var lines = new[] { 18.0, 12.0, 18.0, 10.0 };
            var geometry = new GeometryGroup();
            for (var i = 0; i < lines.Length; i++)
            {
                var len = justify ? 18.0 : lines[i];
                var x0 = justify ? 3 : center ? 12 - len / 2 : right ? 21 - len : 3;
                var y = 5 + i * 5;
                geometry.Children.Add(new LineGeometry(new Point(x0, y), new Point(x0 + len, y)));
            }
            return new System.Windows.Shapes.Path
            {
                Data = geometry,
                Width = 24,
                Height = 24,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                FlowDirection = FlowDirection.LeftToRight, // הסמל עצמו לא מתהפך
                Stroke = (Brush)Application.Current.Resources["KioskTextBrush"]
            };
        }

        private Border Separator() => new()
        {
            Width = 1,
            Height = 26,
            Background = Res<Brush>("KioskBorderBrush"),
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        private Button MakeButton(string text, string styleKey, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = text,
                Style = Res<Style>(styleKey),
                FontSize = 15,
                MinHeight = 40,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(4, 0, 4, 0)
            };
            b.Click += onClick;
            return b;
        }

        // ===================== מונה מילים ועמודים =====================

        private int _pages = 1;

        private void UpdateWordCount()
        {
            var words = PlainText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            _counter.Text = words == 0
                ? "דף ריק"
                : $"{words:N0} מילים · {_pages} {(_pages == 1 ? "עמוד" : "עמודים")} בהדפסה";
        }

        /// <summary>מספר העמודים המדויק בהדפסה (A4) - מחושב שנייה אחרי שמפסיקים להקליד</summary>
        private void UpdatePageCount()
        {
            try
            {
                _pages = IsEmpty ? 1 : TextPrinter.CountPages(_editor.Document);
            }
            catch (Exception ex)
            {
                App.LogError("עורך טקסט: כשל בחישוב מספר העמודים", ex);
            }
            UpdateWordCount();
        }

        // ===================== פעולות =====================

        private async void OnPrintClick(object sender, RoutedEventArgs e)
        {
            if (IsEmpty)
            {
                ShowStatus("אין טקסט להדפסה.", isError: true);
                return;
            }

            var printer = TextPrinter.DefaultPhysicalPrinter();
            var monitor = App.PrintMonitor;
            if (printer == null || monitor == null)
            {
                ShowStatus("לא מוגדרת מדפסת בעמדה. נא לפנות לגבאים.", isError: true);
                return;
            }

            string? documentName = null;
            try
            {
                // אישור (עמודים + סכום) וחיוב - לפני ששולחים להדפסה
                var pages = TextPrinter.CountPages(_editor.Document);
                var title = FirstLine();
                var (docName, addedToArea) = await monitor.PrepayAsync(printer, pages, string.IsNullOrEmpty(title) ? "עורך טקסט" : $"עורך טקסט - {title}");
                if (addedToArea)
                {
                    PrintArea.Add(new PrintAreaItem
                    {
                        Kind = PrintAreaItemKind.EditorDocument,
                        Title = title,
                        Source = "עורך טקסט",
                        Pages = pages,
                        DocumentData = TextPrinter.SaveDocument(_editor.Document)
                    });
                    ShowStatus("נוסף לאזור ההדפסות.", isError: false);
                    await KioskMessageDialog.ShowTimedAsync(this, KioskMessageKind.Success, "נוסף לאזור ההדפסות",
                        "אפשר להדפיס אותו מתי שרוצים - מהלשונית ← \"אזור הדפסות\"", 2.5);
                    return;
                }
                documentName = docName;
                if (documentName == null)
                {
                    ShowStatus("ההדפסה בוטלה.", isError: false);
                    return;
                }
                TextPrinter.Print(_editor.Document, printer, documentName);
                App.LogInfo($"עורך טקסט: נשלחו להדפסה {pages} עמודים ({printer})");
                ShowStatus($"נשלח להדפסה: {pages} {(pages == 1 ? "עמוד" : "עמודים")}.", isError: false);
                await KioskMessageDialog.ShowTimedAsync(this, KioskMessageKind.Success, "נשלח להדפסה",
                    $"{pages} {(pages == 1 ? "עמוד" : "עמודים")}", 2.5);
            }
            catch (Exception ex)
            {
                App.LogError("עורך טקסט: הדפסה נכשלה", ex);
                // חויב אבל לא נשלח - מחזירים את הכסף
                if (documentName != null) await monitor.RefundPrepaidAsync(documentName);
                ShowStatus("ההדפסה נכשלה: " + ex.Message, isError: true);
            }
        }

        // "ניקוי": שאלת האישור מוצגת כפס בתוך העורך (חלון הודעה נפרד גרם לעורך להיתקע מאחורי
        // מסך בחירת התוכנות)
        private Border _clearConfirmBar = null!;

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            if (IsEmpty) return;
            _clearConfirmBar.Visibility = Visibility.Visible;
        }

        private void ConfirmClear(bool delete)
        {
            _clearConfirmBar.Visibility = Visibility.Collapsed;
            if (delete)
            {
                try
                {
                    // מוחקים את התוכן (לא מחליפים את המסמך עצמו) ומחזירים את עיצוב ברירת המחדל
                    var doc = _editor.Document;
                    doc.Blocks.Clear();
                    doc.Blocks.Add(new Paragraph());
                    doc.FontFamily = new FontFamily("David, Arial");
                    doc.FontSize = PtToDip(DefaultFontPt);
                    doc.TextAlignment = TextAlignment.Left;
                    _editor.CaretPosition = doc.ContentStart;
                    UpdateToolbar();
                    UpdateWordCount();
                    UpdatePageCount();
                    App.LogInfo("עורך טקסט: הטקסט נוקה");
                }
                catch (Exception ex)
                {
                    App.LogError("עורך טקסט: ניקוי נכשל", ex);
                }
            }
            _editor.Focus();
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (IsEmpty)
            {
                ShowStatus("אין טקסט לשמירה.", isError: true);
                return;
            }
            if (FileDialogAssistant.FindUsbDrive() == null)
            {
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "לא נמצא התקן USB",
                    "יש לחבר דיסק-און-קי לעמדה, ולנסות שוב.");
                return;
            }

            var dialog = new SaveToUsbDialog(_lastFileName) { Owner = this };
            dialog.ShowDialog();
            if (dialog.Result == null) return;

            var usb = FileDialogAssistant.FindUsbDrive();
            if (usb == null)
            {
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "לא נמצא התקן USB",
                    "יש לחבר דיסק-און-קי לעמדה, ולנסות שוב.");
                return;
            }
            var extension = dialog.Result;
            if (extension == ".pdf" && !TextPrinter.PdfPrinterAvailable())
            {
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "שמירה כ-PDF לא זמינה",
                    "בעמדה זו חסר \"Microsoft Print to PDF\". אפשר לשמור כ-Word.");
                return;
            }

            var name = SafeFileName(dialog.FileName);
            _lastFileName = name;
            try
            {
                var (path, isUpdate) = TargetPath(usb, name, extension);
                if (extension == ".pdf")
                {
                    // "Microsoft Print to PDF" כותב את הקובץ ברקע - לעדכון מוחקים קודם את הגרסה הקודמת
                    if (isUpdate) File.Delete(path);
                    TextPrinter.SavePdf(_editor.Document, path);
                }
                else if (isUpdate)
                {
                    // קודם לקובץ זמני, ורק אם הצליח מחליפים - שתקלה באמצע לא תמחק את הקודם
                    var temp = path + ".tmp";
                    if (File.Exists(temp)) File.Delete(temp);
                    SimpleDocxWriter.Write(temp, _editor.Document);
                    File.Copy(temp, path, overwrite: true);
                    File.Delete(temp);
                }
                else
                {
                    SimpleDocxWriter.Write(path, _editor.Document);
                }
                _savedThisSession[extension] = (name, path);
                App.LogInfo($"עורך טקסט: {(isUpdate ? "עודכן" : "נשמר")} קובץ בהתקן USB ({path})");
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Success,
                    isUpdate ? "הקובץ עודכן בהצלחה בהתקן" : "הקובץ נשמר בהצלחה בהתקן",
                    $"בשם: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                App.LogError("עורך טקסט: שמירה להתקן USB נכשלה", ex);
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "השמירה נכשלה", ex.Message);
            }
            _editor.Focus();
        }

        /// <summary>
        /// לאן לשמור: אם באותה התחברות כבר נשמר קובץ מאותו סוג ובאותו שם, והוא עדיין בהתקן -
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

        /// <summary>שם קובץ חוקי: בלי תווים אסורים, לא ריק, לא ארוך מדי</summary>
        private static string SafeFileName(string input)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string((input ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(cleaned)) cleaned = DefaultFileName;
            return cleaned.Length > 80 ? cleaned.Substring(0, 80) : cleaned;
        }

        private void ShowStatus(string text, bool isError)
        {
            _status.Text = text;
            _status.Foreground = isError ? Res<Brush>("KioskDangerBrush") : Res<Brush>("KioskSuccessBrush");
            _status.FontWeight = FontWeights.SemiBold;
        }

        /// <summary>כפתור עיצוב עם מצב "פעיל" (מודגש פעיל, יישור נוכחי)</summary>
        private class ToggleButton : Button
        {
            private readonly Brush _activeBrush;
            private readonly Brush _outlineBrush;
            private readonly Brush _textBrush;
            private bool _checked;

            public ToggleButton(UIElement content, string tooltip, Brush active, Brush outline, Brush text)
            {
                _activeBrush = active;
                _outlineBrush = outline;
                _textBrush = text;
                Content = content;
                ToolTip = tooltip;
                Style = (Style)Application.Current.Resources["KioskSecondaryButton"];
                MinHeight = 38;
                MinWidth = 42;
                Padding = new Thickness(8, 0, 8, 0);
                Margin = new Thickness(3, 0, 3, 0);
                Focusable = false;
                Apply();
            }

            public bool IsChecked
            {
                get => _checked;
                set { _checked = value; Apply(); }
            }

            private void Apply()
            {
                Background = _checked ? _activeBrush : Brushes.Transparent;
                BorderBrush = _checked ? _activeBrush : _outlineBrush;
                var fg = _checked ? Brushes.White : _textBrush;
                Foreground = fg;
                if (Content is System.Windows.Shapes.Path path) path.Stroke = fg;
                if (Content is TextBlock tb) tb.Foreground = fg;
            }
        }
    }
}
