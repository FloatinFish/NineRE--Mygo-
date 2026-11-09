using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PalrLauncher
{
    /// <summary>深墨黑金主题：颜色、字体、控件工厂。</summary>
    public static class Theme
    {
        public static readonly Color Ink = C("#0A0B0D");
        public static readonly Color Panel = C("#101519");
        public static readonly Color Panel2 = C("#161C21");
        public static readonly Color Hairline = C("#39424E");
        public static readonly Color Gold = C("#C9A35A");
        public static readonly Color GoldBright = C("#E4C27E");
        public static readonly Color GoldDim = C("#7A6334");
        public static readonly Color TextPrimary = C("#F2EDE2");
        public static readonly Color TextSecondary = C("#C9C2B2");
        public static readonly Color TextMuted = C("#9A9284");
        public static readonly Color Danger = C("#C05A4E");
        public static readonly Color Ok = C("#7FB069");

        public static readonly FontFamily Body = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, SimHei");
        public static readonly FontFamily Serif = new FontFamily("SimSun, Songti SC, Microsoft YaHei UI");
        public static readonly FontFamily Mono = new FontFamily("Consolas, Cascadia Mono, Courier New");

        public static Color C(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        public static SolidColorBrush B(string hex) { return new SolidColorBrush(C(hex)); }
        public static SolidColorBrush B(Color c) { return new SolidColorBrush(c); }
        public static SolidColorBrush BA(Color c, byte alpha)
        {
            var b = new SolidColorBrush(c);
            b.Opacity = 1;
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        public static TextBlock Text(string s, double size, Color color, FontWeight? weight = null, FontFamily family = null)
        {
            var t = new TextBlock
            {
                Text = s,
                FontSize = size,
                Foreground = new SolidColorBrush(color),
                FontFamily = family ?? Body,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            if (weight.HasValue) t.FontWeight = weight.Value;
            return t;
        }

        public static Border PanelBox(double radius = 4, byte alpha = 0xCC)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(alpha, Panel.R, Panel.G, Panel.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, Hairline.R, Hairline.G, Hairline.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(radius),
                SnapsToDevicePixels = true,
            };
        }

        public static Border HLine(Color color)
        {
            return new Border { Height = 1, Background = new SolidColorBrush(color), Opacity = 0.45 };
        }

        /// <summary>金色主按钮（实心金底、墨字）</summary>
        public static Border PrimaryButton(string label, RoutedEventHandler onClick)
        {
            var baseBrush = GoldPlate();
            var hoverBrush = GoldPlate(0.12);
            var b = new Border
            {
                Height = 50,
                CornerRadius = new CornerRadius(3),
                Background = baseBrush,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xEA, 0xD3, 0x9E)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1C, 0x16, 0x0A)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = Body,
                },
            };
            b.MouseEnter += delegate { b.Background = hoverBrush; };
            b.MouseLeave += delegate { b.Background = baseBrush; };
            b.MouseLeftButtonUp += delegate { onClick(b, null); };
            return b;
        }

        /// <summary>描边次按钮（透明底、金边）</summary>
        public static Border GhostButton(string label, RoutedEventHandler onClick)
        {
            var b = new Border
            {
                Height = 38,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, Hairline.R, Hairline.G, Hairline.B)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 13.5,
                    Foreground = new SolidColorBrush(TextPrimary),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            b.MouseEnter += delegate { b.BorderBrush = new SolidColorBrush(GoldDim); };
            b.MouseLeave += delegate { b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, Hairline.R, Hairline.G, Hairline.B)); };
            b.MouseLeftButtonUp += delegate { onClick(b, null); };
            return b;
        }

        /// <summary>文字链（无边框无底色，用于低频危险操作）</summary>
        public static Border TextLink(string label, Color color, RoutedEventHandler onClick)
        {
            var b = new Border
            {
                Height = 28,
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(color),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            b.MouseEnter += delegate { ((TextBlock)b.Child).Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xC0, 0x5A, 0x4E)); };
            b.MouseLeave += delegate { ((TextBlock)b.Child).Foreground = new SolidColorBrush(color); };
            b.MouseLeftButtonUp += delegate { onClick(b, null); };
            return b;
        }


        /// <summary>游戏里的金色金属板：上亮下暗的竖向渐变</summary>
        public static LinearGradientBrush GoldPlate(double lift = 0)
        {
            byte r1 = (byte)Math.Min(255, 0xD8 + lift * 255);
            byte g1 = (byte)Math.Min(255, 0xB2 + lift * 255);
            byte b1 = (byte)Math.Min(255, 0x6C + lift * 255);
            var br = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
            };
            br.GradientStops.Add(new GradientStop(Color.FromRgb(r1, g1, b1), 0));
            br.GradientStops.Add(new GradientStop(Color.FromRgb(0xBE, 0x93, 0x4C), 0.55));
            br.GradientStops.Add(new GradientStop(Color.FromRgb(0xA5, 0x7C, 0x38), 1));
            return br;
        }

        /// <summary>区段标题：宋体小标题 + 一条渐隐金线（游戏面板的常见分节方式）</summary>
        public static Grid SectionTitle(string text)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var t = new TextBlock
            {
                Text = text,
                FontFamily = Serif,
                FontSize = 13.5,
                Foreground = new SolidColorBrush(Gold),
                Margin = new Thickness(0, 0, 12, 0),
            };
            g.Children.Add(t);

            var line = new Border
            {
                Height = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush(
                    Color.FromArgb(0x99, 0xC9, 0xA3, 0x5A),
                    Color.FromArgb(0x00, 0xC9, 0xA3, 0x5A),
                    new Point(0, 0), new Point(1, 0)),
            };
            Grid.SetColumn(line, 1);
            g.Children.Add(line);
            return g;
        }

        public static TextBlock Label(string s)
        {
            return new TextBlock
            {
                Text = s,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(TextMuted),
            };
        }

        public static EllipseAvatar Avatar(string name, double size)
        {
            return new EllipseAvatar(name, size);
        }
    }

    /// <summary>圆形头像占位：深底 + 金环 + 首字</summary>
    public sealed class EllipseAvatar : Grid
    {
        public EllipseAvatar(string name, double size)
        {
            Width = size;
            Height = size;
            Children.Add(new System.Windows.Shapes.Ellipse
            {
                Fill = new SolidColorBrush(Theme.C("#1B1F26")),
                Stroke = new SolidColorBrush(Theme.GoldDim),
                StrokeThickness = 1,
            });
            var initial = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            var txt = Theme.Text(initial, size * 0.42, Theme.TextPrimary, FontWeights.SemiBold, Theme.Serif);
            txt.HorizontalAlignment = HorizontalAlignment.Center;
            txt.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(txt);
        }
    }

    /// <summary>
    /// 游戏风格描边面板：1px 金色外框 + 四角短金线。
    /// 用法与 Border 相同（Padding / Child）。
    /// </summary>
    public sealed class OrnamentBox : Grid
    {
        private readonly Border _frame;

        public OrnamentBox(double radius = 4, byte alpha = 0xE8, bool corners = true)
        {
            _frame = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(alpha, Theme.Panel.R, Theme.Panel.G, Theme.Panel.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x7A, 0xC9, 0xA3, 0x5A)),
                /** 2026-10-30 用户要求描边稍粗：1 → 1.6 */
                BorderThickness = new Thickness(1.6),
                CornerRadius = new CornerRadius(radius),
                SnapsToDevicePixels = true,
            };
            Children.Add(_frame);
            if (corners) AddCorners();
        }

        public Thickness Padding
        {
            get { return _frame.Padding; }
            set { _frame.Padding = value; }
        }

        public UIElement Child
        {
            get { return _frame.Child; }
            set { _frame.Child = value; }
        }

        private void AddCorners()
        {
            var brush = new SolidColorBrush(Color.FromArgb(0xB4, 0xE0, 0xC0, 0x84));
            const double len = 13;
            Children.Add(Tick(brush, len, 0, 0, HorizontalAlignment.Left, VerticalAlignment.Top));
            Children.Add(Tick(brush, len, 0, 0, HorizontalAlignment.Right, VerticalAlignment.Top));
            Children.Add(Tick(brush, len, 0, 0, HorizontalAlignment.Left, VerticalAlignment.Bottom));
            Children.Add(Tick(brush, len, 0, 0, HorizontalAlignment.Right, VerticalAlignment.Bottom));
        }

        private static Border Tick(Brush brush, double len, double mx, double my,
            HorizontalAlignment h, VerticalAlignment v)
        {
            bool left = h == HorizontalAlignment.Left;
            bool top = v == VerticalAlignment.Top;
            return new Border
            {
                Width = len,
                Height = len,
                BorderBrush = brush,
                BorderThickness = new Thickness(left ? 1 : 0, top ? 1 : 0, left ? 0 : 1, top ? 0 : 1),
                HorizontalAlignment = h,
                VerticalAlignment = v,
                Margin = new Thickness(left ? -1 : 0, top ? -1 : 0, left ? 0 : -1, top ? 0 : -1),
                IsHitTestVisible = false,
            };
        }
    }
}
