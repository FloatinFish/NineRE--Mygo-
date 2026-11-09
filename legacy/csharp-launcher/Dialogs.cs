using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PalrLauncher
{
    /// <summary>添加账号浮层（账号名 + 密码）。</summary>
    public sealed class AccountDialog : Window
    {
        public string AccountName { get { return _name.Text.Trim(); } }
        public string AccountPassword { get { return _pwd.Password; } }

        private readonly TextBox _name;
        private readonly PasswordBox _pwd;

        public AccountDialog(Window owner, string preset)
        {
            Owner = owner;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            FontFamily = Theme.Body;

            var box = new OrnamentBox(4, 0xFA);
            box.Padding = new Thickness(22, 16, 22, 20);
            var sp = new StackPanel();

            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Theme.Text("添加账号", 17, Theme.TextPrimary, FontWeights.Normal, Theme.Serif);
            title.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(title);

            var closeBtn = new Border
            {
                Width = 26,
                Height = 24,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "✕",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Theme.TextMuted),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            closeBtn.MouseEnter += delegate { ((TextBlock)closeBtn.Child).Foreground = new SolidColorBrush(Theme.GoldBright); };
            closeBtn.MouseLeave += delegate { ((TextBlock)closeBtn.Child).Foreground = new SolidColorBrush(Theme.TextMuted); };
            closeBtn.MouseLeftButtonDown += delegate (object s4, MouseButtonEventArgs e4) { e4.Handled = true; DialogResult = false; };
            Grid.SetColumn(closeBtn, 1);
            titleRow.Children.Add(closeBtn);
            sp.Children.Add(titleRow);

            sp.Children.Add(new Border { Height = 10 });
            var hint = Theme.Text("首次进入会自动在本地服务端建号，密码可留空。", 11.5, Theme.TextMuted);
            hint.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(hint);
            sp.Children.Add(new Border { Height = 16 });

            sp.Children.Add(Theme.Label("账号名"));
            sp.Children.Add(new Border { Height = 5 });
            _name = MakeInput(preset ?? "");
            sp.Children.Add(_name);
            sp.Children.Add(new Border { Height = 12 });

            sp.Children.Add(Theme.Label("密码（可空）"));
            sp.Children.Add(new Border { Height = 5 });
            _pwd = new PasswordBox
            {
                Height = 34,
                FontSize = 13,
                Foreground = new SolidColorBrush(Theme.TextPrimary),
                Background = new SolidColorBrush(Color.FromArgb(0x66, 0x1B, 0x1F, 0x26)),
                BorderBrush = new SolidColorBrush(Theme.Hairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                PasswordChar = '●',
            };
            sp.Children.Add(_pwd);
            sp.Children.Add(new Border { Height = 20 });

            var buttons = new Grid();
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var cancel = Theme.GhostButton("取消", delegate { DialogResult = false; });
            buttons.Children.Add(cancel);
            var ok = Theme.PrimaryButton("添加并进入", delegate { DialogResult = true; });
            ok.Height = 38;
            Grid.SetColumn(ok, 2);
            buttons.Children.Add(ok);
            sp.Children.Add(buttons);

            box.Child = sp;
            Content = box;
            /**
             * ★ 2026-10-30 用户反馈「添加账号弹窗失效」：
             * 旧实现把 DragMove 挂在**整个窗口**上——按钮本体是 Border，点下去先进入拖拽
             * （鼠标被捕获），MouseLeftButtonUp 回不到按钮上，于是「取消 / 添加并进入 / ✕」
             * 全部点不动。现在只让标题行可拖动，且点在关闭按钮上不拖。
             */
            titleRow.MouseLeftButtonDown += delegate (object s3, MouseButtonEventArgs e3)
            {
                if (closeBtn.IsMouseOver) { return; }
                try { DragMove(); } catch { }
            };
            KeyDown += delegate (object s2, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
                else if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
            };
            Loaded += delegate { _name.Focus(); };
        }

        private TextBox MakeInput(string text)
        {
            return new TextBox
            {
                Text = text,
                Height = 34,
                FontSize = 13.5,
                Foreground = new SolidColorBrush(Theme.TextPrimary),
                Background = new SolidColorBrush(Color.FromArgb(0x66, 0x1B, 0x1F, 0x26)),
                BorderBrush = new SolidColorBrush(Theme.Hairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                CaretBrush = new SolidColorBrush(Theme.Gold),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
        }
    }

    /// <summary>确认浮层。</summary>
    public static class ConfirmDialog
    {
        public static bool Ask(Window owner, string title, string message)
        {
            var w = new Window
            {
                Owner = owner,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                FontFamily = Theme.Body,
            };
            var box = new OrnamentBox(4, 0xFA);
            box.Padding = new Thickness(22, 18, 22, 20);
            var sp = new StackPanel();
            var titleText = Theme.Text(title, 16, Theme.TextPrimary, FontWeights.Normal, Theme.Serif);
            sp.Children.Add(titleText);
            sp.Children.Add(new Border { Height = 10 });
            var msg = Theme.Text(message, 12.5, Theme.TextSecondary);
            msg.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(msg);
            sp.Children.Add(new Border { Height = 18 });

            var buttons = new Grid();
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.Children.Add(Theme.GhostButton("取消", delegate { w.DialogResult = false; }));
            var ok = Theme.PrimaryButton("删除", delegate { w.DialogResult = true; });
            ok.Height = 38;
            Grid.SetColumn(ok, 2);
            buttons.Children.Add(ok);
            sp.Children.Add(buttons);

            box.Child = sp;
            w.Content = box;
            /** 同上：只在标题文字上拖动，避免吞掉按钮点击 */
            titleText.MouseLeftButtonDown += delegate { try { w.DragMove(); } catch { } };
            w.KeyDown += delegate (object s2, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { w.DialogResult = false; e.Handled = true; }
            };
            return w.ShowDialog() == true;
        }
    }
}
