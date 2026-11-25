using System;
using System.Windows;
using System.Windows.Controls;
using KioskApp.Core.Models;
using KioskApp.Services;

namespace KioskApp.Views
{
    public partial class AppPickerWindow : Window
    {
        public AppPickerWindow()
        {
            InitializeComponent();
            App.KeepFullScreen(this);
            Loaded += (_, _) => { PopulateApps(); ShowTab(); };
            Closed += (_, _) => CloseTab();
        }

        // הלשונית הצפה (שם, זמן, יתרה, הודעה לגבאי, ניתוק...) גם במסך הזה
        private OverlayWindow? _tab;

        private void ShowTab()
        {
            if (_tab != null || !App.Session.IsActive) return;
            // Owner = המסך הזה: חלון "בבעלות" תמיד מוצג מעליו, גם כשלוחצים על המסך עצמו
            _tab = new OverlayWindow { Owner = this };
            _tab.InitializeForPicker(this);
            _tab.Closed += (_, _) => _tab = null;
            _tab.Show();
        }

        private void CloseTab()
        {
            _tab?.Close();
            _tab = null;
        }

        private void PopulateApps()
        {
            var user = App.Session.CurrentUser;
            WelcomeText.Text = string.IsNullOrWhiteSpace(user?.DisplayName)
                ? $"שלום, {user?.Username}" : $"שלום, {user!.DisplayName}";
            FooterText.Text = "לעזרה פנו לגבאים";
            AppsPanel.Children.Clear();

            var apps = App.Session.Config?.AllowedApps ?? new System.Collections.Generic.List<AllowedApp>();
            var editorEnabled = App.Session.Config?.TextEditorEnabled == true;
            if (apps.Count == 0 && !editorEnabled)
            {
                AppsPanel.Children.Add(new TextBlock
                {
                    Text = "לא הוגדרו עדיין תוכנות מאושרות. יש לפנות לגבאים.",
                    Foreground = (System.Windows.Media.Brush)FindResource("KioskMutedBrush"),
                    FontSize = 18
                });
                return;
            }

            foreach (var app in SortApps(apps))
            {
                var subtitle = string.IsNullOrWhiteSpace(app.Description) ? null : app.Description.Trim();
                var button = MakeTile(app.DisplayName, "IconBook", subtitle, AppIconLoader.Load(app.IconPath, app.ExePath));
                button.Tag = app;
                button.Click += OnAppButtonClick;
                AppsPanel.Children.Add(button);
            }

            if (editorEnabled)
            {
                var editorButton = MakeTile("עורך טקסט", "IconPen", "כתיבה ושמירה לדיסק-און-קי");
                editorButton.Click += (_, _) => TextEditorWindow.ShowEditor("חזרה לבחירת תוכנה");
                AppsPanel.Children.Add(editorButton);
            }
        }

        /// <summary>אריח בעיצוב הקיוסק: סמל בריבוע רך, שם, ושורת הסבר אופציונלית</summary>
        /// <summary>image: האייקון של התוכנה (אם אין - הסמל iconKey)</summary>
        private Button MakeTile(string title, string iconKey, string? subtitle = null, System.Windows.Media.ImageSource? image = null)
        {
            FrameworkElement icon = image != null
                ? new Image { Source = image, Width = 48, Height = 48, Stretch = System.Windows.Media.Stretch.Uniform }
                : new System.Windows.Shapes.Path
                {
                    Data = (System.Windows.Media.Geometry)FindResource(iconKey),
                    Style = (Style)FindResource("KioskIcon"),
                    Width = 38,
                    Height = 38,
                    Stroke = (System.Windows.Media.Brush)FindResource("KioskAccentBrush"),
                    StrokeThickness = 1.6
                };
            if (image != null) System.Windows.Media.RenderOptions.SetBitmapScalingMode(icon, System.Windows.Media.BitmapScalingMode.HighQuality);
            var iconBox = new Border
            {
                Width = 72,
                Height = 72,
                CornerRadius = new CornerRadius(18),
                Background = (System.Windows.Media.Brush)FindResource("KioskSoftBrush"),
                Child = icon,
                Margin = new Thickness(0, 0, 0, 14)
            };
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;

            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(iconBox);
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            if (subtitle != null)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    FontSize = 14,
                    Foreground = (System.Windows.Media.Brush)FindResource("KioskMutedBrush"),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxHeight = 40,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }

            return new Button { Style = (Style)FindResource("KioskTileButton"), Content = panel };
        }

        private static System.Collections.Generic.List<AllowedApp> SortApps(System.Collections.Generic.List<AllowedApp> apps)
        {
            var copy = new System.Collections.Generic.List<AllowedApp>(apps);
            copy.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            return copy;
        }

        private async void OnAppButtonClick(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var app = (AllowedApp)button.Tag;

            button.IsEnabled = false;
            try
            {
                // התוכנה נסגרה לפני רגע ועוד שומרת את המצב שלה (ספרים פתוחים וכו') - מחכים שתסיים,
                // כדי לא לסגור אותה בכוח באמצע (ואז היא הייתה נפתחת מאופסת)
                var folder = WindowGuard.AppFolderRoot((app.ExePath ?? "").Trim().Trim('"'));
                // (בלי הודעה - פשוט ממתינים, עד 12 שניות לכל היותר)
                for (var i = 0; i < 24 && WindowGuard.AnyProcessRunningFrom(folder); i++)
                    await System.Threading.Tasks.Task.Delay(500);

                var process = ProcessLauncher.Launch(app.ExePath);
                App.CurrentAppName = app.DisplayName;

                var overlay = new OverlayWindow();
                Application.Current.MainWindow = overlay;
                overlay.Initialize(process, this);
                overlay.Show();

                CloseTab(); // בתוך התוכנה - הלשונית של התוכנה במקומה
                Hide(); // לא Close: אם המשתמש יבחר "החלפת תוכנה" מה-Overlay, נחזור לחלון הזה
            }
            catch (Exception ex)
            {
                App.LogError("כשל בהפעלת תוכנה מהעמדה", ex);
                MessageBox.Show(ex.Message, "שגיאה בהפעלת התוכנה", MessageBoxButton.OK, MessageBoxImage.Error);
                button.IsEnabled = true;
            }
        }

        /// <summary>נקרא מה-Overlay כשהמשתמש בוחר "החלפת תוכנה" - חוזרים למסך הזה בלי להתנתק.</summary>
        public void ShowAgain()
        {
            App.CurrentAppName = null;
            PopulateApps();
            Show();
            Application.Current.MainWindow = this;
            App.BringToFront(this);
            ShowTab();
        }
    }
}
