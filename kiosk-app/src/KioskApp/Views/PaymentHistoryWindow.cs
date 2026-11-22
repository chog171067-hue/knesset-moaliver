using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KioskApp.Core.Models;

namespace KioskApp.Views
{
    /// <summary>
    /// "היסטוריית תשלומים" מהלשונית הצפה: העסקאות האחרונות של המשתמש המחובר
    /// (הטענות, פתיחת חשבון, חיובי הדפסה, החזרים) - תאריך, תיאור וסכום.
    /// </summary>
    public class PaymentHistoryWindow : Window
    {
        private static T Res<T>(string key) => (T)Application.Current.Resources[key];

        private readonly StackPanel _list = new();
        private readonly TextBlock _status;

        public PaymentHistoryWindow()
        {
            Title = "היסטוריית תשלומים";
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

            var panel = new StackPanel { Width = 520 };
            panel.Children.Add(new TextBlock
            {
                Text = "היסטוריית תשלומים",
                FontFamily = Res<FontFamily>("KioskHeadingFont"),
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14)
            });

            _status = new TextBlock
            {
                Text = "טוען...",
                FontSize = 16,
                Foreground = Res<Brush>("KioskMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 10, 0, 10)
            };
            panel.Children.Add(_status);

            panel.Children.Add(new Border
            {
                BorderBrush = Res<Brush>("KioskBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = new ScrollViewer
                {
                    MaxHeight = 420,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = _list
                }
            });

            var close = new Button
            {
                Content = "סגירה",
                Style = Res<Style>("KioskPrimaryButton"),
                MinHeight = 46,
                MinWidth = 160,
                FontSize = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 0),
                IsDefault = true,
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
                Padding = new Thickness(28, 24, 28, 24),
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

            Loaded += async (_, _) => await LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            var userId = App.Session.CurrentUser?.Id;
            if (string.IsNullOrEmpty(userId)) { Close(); return; }
            try
            {
                var info = await App.ApiClient.GetAccountInfoAsync(userId);
                App.Session.UpdateBalance(info.Balance);
                if (info.Transactions.Count == 0)
                {
                    _status.Text = "אין עדיין תשלומים בחשבון.";
                    return;
                }
                _status.Text = $"יתרה נוכחית: {info.Balance:0.00} ₪";
                _status.FontWeight = FontWeights.SemiBold;
                _status.Foreground = Res<Brush>("KioskTextBrush");
                var odd = false;
                foreach (var t in info.Transactions)
                {
                    _list.Children.Add(MakeRow(t, odd));
                    odd = !odd;
                }
            }
            catch (Exception ex)
            {
                App.LogError("היסטוריית תשלומים: כשל בטעינה", ex);
                _status.Text = "לא ניתן לטעון את ההיסטוריה כרגע. אפשר לנסות שוב בעוד רגע.";
            }
        }

        private static UIElement MakeRow(AccountTransaction t, bool odd)
        {
            var row = new Grid { Margin = new Thickness(0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var date = new TextBlock
            {
                Text = t.Timestamp.ToLocalTime().ToString("dd/MM/yy HH:mm"),
                FontSize = 14,
                Foreground = Res<Brush>("KioskMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var description = string.IsNullOrWhiteSpace(t.Description)
                ? (t.IsCharge ? "חיוב" : "הטענה")
                : t.Description;
            var desc = new TextBlock
            {
                Text = description,
                FontSize = 15,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                ToolTip = description
            };
            var amount = new TextBlock
            {
                // משמאל לימין - כדי שהסימן יוצג לפני המספר: "-0.40 ₪" / "+10.00 ₪"
                FlowDirection = FlowDirection.LeftToRight,
                Text = $"{(t.IsCharge ? "-" : "+")}{t.Amount:0.00} ₪",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = t.IsCharge ? Res<Brush>("KioskDangerBrush") : Res<Brush>("KioskSuccessBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(date, 0);
            Grid.SetColumn(desc, 1);
            Grid.SetColumn(amount, 2);
            row.Children.Add(date);
            row.Children.Add(desc);
            row.Children.Add(amount);

            return new Border
            {
                Background = odd ? Res<Brush>("KioskInputBrush") : Brushes.Transparent,
                Padding = new Thickness(14, 10, 14, 10),
                Child = row
            };
        }
    }
}
