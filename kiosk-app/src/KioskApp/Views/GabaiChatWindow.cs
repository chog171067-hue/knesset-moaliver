using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KioskApp.Core.Models;

namespace KioskApp.Views
{
    /// <summary>
    /// "שליחת הודעה לגבאי" מהלשונית הצפה - כמו "הודעות לגבאי" באזור האישי באתר:
    /// ההודעה מגיעה לדף הניהול ← "הודעות לגבאי" (וגם במייל לגבאי), והתשובה מוצגת כאן.
    /// השיחה שמורה לחשבון - אפשר לראות את התשובה גם בכניסה הבאה.
    /// </summary>
    public class GabaiChatWindow : Window
    {
        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        private readonly StackPanel _messages = new();
        private readonly ScrollViewer _scroll;
        private readonly TextBox _input;
        private readonly Button _send;
        private readonly TextBlock _status;
        private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(10) };
        private int _shownCount = -1;

        public GabaiChatWindow()
        {
            Title = "שליחת הודעה לגבאי";
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

            var panel = new StackPanel { Width = 560 };
            panel.Children.Add(new TextBlock
            {
                Text = "שליחת הודעה לגבאי",
                FontFamily = Res<FontFamily>("KioskHeadingFont"),
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = "התשובה של הגבאי תופיע כאן. אפשר לראות אותה גם בכניסה הבאה לחשבון.",
                FontSize = 14,
                Foreground = Res<Brush>("KioskMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 14)
            });

            _status = new TextBlock
            {
                Text = "טוען...",
                FontSize = 15,
                Foreground = Res<Brush>("KioskMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20)
            };
            _messages.Children.Add(_status);
            _messages.Margin = new Thickness(12);

            _scroll = new ScrollViewer
            {
                Height = 340,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _messages
            };
            panel.Children.Add(new Border
            {
                Background = Res<Brush>("KioskInputBrush"),
                BorderBrush = Res<Brush>("KioskBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = _scroll
            });

            _input = new TextBox
            {
                Style = Res<Style>("KioskTextBox"),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxLength = 2000,
                Height = 90,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(12, 8, 12, 8),
                FontSize = 16,
                Margin = new Thickness(0, 12, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            panel.Children.Add(_input);

            _send = new Button
            {
                Content = "שליחה",
                Style = Res<Style>("KioskPrimaryButton"),
                MinHeight = 46,
                MinWidth = 150,
                FontSize = 17,
                Margin = new Thickness(6, 14, 6, 0)
            };
            _send.Click += async (_, _) => await SendAsync();
            var close = new Button
            {
                Content = "סגירה",
                Style = Res<Style>("KioskSecondaryButton"),
                MinHeight = 46,
                MinWidth = 120,
                FontSize = 17,
                Margin = new Thickness(6, 14, 6, 0),
                IsCancel = true
            };
            close.Click += (_, _) => Close();
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { _send, close }
            });

            Content = new Border
            {
                Background = Res<Brush>("KioskCardBrush"),
                BorderBrush = Res<Brush>("KioskTanBrush"),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(26, 22, 26, 22),
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

            Loaded += async (_, _) =>
            {
                _input.Focus();
                await LoadAsync();
                _poll.Start();
            };
            _poll.Tick += async (_, _) => await LoadAsync();
            Closed += (_, _) => _poll.Stop();
        }

        private async Task LoadAsync()
        {
            var userId = App.Session.CurrentUser?.Id;
            if (string.IsNullOrEmpty(userId)) { Close(); return; }
            try
            {
                var thread = await App.ApiClient.GetGabaiThreadAsync(userId, markRead: true);
                Render(thread);
            }
            catch (Exception ex)
            {
                App.LogError("הודעות לגבאי: כשל בטעינה", ex);
                if (_shownCount < 0) _status.Text = "לא ניתן לטעון את ההודעות כרגע. אפשר לנסות שוב בעוד רגע.";
            }
        }

        private async Task SendAsync()
        {
            var userId = App.Session.CurrentUser?.Id;
            var text = _input.Text.Trim();
            if (string.IsNullOrEmpty(userId) || text.Length == 0) { _input.Focus(); return; }

            _send.IsEnabled = false;
            _send.Content = "שולח...";
            try
            {
                var thread = await App.ApiClient.SendGabaiMessageAsync(userId, text);
                _input.Clear();
                Render(thread);
                App.LogInfo("נשלחה הודעה לגבאי מהעמדה");
            }
            catch (Exception ex)
            {
                App.LogError("הודעות לגבאי: כשל בשליחה", ex);
                KioskMessageDialog.ShowMessage(this, KioskMessageKind.Error, "ההודעה לא נשלחה", ex.Message);
            }
            finally
            {
                _send.IsEnabled = true;
                _send.Content = "שליחה";
                _input.Focus();
            }
        }

        private void Render(GabaiThread thread)
        {
            if (thread.Messages.Count == _shownCount) return; // אין חדש
            _shownCount = thread.Messages.Count;
            _messages.Children.Clear();
            if (thread.Messages.Count == 0)
            {
                _status.Text = "עדיין אין הודעות. כתבו את ההודעה למטה ולחצו \"שליחה\".";
                _messages.Children.Add(_status);
                return;
            }
            foreach (var m in thread.Messages)
            {
                _messages.Children.Add(MakeBubble(m));
            }
            _scroll.ScrollToEnd();
        }

        private static UIElement MakeBubble(GabaiMessage m)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = (m.FromAdmin ? "הגבאי · " : "אני · ") + m.Timestamp.ToLocalTime().ToString("dd/MM HH:mm"),
                FontSize = 12,
                Foreground = m.FromAdmin ? Res<Brush>("KioskOnBrandBrush") : Res<Brush>("KioskMutedBrush"),
                Opacity = m.FromAdmin ? 0.85 : 1,
                Margin = new Thickness(0, 0, 0, 3)
            });
            content.Children.Add(new TextBlock
            {
                Text = m.Text,
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                Foreground = m.FromAdmin ? Res<Brush>("KioskOnBrandBrush") : Res<Brush>("KioskTextBrush")
            });
            // ההודעות שלי - בצד ימין, של הגבאי - בצד שמאל
            return new Border
            {
                Background = m.FromAdmin ? Res<Brush>("KioskBrandBrush") : Res<Brush>("KioskSoftBrush"),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 8, 14, 10),
                Margin = new Thickness(0, 0, 0, 8),
                MaxWidth = 400,
                HorizontalAlignment = m.FromAdmin ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Child = content
            };
        }
    }
}
