using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PalrLauncher
{
    /// <summary>
    /// 公告浮层（2026-10-30）。
    /// 内容后期可扩展：优先读启动器目录下 `assets\announce.txt`（纯文本），
    /// 没有文件时显示占位文案；Esc / ✕ 关闭，标题行可拖动。
    /// </summary>
    public static class AnnounceDialog
    {
        public static void Show(Window owner)
        {
            var w = new Window
            {
                Owner = owner,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 520,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 520,
                FontFamily = Theme.Body,
            };

            var box = new OrnamentBox(4, 0xF2);
            box.Padding = new Thickness(24, 18, 24, 20);
            var sp = new StackPanel();

            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Theme.Text("公告", 17, Theme.TextPrimary, FontWeights.Normal, Theme.Serif);
            title.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(title);
            var close = new Border
            {
                Width = 26, Height = 24, Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "✕", FontSize = 12,
                    Foreground = new SolidColorBrush(Theme.TextMuted),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            close.MouseLeftButtonDown += delegate (object s2, MouseButtonEventArgs e2) { e2.Handled = true; w.Close(); };
            Grid.SetColumn(close, 1);
            titleRow.Children.Add(close);
            sp.Children.Add(titleRow);

            sp.Children.Add(new Border { Height = 12 });
            var div = Theme.HLine(Theme.Hairline);
            div.Margin = new Thickness(0, 0, 0, 14);
            sp.Children.Add(div);

            string text = "暂无公告。";
            try
            {
                string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "announce.txt");
                if (System.IO.File.Exists(p))
                {
                    string t = System.IO.File.ReadAllText(p).Trim();
                    if (t.Length > 0) text = t;
                }
            }
            catch { }

            var body = Theme.Text(text, 12.5, Theme.TextSecondary);
            body.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 360,
                Content = body,
            });

            sp.Children.Add(new Border { Height = 18 });
            var ok = Theme.PrimaryButton("知道了", delegate { w.Close(); });
            ok.Height = 38;
            sp.Children.Add(ok);

            box.Child = sp;
            w.Content = box;
            titleRow.MouseLeftButtonDown += delegate (object s3, MouseButtonEventArgs e3)
            {
                if (close.IsMouseOver) return;
                try { w.DragMove(); } catch { }
            };
            w.KeyDown += delegate (object s4, KeyEventArgs e4) { if (e4.Key == Key.Escape) w.Close(); };
            w.ShowDialog();
        }
    }

    /// <summary>
    /// 全部设置浮层（2026-10-30）：接入原登录页的全部设置项。
    /// 直接读写 Unity PlayerPrefs（键名与哈希见 GamePrefs），改动在下次启动游戏时生效。
    /// </summary>
    public static class SettingsDialog
    {
        private static readonly string[] QualityNames = { "", "标准", "低", "高", "极致" };

        public static void Show(Window owner)
        {
            var w = new Window
            {
                Owner = owner,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                FontFamily = Theme.Body,
            };

            var box = new OrnamentBox(4, 0xF2);
            box.Padding = new Thickness(24, 18, 24, 20);
            var sp = new StackPanel();

            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Theme.Text("设置", 17, Theme.TextPrimary, FontWeights.Normal, Theme.Serif);
            title.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(title);
            var close = new Border
            {
                Width = 26, Height = 24, Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "✕", FontSize = 12,
                    Foreground = new SolidColorBrush(Theme.TextMuted),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            close.MouseLeftButtonDown += delegate (object s2, MouseButtonEventArgs e2) { e2.Handled = true; w.Close(); };
            Grid.SetColumn(close, 1);
            titleRow.Children.Add(close);
            sp.Children.Add(titleRow);

            sp.Children.Add(new Border { Height = 10 });
            var hint = Theme.Text("与游戏内登录页的设置项一一对应，改动在下次启动游戏时生效。", 11.5, Theme.TextMuted);
            hint.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(hint);
            sp.Children.Add(new Border { Height = 14 });
            var div0 = Theme.HLine(Theme.Hairline);
            div0.Margin = new Thickness(0, 0, 0, 10);
            sp.Children.Add(div0);

            // ── 画质 ──
            sp.Children.Add(Theme.Label("画质"));
            sp.Children.Add(new Border { Height = 6 });
            var qualityRow = new StackPanel { Orientation = Orientation.Horizontal };
            int curQuality = GamePrefs.ReadInt("Quality", 1);
            var qualityBtns = new List<Border>();
            for (int i = 1; i <= 4; i++)
            {
                int val = i;
                var b = Theme.GhostButton(QualityNames[i], null);
                b.Width = 92;
                b.Height = 32;
                b.Margin = new Thickness(0, 0, 8, 0);
                b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    GamePrefs.WriteInt("Quality", val);
                    foreach (var x in qualityBtns)
                    {
                        bool on = x == b;
                        x.BorderBrush = new SolidColorBrush(on ? Theme.Gold : Theme.Hairline);
                        var tb = x.Child as TextBlock;
                        if (tb != null) tb.Foreground = new SolidColorBrush(on ? Theme.GoldBright : Theme.TextPrimary);
                    }
                };
                if (i == curQuality)
                {
                    b.BorderBrush = new SolidColorBrush(Theme.Gold);
                    var tb0 = b.Child as TextBlock;
                    if (tb0 != null) tb0.Foreground = new SolidColorBrush(Theme.GoldBright);
                }
                qualityBtns.Add(b);
                qualityRow.Children.Add(b);
            }
            sp.Children.Add(qualityRow);
            sp.Children.Add(new Border { Height = 14 });

            // ── 画面开关 ──
            sp.Children.Add(Theme.Label("画面"));
            sp.Children.Add(new Border { Height = 6 });
            AddToggle(sp, "抗锯齿", "AntiAliasing", true);
            AddToggle(sp, "泛光", "Bloom", true);
            AddToggle(sp, "高帧率", "FPSHigh", true);
            AddToggle(sp, "角色阴影", "CharShadow", true);
            AddToggle(sp, "战斗角色描边", "BattleCharOutline", true);
            AddToggle(sp, "2D 看板娘", "Is2DLiveBoardGirl", true);

            sp.Children.Add(new Border { Height = 12 });
            var div1 = Theme.HLine(Theme.Hairline);
            div1.Margin = new Thickness(0, 0, 0, 10);
            sp.Children.Add(div1);

            // ── 音量 ──
            sp.Children.Add(Theme.Label("音量"));
            sp.Children.Add(new Border { Height = 6 });
            AddVolume(sp, "音乐", "BGMVolume", "BGMMute");
            AddVolume(sp, "音效", "SEVolume", "SEMute");

            sp.Children.Add(new Border { Height = 18 });
            var ok = Theme.PrimaryButton("完成", delegate { w.Close(); });
            ok.Height = 38;
            sp.Children.Add(ok);

            box.Child = sp;
            w.Content = box;
            titleRow.MouseLeftButtonDown += delegate (object s3, MouseButtonEventArgs e3)
            {
                if (close.IsMouseOver) return;
                try { w.DragMove(); } catch { }
            };
            w.KeyDown += delegate (object s4, KeyEventArgs e4) { if (e4.Key == Key.Escape) w.Close(); };
            w.ShowDialog();
        }

        private static void AddToggle(Panel parent, string label, string prefKey, bool def)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = Theme.Text(label, 12.5, Theme.TextPrimary);
            l.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(l);

            var btn = Theme.GhostButton("开", null);
            btn.Width = 56;
            btn.Height = 28;
            Action refresh = delegate
            {
                var tb = btn.Child as TextBlock;
                if (tb == null) return;
                bool on = GamePrefs.ReadBool(prefKey, def);
                tb.Text = on ? "开" : "关";
                tb.Foreground = new SolidColorBrush(on ? Theme.GoldBright : Theme.TextMuted);
                btn.BorderBrush = new SolidColorBrush(on ? Theme.Gold : Theme.Hairline);
            };
            btn.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                GamePrefs.WriteBool(prefKey, !GamePrefs.ReadBool(prefKey, def));
                refresh();
            };
            refresh();
            Grid.SetColumn(btn, 1);
            row.Children.Add(btn);
            parent.Children.Add(row);
        }

        private static void AddVolume(Panel parent, string label, string volKey, string muteKey)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = Theme.Text(label, 12.5, Theme.TextPrimary);
            l.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(l);

            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                SmallChange = 0.05,
                LargeChange = 0.1,
                Value = GamePrefs.ReadFloat(volKey, 1f),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            Grid.SetColumn(slider, 1);
            g.Children.Add(slider);

            var mb = Theme.GhostButton("有声", null);
            mb.Width = 64;
            mb.Height = 28;
            Action refresh = delegate
            {
                var tb = mb.Child as TextBlock;
                if (tb == null) return;
                bool m = GamePrefs.ReadBool(muteKey, false);
                tb.Text = m ? "静音" : "有声";
                tb.Foreground = new SolidColorBrush(m ? Theme.Danger : Theme.TextMuted);
            };
            mb.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                GamePrefs.WriteBool(muteKey, !GamePrefs.ReadBool(muteKey, false));
                refresh();
            };
            refresh();
            Grid.SetColumn(mb, 2);
            g.Children.Add(mb);

            slider.ValueChanged += delegate
            {
                try { GamePrefs.WriteFloat(volKey, (float)slider.Value); } catch { }
            };

            parent.Children.Add(g);
        }
    }

    /// <summary>通用信息浮层（单按钮）。用于「更新启动器」等占位提示。</summary>
    public static class InfoDialog
    {
        public static void Show(Window owner, string title, string message)
        {
            var w = new Window
            {
                Owner = owner,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 440,
                SizeToContent = SizeToContent.Height,
                FontFamily = Theme.Body,
            };
            var box = new OrnamentBox(4, 0xF2);
            box.Padding = new Thickness(24, 18, 24, 20);
            var sp = new StackPanel();
            var t = Theme.Text(title, 16, Theme.TextPrimary, FontWeights.Normal, Theme.Serif);
            sp.Children.Add(t);
            sp.Children.Add(new Border { Height = 10 });
            var div = Theme.HLine(Theme.Hairline);
            div.Margin = new Thickness(0, 0, 0, 12);
            sp.Children.Add(div);
            var body = Theme.Text(message, 12.5, Theme.TextSecondary);
            body.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(body);
            sp.Children.Add(new Border { Height = 18 });
            var ok = Theme.PrimaryButton("好", delegate { w.Close(); });
            ok.Height = 36;
            sp.Children.Add(ok);
            box.Child = sp;
            w.Content = box;
            t.MouseLeftButtonDown += delegate { try { w.DragMove(); } catch { } };
            w.KeyDown += delegate (object s2, KeyEventArgs e2) { if (e2.Key == Key.Escape) w.Close(); };
            w.ShowDialog();
        }
    }
}
