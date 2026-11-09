using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PalrLauncher
{
    public sealed class MainWindow : Window
    {
        private readonly Paths _paths;
        private readonly Store _store;
        private readonly Services _services;

        private Dictionary<string, AccountStat> _stats = new Dictionary<string, AccountStat>();
        private ServiceState _state = new ServiceState();
        private string _selected;
        private bool _busy;
        private Process _game;
        private bool _enteredGame;
        private DateTime _statsAt = DateTime.MinValue;
        /** 启动后是否已做过账号表对账（只做一次） */
        private bool _reconciled;
        /** 服务起不来时只提示一次（引导去跑 环境检查.bat） */
        private bool _serviceHintShown;
        private DateTime _startedAt = DateTime.UtcNow;
        private string _consoleMode = "server";
        private bool _consoleOpen;

        // UI
        private StackPanel _accountList;
        private TextBlock _accountCount;
        /** 账号列表滚动容器（滑块位置随内容刷新重算） */
        private ScrollViewer _accountScroll;
        private TextBlock _detailName, _detailSub;
        private TextBlock _statTier, _statCards, _statTime, _detailLast;
        private Border _retryBtn;
        private Border _restartBtn, _updateBtn;
        private TextBlock _svcPg, _svcSrv, _svcHttp;
        private TextBlock _statusText;
        private Border _enterBtn, _deleteBtn;
        private TextBlock _statLevel, _statStages, _statMoney, _statJade;
        private Border _consoleBody;
        private TextBox _consoleText;
        private TextBlock _consoleChevron;
        private TextBlock _resLabel;
        private Border _winToggle, _fullToggle;
        private MediaElement _video;
        /** 预览 BGM（音量与游戏设置联动，进游戏即停） */
        private MediaElement _bgm;

        private readonly DispatcherTimer _tick = new DispatcherTimer();

        public MainWindow()
        {
            _paths = Paths.Detect();
            _store = new Store(_paths);
            _services = new Services(_paths);

            Title = "NineRE Starter";
            /**
             * ★ 2026-10-30 用户反馈「关闭/缩小按钮失效」根因：
             * 显示器 125% 缩放时，WPF 把内容按 1.25 倍渲染（1180 DIP → 1475 px），
             * 而窗口按 1180 **像素** 创建 → 右侧一栏（含标题栏的 ✕ / —）被挤到窗口外，
             * 看不见也点不到。这里按实际 DPI 放大窗口尺寸，并夹到可用屏幕范围内。
             */
            Width = 1180;
            Height = 720;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Theme.Ink);
            FontFamily = Theme.Body;
            UseLayoutRounding = true;

            if (_paths.Mode == LayoutMode.Unknown)
            {
                BuildUnknownLayout();
                return;
            }

            BuildUi();
            LoadAccounts();
            StartServicesAsync();

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { EnterGameFlow(); e.Handled = true; }
                else if (e.Key == Key.Delete) { DeleteAccountFlow(); e.Handled = true; }
                else if (e.Key == Key.R && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { RestartServerFlow(); e.Handled = true; }
                else if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { AddAccountFlow(); e.Handled = true; }
                else if (e.Key == Key.Escape) { Close(); }
            };

            InitPreviewBgm();

            _tick.Interval = TimeSpan.FromMilliseconds(900);
            _tick.Tick += OnTick;
            _tick.Start();
            /**
             * 2026-10-30 用户要求：**启动器关闭时一并停掉所有本地服务**。
             * 游戏还在跑时先确认（关掉会把游戏连接断掉）；确认后关窗口 → 停服务。
             */
            Closing += delegate (object s0, System.ComponentModel.CancelEventArgs ce0)
            {
                if (_game != null && !_game.HasExited)
                {
                    bool ok = ConfirmDialog.Ask(this, "关闭启动器",
                        "游戏仍在运行。关闭启动器会同时停止数据库与服务器，游戏将断开连接。确定关闭吗？");
                    if (!ok)
                    {
                        ce0.Cancel = true;
                        return;
                    }
                    try { _game.Kill(); } catch { }
                }
            };
            Closed += delegate
            {
                _tick.Stop();
                try { _services.StopAll(); } catch { }
            };
        }

        // ───────────────────────────── 构建界面 ─────────────────────────────

        private void BuildUnknownLayout()
        {
            var root = new Grid { Margin = new Thickness(40) };
            var panel = Theme.PanelBox();
            panel.Padding = new Thickness(28);
            var sp = new StackPanel();
            sp.Children.Add(Theme.Text("找不到游戏目录", 22, Theme.TextPrimary, FontWeights.SemiBold));
            sp.Children.Add(new Border { Height = 12 });
            sp.Children.Add(Theme.Text("启动器需要和游戏放在一起（便携包根目录），或者在 launcher.ini 里写 root=游戏根目录。", 13, Theme.TextSecondary));
            sp.Children.Add(new Border { Height = 16 });
            sp.Children.Add(Theme.Text(_paths.Describe(), 12, Theme.TextMuted, null, Theme.Mono));
            panel.Child = sp;
            root.Children.Add(panel);
            Content = root;
        }

        private void BuildUi()
        {
            var root = new Grid();

            // 背景：视频优先，静态图兜底
            var bgImage = new Image { Stretch = Stretch.UniformToFill };
            var bgPath = FindBackground();
            if (bgPath != null)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(bgPath, UriKind.Absolute);
                    bmp.EndInit();
                    bgImage.Source = bmp;
                }
                catch { }
            }
            root.Children.Add(bgImage);

            string videoPath = Path.Combine(_paths.ExeDir, @"assets\login_bg.mp4");
            if (File.Exists(videoPath))
            {
                _video = new MediaElement
                {
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Manual,
                    IsMuted = true,
                    Stretch = Stretch.UniformToFill,
                    ScrubbingEnabled = false,
                };
                _video.MediaEnded += delegate { try { _video.Position = TimeSpan.Zero; _video.Play(); } catch { } };
                _video.MediaFailed += delegate { _video.Visibility = Visibility.Collapsed; };
                _video.Source = new Uri(videoPath, UriKind.Absolute);
                _video.Loaded += delegate { try { _video.Play(); } catch { } };
                root.Children.Add(_video);
            }

            /**
             * 背景压暗层（2026-10-30 用户定：**只压 10%**）。
             * 之前的版本是 61%→29% 的深色渐变（太重，已撤），现在改成整层均匀 10%，
             * 只为提一点文字对比度，不改变背景本身的亮度和颜色。
             */
            var shade = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x0A, 0x0B, 0x0D)),
                IsHitTestVisible = false,
            };
            root.Children.Add(shade);

            var main = new Grid { Margin = new Thickness(26, 0, 26, 14) };
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(main);

            var chrome = BuildChrome();
            Grid.SetRow(chrome, 0);
            main.Children.Add(chrome);


            var content = new Grid { Margin = new Thickness(0, 2, 0, 12) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(292) });
            Grid.SetRow(content, 1);
            main.Children.Add(content);

            /**
             * ★ 2026-10-30 用户要求：LOGO 不再放标题栏（会把标题栏撑得奇怪），
             * 改为「左栏顶部 = LOGO，其下 = 账号面板」的上下布局。
             */
            var leftCol = new Grid();
            /** 2026-10-30 用户要求：LOGO 与账号面板各占左栏一半 */
            leftCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            leftCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var leftLogo = BuildTitleLogo(0);
            Grid.SetRow(leftLogo, 0);
            leftCol.Children.Add(leftLogo);
            var left = BuildAccountsPanel();
            left.Margin = new Thickness(0, 14, 0, 0);
            Grid.SetRow(left, 1);
            leftCol.Children.Add(left);
            Grid.SetColumn(leftCol, 0);
            content.Children.Add(leftCol);

            var mid = BuildDetailPanel();
            Grid.SetColumn(mid, 2);
            content.Children.Add(mid);

            var right = BuildActionPanel();
            Grid.SetColumn(right, 4);
            content.Children.Add(right);

            var console = BuildConsole();
            Grid.SetRow(console, 2);
            main.Children.Add(console);

            Content = root;
        }

        /**
         * 预览 BGM（2026-10-30 用户要求）：
         * 启动器里循环播放 assets\bgm_preview.m4a，**音量直接取游戏的 BGMVolume/BGMMute**，
         * 玩家在启动器里听到的音量 = 游戏内的音量，进游戏时立即停止。
         */
        private void InitPreviewBgm()
        {
            try
            {
                string p = System.IO.Path.Combine(_paths.ExeDir, "assets", "bgm_preview.m4a");
                if (!System.IO.File.Exists(p)) return;
                _bgm = new MediaElement
                {
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Manual,
                    Source = new Uri(p, UriKind.Absolute),
                    IsMuted = false,
                };
                /** MediaElement 必须在可视树里才会打开媒体：挂一个 0 尺寸的透明节点 */
                _bgm.Width = 0;
                _bgm.Height = 0;
                _bgm.Opacity = 0;
                _bgm.IsHitTestVisible = false;
                var host = Content as Grid;
                if (host != null) host.Children.Add(_bgm);
                _bgm.MediaEnded += delegate { try { _bgm.Position = TimeSpan.Zero; _bgm.Play(); } catch { } };
                _bgm.MediaFailed += delegate { SetStatus("预览音乐加载失败（缺解码器或文件损坏）"); };
                _bgm.MediaOpened += delegate
                {
                    ApplyBgmVolume();
                    SetStatus("预览音乐已开始播放（游戏音量=" + GamePrefs.ReadFloat("BGMVolume", 1f).ToString("0.00") +
                        (GamePrefs.ReadBool("BGMMute", false) ? "，静音" : "") + "）");
                };
                _bgm.Loaded += delegate { try { _bgm.Play(); } catch { } };
                ApplyBgmVolume();
            }
            catch (Exception ex) { SetStatus("预览音乐初始化失败：" + ex.Message); }
        }

        /** 把游戏音量设置套到预览音乐上（静音 = 0） */
        private void ApplyBgmVolume()
        {
            if (_bgm == null) return;
            try
            {
                bool mute = GamePrefs.ReadBool("BGMMute", false);
                float vol = GamePrefs.ReadFloat("BGMVolume", 1f);
                if (vol > 1f) vol = 1f;
                if (vol < 0f) vol = 0f;
                _bgm.IsMuted = mute;
                _bgm.Volume = vol;
            }
            catch { }
        }

        /** 进入游戏：停掉预览音乐 */
        private void StopPreviewBgm()
        {
            try { if (_bgm != null) { _bgm.Stop(); _bgm.Close(); } } catch { }
        }

        private string FindBackground()
        {
            string[] cands =
            {
                Path.Combine(_paths.ExeDir, @"assets\login_bg.jpg"),
                Path.Combine(_paths.ExeDir, @"assets\login_bg.png"),
            };
            foreach (var c in cands) if (File.Exists(c)) return c;
            return null;
        }

        /** 登录页大 LOGO（左栏顶部展示）；资源缺失时回退文字。 */
        private UIElement BuildTitleLogo(double height)
        {
            var logoPath = System.IO.Path.Combine(_paths.ExeDir, "assets", "title_logo.png");
            if (System.IO.File.Exists(logoPath))
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bmp.EndInit();
                    var img = new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    if (height > 0)
                    {
                        img.Height = height;
                        return img;
                    }
                    /** height<=0：用 Viewbox 填满所在行（左栏上半区），随窗口缩放 */
                    var vb = new System.Windows.Controls.Viewbox
                    {
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 6, 0, 6),
                        Child = img,
                    };
                    return vb;
                }
                catch { }
            }
            var t = Theme.Text("NineRE", 30, Theme.Gold, FontWeights.Bold, Theme.Serif);
            t.Margin = new Thickness(4, 10, 0, 10);
            return t;
        }

        private UIElement BuildChrome()
        {
            var g = new Grid { Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });



            /** 原「标题文字」位置：公告按钮（2026-10-30） */
            var announce = new Border
            {
                Height = 24,
                Padding = new Thickness(10, 0, 10, 0),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xC9, 0xA3, 0x5A)),
                Background = new SolidColorBrush(Color.FromArgb(0x66, 0x12, 0x14, 0x18)),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "公告",
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(Theme.GoldBright),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            announce.MouseEnter += delegate { ((TextBlock)announce.Child).Foreground = new SolidColorBrush(Theme.GoldBright); };
            announce.MouseLeave += delegate { ((TextBlock)announce.Child).Foreground = new SolidColorBrush(Theme.TextSecondary); };
            announce.MouseLeftButtonDown += delegate (object s5, MouseButtonEventArgs e5)
            {
                e5.Handled = true;
                try { AnnounceDialog.Show(this); } catch (Exception ex) { SetStatus("公告打开失败：" + ex.Message); }
            };
            g.Children.Add(announce);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(ChromeButton("—", delegate { WindowState = WindowState.Minimized; }));
            right.Children.Add(new Border { Width = 6 });
            right.Children.Add(ChromeButton("✕", delegate { Close(); }));
            Grid.SetColumn(right, 1);
            g.Children.Add(right);

            /**
             * 拖动窗口：**只认标题文字区域**。
             * 旧实现只要点在 Border/TextBlock 上就 DragMove()——而按钮本体是 Border、字形是
             * TextBlock，于是点「最小化/关闭」会先进入拖拽（鼠标被捕获），MouseLeftButtonUp
             * 根本回不到按钮上，按钮就"失效"了。
             */
            g.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (right.IsMouseOver) {
                    return;
                }
                try { DragMove(); } catch { }
            };
            return g;
        }

        private Border ChromeButton(string glyph, Action onClick)
        {
            var b = new Border
            {
                Width = 27,
                Height = 22,
                CornerRadius = new CornerRadius(3),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    /** 2026-10-30：背景是浅色登录画面，纯黑字 + 淡阴影才看得清 */
                    Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x10, 0x10, 0x10)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            /** 字形加淡阴影，压在浅色背景上也清楚 */
            var glyphText = b.Child as TextBlock;
            if (glyphText != null)
            {
                glyphText.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    ShadowDepth = 0,
                    BlurRadius = 4,
                    Opacity = 0.45,
                };
            }
            b.MouseEnter += delegate { b.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)); };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            /**
             * 用 MouseLeftButtonDown 并吃掉事件：按钮不再依赖 Up（拖拽会吞掉 Up），
             * 也阻止事件冒泡到标题栏触发 DragMove。
             */
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                onClick();
            };
            return b;
        }

        private OrnamentBox BuildAccountsPanel()
        {
            var box = new OrnamentBox(4, 0xB4);
            box.Padding = new Thickness(16, 14, 12, 14);

            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _accountCount = null;
            g.Children.Add(Theme.SectionTitle("账号"));

            _accountList = new StackPanel();
            var scroll = new ScrollViewer
            {
                Margin = new Thickness(0, 12, 0, 8),
                Content = _accountList,
            };
            /**
             * 2026-10-30 用户要求：滚动条**改细**，并且**只在滚动时显示**。
             * 做法：系统滚动条整体隐藏（VerticalScrollBarVisibility=Hidden，不占位），
             * 右侧自绘一条 4px 的金色滑块，按滚动比例定位；滚动/悬停时亮起，
             * 静止 900ms 后淡出。
             */
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

            var thumb = new Border
            {
                Width = 4,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromArgb(0x8C, 0xC9, 0xA3, 0x5A)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Opacity = 0,
                Margin = new Thickness(0, 0, 1, 0),
            };
            var host = new Grid { Margin = new Thickness(0, 12, 0, 8) };
            scroll.Margin = new Thickness(0);
            host.Children.Add(scroll);
            host.Children.Add(thumb);
            Grid.SetRow(host, 1);
            g.Children.Add(host);

            var fade = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            Action updateThumb = delegate
            {
                double content = scroll.ScrollableHeight + scroll.ViewportHeight;
                if (content <= scroll.ViewportHeight + 1)
                {
                    thumb.Opacity = 0;
                    return;
                }
                double ratio = scroll.ViewportHeight / content;
                double h = Math.Max(28, scroll.ViewportHeight * ratio);
                double maxTop = Math.Max(0, scroll.ViewportHeight - h);
                double top = scroll.ScrollableHeight <= 0 ? 0 : (scroll.VerticalOffset / scroll.ScrollableHeight) * maxTop;
                thumb.Height = h;
                thumb.Margin = new Thickness(0, top, 1, 0);
            };
            Action reveal = delegate
            {
                updateThumb();
                thumb.Opacity = 1;
                fade.Stop();
                fade.Start();
            };
            fade.Tick += delegate { fade.Stop(); thumb.BeginAnimation(UIElement.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))); };
            scroll.ScrollChanged += delegate { if (thumb.Opacity > 0 || scroll.IsMouseOver) reveal(); };
            scroll.MouseEnter += delegate { if (scroll.ScrollableHeight > 1) reveal(); };
            scroll.PreviewMouseWheel += delegate (object s0, MouseWheelEventArgs e0)
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e0.Delta);
                e0.Handled = true;
                reveal();
            };
            _accountScroll = scroll;

            var add = Theme.GhostButton("添加账号", delegate { AddAccountFlow(); });
            Grid.SetRow(add, 2);
            g.Children.Add(add);

            box.Child = g;
            return box;
        }

        private OrnamentBox BuildDetailPanel()
        {
            var box = new OrnamentBox(4, 0xB4);
            box.Padding = new Thickness(26, 22, 26, 22);

            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new StackPanel();
            _detailName = Theme.Text("未选择账号", 25, Theme.TextPrimary, FontWeights.Normal, Theme.Serif);
            _detailSub = Theme.Text("", 12, Theme.TextMuted);
            _detailSub.Margin = new Thickness(0, 6, 0, 0);
            head.Children.Add(_detailName);
            head.Children.Add(_detailSub);
            g.Children.Add(head);

            var div = Theme.HLine(Theme.Hairline);
            div.Margin = new Thickness(0, 20, 0, 6);
            Grid.SetRow(div, 1);
            g.Children.Add(div);

            var rows = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
            _statLevel = AddInfoRow(rows, "等级");
            _statTier = AddInfoRow(rows, "段位");
            _statStages = AddInfoRow(rows, "主线进度");
            _statCards = AddInfoRow(rows, "卡牌");
            _statMoney = AddInfoRow(rows, "金币");
            _statJade = AddInfoRow(rows, "玉");
            _statTime = AddInfoRow(rows, "游玩时长");
            _detailLast = AddInfoRow(rows, "上次游玩");
            Grid.SetRow(rows, 2);
            g.Children.Add(rows);

            /** 删除此账号：移到账号详情面板（用户要求，2026-10-30） */
            var footer = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            _deleteBtn = Theme.TextLink("删除此账号", Theme.TextMuted, delegate { DeleteAccountFlow(); });
            footer.Children.Add(_deleteBtn);
            Grid.SetRow(footer, 3);
            g.Children.Add(footer);

            box.Child = g;
            return box;
        }

        /// <summary>标签 + 值的一行（无卡片、无边框，靠发丝线分区）</summary>
        private TextBlock AddInfoRow(Panel parent, string label)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var l = Theme.Text(label, 12, Theme.TextMuted);
            l.Margin = new Thickness(0, 11, 0, 11);
            row.Children.Add(l);

            var v = Theme.Text("—", 13, Theme.TextPrimary);
            v.Margin = new Thickness(0, 11, 0, 11);
            Grid.SetColumn(v, 1);
            row.Children.Add(v);

            parent.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x3A, Theme.Hairline.R, Theme.Hairline.G, Theme.Hairline.B)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = row,
            });
            return v;
        }

        private OrnamentBox BuildActionPanel()
        {
            var box = new OrnamentBox(4, 0xB4);
            box.Padding = new Thickness(16, 12, 16, 12);

            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 画面设置
            g.Children.Add(Theme.SectionTitle("画面设置"));

            var sp = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
            var toggles = new StackPanel { Orientation = Orientation.Horizontal };
            _winToggle = MakeToggle("窗口化", delegate { ApplyScreenMode(0); });
            _fullToggle = MakeToggle("全屏", delegate { ApplyScreenMode(1); });
            toggles.Children.Add(_winToggle);
            toggles.Children.Add(new Border { Width = 8 });
            toggles.Children.Add(_fullToggle);
            sp.Children.Add(toggles);
            sp.Children.Add(new Border { Height = 7 });

            var resRow = new Grid();
            resRow.Children.Add(BuildResolutionPicker());
            sp.Children.Add(resRow);

            /** 画质（原登录页 Quality 1..4） */
            sp.Children.Add(new Border { Height = 9 });
            sp.Children.Add(Theme.Label("画质"));
            sp.Children.Add(new Border { Height = 5 });
            sp.Children.Add(BuildQualityRow());

            /** 画面开关（阴影/描边/看板娘按用户要求去掉） */
            sp.Children.Add(new Border { Height = 9 });
            sp.Children.Add(CompactToggle("抗锯齿", "AntiAliasing", true));
            sp.Children.Add(CompactToggle("泛光", "Bloom", true));
            sp.Children.Add(CompactToggle("高帧率", "FPSHigh", true));

            /** 音量（附文本提示：与游戏内一致，可直接试听） */
            sp.Children.Add(new Border { Height = 9 });
            sp.Children.Add(Theme.Label("音量"));
            sp.Children.Add(new Border { Height = 3 });
            sp.Children.Add(VolumeRow("音乐", "BGMVolume", "BGMMute"));
            sp.Children.Add(VolumeRow("音效", "SEVolume", "SEMute"));
            /** 文本提示放在两个音量条下方，右对齐（用户要求；不再显示百分比读数） */
            var volHint = Theme.Text("音量与游戏内一致，可直接试听", 10.5, Theme.TextMuted);
            volHint.Margin = new Thickness(0, 4, 0, 0);
            volHint.TextWrapping = TextWrapping.Wrap;
            volHint.TextAlignment = TextAlignment.Right;
            volHint.HorizontalAlignment = HorizontalAlignment.Right;
            sp.Children.Add(volHint);

            Grid.SetRow(sp, 1);
            g.Children.Add(sp);

            // 服务状态
            var svc = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            svc.Children.Add(Theme.HLine(Theme.Hairline));
            svc.Children.Add(new Border { Height = 8 });
            svc.Children.Add(Theme.SectionTitle("本地服务"));
            svc.Children.Add(new Border { Height = 4 });
            _svcPg = AddServiceRow(svc, "数据库");
            _svcSrv = AddServiceRow(svc, "服务器");
            _svcHttp = AddServiceRow(svc, "接口");
            svc.Children.Add(new Border { Height = 2 });
            _restartBtn = Theme.TextLink("重启服务器", Theme.TextMuted, delegate { RestartServerFlow(); });
            _restartBtn.HorizontalAlignment = HorizontalAlignment.Right;
            svc.Children.Add(_restartBtn);
            Grid.SetRow(svc, 2);
            g.Children.Add(svc);

            // 按钮
            var actions = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
            /** 更新启动器（占位：更新通道后续接入） */
            _updateBtn = Theme.GhostButton("更新启动器", delegate { CheckLauncherUpdate(); });
            _updateBtn.Height = 34;
            actions.Children.Add(_updateBtn);
            actions.Children.Add(new Border { Height = 8 });
            _enterBtn = Theme.PrimaryButton("进入游戏", delegate { EnterGameFlow(); });
            actions.Children.Add(_enterBtn);
            Grid.SetRow(actions, 3);
            g.Children.Add(actions);

            box.Child = g;
            return box;
        }

        /** 画质四选一（PlayerPrefs.Quality：1 标准 / 2 低 / 3 高 / 4 极致） */
        private UIElement BuildQualityRow()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            string[] names = { "标准", "低", "高", "极致" };
            int cur = GamePrefs.ReadInt("Quality", 1);
            var btns = new List<Border>();
            for (int i = 0; i < names.Length; i++)
            {
                int val = i + 1;
                var b = Theme.GhostButton(names[i], delegate { });
                b.Width = 56;
                b.Height = 26;
                b.Margin = new Thickness(0, 0, 6, 0);
                var tb0 = b.Child as TextBlock;
                if (tb0 != null) tb0.FontSize = 11.5;
                b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    GamePrefs.WriteInt("Quality", val);
                    foreach (var x in btns)
                    {
                        bool on = x == b;
                        x.BorderBrush = new SolidColorBrush(on ? Theme.Gold : Theme.Hairline);
                        var tb = x.Child as TextBlock;
                        if (tb != null) tb.Foreground = new SolidColorBrush(on ? Theme.GoldBright : Theme.TextPrimary);
                    }
                };
                if (val == cur)
                {
                    b.BorderBrush = new SolidColorBrush(Theme.Gold);
                    if (tb0 != null) tb0.Foreground = new SolidColorBrush(Theme.GoldBright);
                }
                btns.Add(b);
                row.Children.Add(b);
            }
            return row;
        }

        /** 紧凑开关行：左标签 + 右开/关 */
        private UIElement CompactToggle(string label, string prefKey, bool def)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = Theme.Text(label, 12, Theme.TextPrimary);
            l.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(l);

            var btn = Theme.GhostButton("开", delegate { });
            btn.Width = 46;
            btn.Height = 24;
            var tb0 = btn.Child as TextBlock;
            if (tb0 != null) tb0.FontSize = 11;
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
            return row;
        }

        /** 音量行：标签 + 滑条 + 静音 */
        private UIElement VolumeRow(string label, string volKey, string muteKey)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = Theme.Text(label, 12, Theme.TextPrimary);
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
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(slider, 1);
            g.Children.Add(slider);

            var mb = Theme.GhostButton("有声", delegate { });
            mb.Width = 46;
            mb.Height = 24;
            var tb0 = mb.Child as TextBlock;
            if (tb0 != null) tb0.FontSize = 11;
            Action refresh = delegate
            {
                var tb = mb.Child as TextBlock;
                if (tb == null) return;
                bool m = GamePrefs.ReadBool(muteKey, false);
                tb.Text = m ? "静音" : "有声";
                tb.Foreground = new SolidColorBrush(m ? Theme.Danger : Theme.TextMuted);
                mb.BorderBrush = new SolidColorBrush(m ? Theme.Danger : Theme.Hairline);
            };
            mb.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                GamePrefs.WriteBool(muteKey, !GamePrefs.ReadBool(muteKey, false));
                refresh();
                if (muteKey == "BGMMute") ApplyBgmVolume();
            };
            refresh();
            Grid.SetColumn(mb, 3);
            g.Children.Add(mb);

            slider.ValueChanged += delegate
            {
                try { GamePrefs.WriteFloat(volKey, (float)slider.Value); } catch { }
                if (volKey == "BGMVolume") ApplyBgmVolume();
            };
            return g;
        }

        private Border MakeToggle(string label, Action onClick)
        {
            var b = new Border
            {
                Height = 32,
                Width = 96,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 12.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            b.MouseLeftButtonUp += delegate { onClick(); };
            b.Tag = label;
            return b;
        }

        private void StyleToggle(Border b, bool active)
        {
            var text = (TextBlock)b.Child;
            if (active)
            {
                b.Background = new SolidColorBrush(Color.FromArgb(0x33, Theme.Gold.R, Theme.Gold.G, Theme.Gold.B));
                b.BorderBrush = new SolidColorBrush(Theme.Gold);
                text.Foreground = new SolidColorBrush(Theme.GoldBright);
            }
            else
            {
                b.Background = Brushes.Transparent;
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, Theme.Hairline.R, Theme.Hairline.G, Theme.Hairline.B));
                text.Foreground = new SolidColorBrush(Theme.TextSecondary);
            }
        }

        private UIElement BuildResolutionPicker()
        {
            var holder = new Grid();
            var btn = new Border
            {
                Height = 32,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, Theme.Hairline.R, Theme.Hairline.G, Theme.Hairline.B)),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _resLabel = new TextBlock
            {
                Text = "1920 × 1080",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Theme.TextSecondary),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(_resLabel);
            var chev = new TextBlock
            {
                Text = "▾",
                FontSize = 11,
                Foreground = new SolidColorBrush(Theme.TextMuted),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(chev, 1);
            row.Children.Add(chev);
            btn.Child = row;

            var popup = new Popup
            {
                PlacementTarget = btn,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
            };
            var list = new StackPanel();
            var popBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x14, 0x18, 0x1D)),
                BorderBrush = new SolidColorBrush(Theme.Hairline),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4),
                Child = list,
            };
            popup.Child = popBorder;

            var modes = new[]
            {
                new[] { "1920", "1080" }, new[] { "2560", "1440" }, new[] { "1600", "900" },
                new[] { "1366", "768" }, new[] { "1280", "720" },
            };
            foreach (var m in modes)
            {
                var item = new TextBlock
                {
                    Text = m[0] + " × " + m[1],
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(Theme.TextSecondary),
                    Padding = new Thickness(10, 7, 18, 7),
                };
                var ib = new Border { Child = item, Cursor = Cursors.Hand, CornerRadius = new CornerRadius(4) };
                int w = int.Parse(m[0]), h = int.Parse(m[1]);
                ib.MouseEnter += delegate { ib.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)); };
                ib.MouseLeave += delegate { ib.Background = Brushes.Transparent; };
                ib.MouseLeftButtonUp += delegate
                {
                    _resLabel.Text = w + " × " + h;
                    _store.SetScreen(_store.ScreenFull, w, h);
                    SaveStoreQuiet();
                    popup.IsOpen = false;
                };
                list.Children.Add(ib);
            }
            btn.MouseLeftButtonUp += delegate { popup.IsOpen = !popup.IsOpen; };
            holder.Children.Add(btn);
            holder.Children.Add(popup);
            return holder;
        }

        private TextBlock AddServiceRow(StackPanel parent, string label)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = Theme.Text(label, 12, Theme.TextSecondary);
            row.Children.Add(l);
            var v = Theme.Text("—", 12, Theme.TextMuted, FontWeights.SemiBold);
            Grid.SetColumn(v, 1);
            row.Children.Add(v);
            parent.Children.Add(row);
            return v;
        }

        private Border BuildConsole()
        {
            _consoleBody = new Border
            {
                Height = 0,
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x0C, 0x0E, 0x11)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, Theme.Hairline.R, Theme.Hairline.G, Theme.Hairline.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
            };

            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var head = new Grid { Margin = new Thickness(14, 0, 10, 0) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var clickHead = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            _consoleChevron = Theme.Text("▸", 11, Theme.TextMuted);
            clickHead.Children.Add(_consoleChevron);
            clickHead.Children.Add(new Border { Width = 8 });
            clickHead.Children.Add(Theme.Text("服务端控制台", 12.5, Theme.TextPrimary, FontWeights.SemiBold));
            clickHead.MouseLeftButtonUp += delegate { ToggleConsole(); };
            head.Children.Add(clickHead);

            var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            _retryBtn = new Border
            {
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 12, 0),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Theme.GoldDim),
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock { Text = "重试启动服务", FontSize = 11.5, Foreground = new SolidColorBrush(Theme.GoldBright) },
            };
            _retryBtn.MouseLeftButtonUp += delegate { StartServicesAsync(); };
            tabs.Children.Add(_retryBtn);
            tabs.Children.Add(ConsoleTab("服务端", "server"));
            tabs.Children.Add(new Border { Width = 6 });
            tabs.Children.Add(ConsoleTab("数据库", "pg"));
            Grid.SetColumn(tabs, 2);
            head.Children.Add(tabs);

            g.Children.Add(head);

            _consoleText = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromArgb(0xEE, 0xD8, 0xD2, 0xC4)),
                FontFamily = Theme.Mono,
                FontSize = 11.5,
                Margin = new Thickness(14, 0, 10, 10),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap,
            };
            Grid.SetRow(_consoleText, 1);
            g.Children.Add(_consoleText);

            _consoleBody.Child = g;
            return _consoleBody;
        }

        private Border ConsoleTab(string label, string mode)
        {
            var b = new Border
            {
                Padding = new Thickness(10, 3, 10, 3),
                CornerRadius = new CornerRadius(4),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = label, FontSize = 11.5, Foreground = new SolidColorBrush(Theme.TextMuted) },
            };
            b.MouseLeftButtonUp += delegate { _consoleMode = mode; RefreshConsole(); };
            b.Tag = label;
            return b;
        }

        private Border BuildStatusBar()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _statusText = Theme.Text("准备中…", 11.5, Theme.TextSecondary);
            _statusText.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(_statusText);
            var ver = Theme.Text("NineRE · 启动器", 11, Theme.TextMuted);
            ver.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(ver, 1);
            g.Children.Add(ver);
            return new Border { Child = g };
        }

        // ───────────────────────────── 数据刷新 ─────────────────────────────

        private void LoadAccounts()
        {
            _store.Load();
            if (_selected == null || _store.Find(_selected) == null)
                _selected = _store.Accounts.Count > 0 ? _store.Accounts[0].Name : null;
            RefreshAccounts();
            RefreshDetail();
            ApplyScreenToggles();
            if (_resLabel != null && _store.ScreenW > 0 && _store.ScreenH > 0)
                _resLabel.Text = _store.ScreenW + " × " + _store.ScreenH;
        }

        private void RefreshAccounts()
        {
            if (_accountList == null) return;
            _accountList.Children.Clear();
            if (_accountCount != null) _accountCount.Text = "";

            if (_store.Accounts.Count == 0)
            {
                var empty = Theme.Text("还没有账号。点下面的「添加账号」，第一次进入会自动建号。", 12, Theme.TextMuted);
                empty.TextWrapping = TextWrapping.Wrap;
                empty.Margin = new Thickness(2, 8, 2, 0);
                _accountList.Children.Add(empty);
                return;
            }

            if (_accountScroll != null)
            {
                Dispatcher.BeginInvoke(new Action(delegate { _accountScroll.UpdateLayout(); }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            foreach (var a in _store.Accounts)
            {
                bool sel = a.Name == _selected;
                var row = new Border
                {
                    Margin = new Thickness(0, 0, 4, 2),
                    Background = new SolidColorBrush(sel
                        ? Color.FromArgb(0x30, 0xE8, 0xD2, 0xA0)
                        : Colors.Transparent),
                    Cursor = Cursors.Hand,
                };

                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var bar = new Border
                {
                    Background = new SolidColorBrush(sel ? Theme.Gold : Colors.Transparent),
                };
                g.Children.Add(bar);

                var col = new StackPanel { Margin = new Thickness(12, 10, 8, 10) };
                var nameText = Theme.Text(a.Name, 13.5, sel ? Theme.TextPrimary : Theme.TextSecondary,
                    sel ? FontWeights.SemiBold : FontWeights.Normal);
                col.Children.Add(nameText);

                AccountStat st;
                string sub;
                if (Stats.TryGet(_stats, a.Name, out st) && st.Ok)
                    sub = (st.Tier > 0 ? Stats.RankName(st.Tier) : "未定位") + " · 卡牌 " + st.Species;
                else
                    sub = "—";
                var subText = Theme.Text(sub, 11, Theme.TextMuted);
                subText.Margin = new Thickness(0, 3, 0, 0);
                col.Children.Add(subText);

                Grid.SetColumn(col, 1);
                g.Children.Add(col);

                var right = Theme.Text(FormatPlaytime(a.Secs), 10.5, Theme.TextMuted);
                right.VerticalAlignment = VerticalAlignment.Center;
                right.Margin = new Thickness(0, 0, 6, 0);
                Grid.SetColumn(right, 2);
                g.Children.Add(right);

                var stack = new StackPanel();
                stack.Children.Add(g);
                stack.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(12, 0, 6, 0),
                    Background = new SolidColorBrush(Color.FromArgb(0x22, 0xC9, 0xA3, 0x5A)),
                });
                row.Child = stack;
                row.MouseEnter += delegate
                {
                    if (a.Name != _selected)
                        row.Background = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
                };
                row.MouseLeave += delegate
                {
                    if (a.Name != _selected)
                        row.Background = Brushes.Transparent;
                };
                row.MouseLeftButtonUp += delegate
                {
                    _selected = a.Name;
                    RefreshAccounts();
                    RefreshDetail();
                };
                _accountList.Children.Add(row);
            }
        }

        private void RefreshDetail()
        {
            var a = _selected == null ? null : _store.Find(_selected);
            if (a == null)
            {
                _detailName.Text = "未选择账号";
                _detailSub.Text = "在左侧选择账号，或点「添加账号」";
                _statTier.Text = "—";
                _statCards.Text = "—";
                _statTime.Text = "—";
                if (_detailLast != null) _detailLast.Text = "—";
                return;
            }

            _detailName.Text = a.Name;
            _detailSub.Text = string.IsNullOrEmpty(a.Uid) ? "UID 待分配" : "UID " + a.Uid;
            _statTime.Text = FormatPlaytime(a.Secs);
            if (_detailLast != null) _detailLast.Text = FormatDate(a.Last);

            AccountStat st;
            if (Stats.TryGet(_stats, a.Name, out st) && st.Ok)
            {
                _statLevel.Text = st.Level > 0 ? "Lv." + st.Level : "—";
                _statTier.Text = st.Tier > 0
                    ? Stats.RankName(st.Tier) + (st.LadderStar > 0 ? "（" + st.LadderStar + " 星）" : "")
                    : "未定位";
                _statStages.Text = st.Stages > 0 ? st.Stages + " 关" : "未开始";
                _statCards.Text = st.Species + " 种 / " + st.Copies + " 张";
                _statMoney.Text = st.Money.ToString("N0");
                _statJade.Text = st.Jade.ToString("N0");
            }
            else
            {
                if (_statLevel != null) _statLevel.Text = "—";
                _statTier.Text = "—";
                if (_statStages != null) _statStages.Text = "—";
                _statCards.Text = "—";
                if (_statMoney != null) _statMoney.Text = "—";
                if (_statJade != null) _statJade.Text = "—";
            }
        }

        private static string FormatPlaytime(long secs)
        {
            if (secs < 60) return "不足 1 分钟";
            long m = secs / 60;
            if (m < 60) return m + " 分钟";
            long h = m / 60;
            m = m % 60;
            return m > 0 ? h + " 小时 " + m + " 分" : h + " 小时";
        }

        private static string FormatDate(long unix)
        {
            if (unix <= 0) return "—";
            try { return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("MM-dd HH:mm"); }
            catch { return "—"; }
        }

        private void RefreshServices()
        {
            if (_svcPg == null) return;
            SetDot(_svcPg, _state.Postgres);
            SetDot(_svcSrv, _state.Server);
            SetDot(_svcHttp, _state.Http);

            bool ready = _state.AllReady;
            if (_retryBtn != null)
                _retryBtn.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
            if (_enterBtn != null)
            {
                /**
                 * 2026-10-30 用户要求：**服务完全就绪后按钮才"亮起"**。
                 * 未就绪：压暗到 30%、无光标手型、文案提示等待；
                 * 就绪：恢复满亮 + 金色外发光（一眼看出可以进了）。
                 */
                _enterBtn.Opacity = ready ? 1.0 : 0.30;
                _enterBtn.Cursor = ready ? Cursors.Hand : Cursors.Arrow;
                if (ready)
                {
                    _enterBtn.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Color.FromRgb(0xE4, 0xC2, 0x7E),
                        ShadowDepth = 0,
                        BlurRadius = 18,
                        Opacity = 0.55,
                    };
                }
                else
                {
                    _enterBtn.Effect = null;
                }
                var t = (TextBlock)_enterBtn.Child;
                t.Text = ready ? "进入游戏" : "等待服务就绪…";
            }
        }

        private void SetDot(TextBlock t, bool ok)
        {
            t.Text = ok ? "就绪" : "未就绪";
            t.Foreground = new SolidColorBrush(ok ? Theme.Ok : Theme.Danger);
        }

        private void ApplyScreenToggles()
        {
            if (_winToggle == null) return;
            bool full = _store.ScreenFull == 1;
            StyleToggle(_winToggle, !full);
            StyleToggle(_fullToggle, full);
        }

        private void ApplyScreenMode(int full)
        {
            if (Launch.GameRunning()) { SetStatus("游戏运行中，画面设置请在游戏内修改"); return; }
            _store.SetScreen(full, _store.ScreenW, _store.ScreenH);
            ApplyScreenToggles();
            SaveStoreQuiet();
        }

        private void SaveStoreQuiet()
        {
            var failed = _store.Save();
            if (failed.Length > 0) SetStatus("账号库写入失败：" + failed[0]);
        }

        // ───────────────────────────── 服务 ─────────────────────────────

        private void StartServicesAsync()
        {
            SetStatus("准备本地服务…");
            Task.Run(() =>
            {
                var st = _services.EnsureAll(delegate (string msg) { Dispatcher.BeginInvoke(new Action(delegate { SetStatus(msg); })); });
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _state = st;
                    RefreshServices();
                    if (st.AllReady) SetStatus("本地服务已就绪");
                    else
                    {
                        SetStatus(st.Message);
                        if (!_consoleOpen) ToggleConsole();
                        RefreshConsole();
                    }
                    RefreshStatsAsync(force: true);
                }));
            });
        }

        /// <summary>重启游戏服（游戏在跑时先确认，重启会断开当前登录）</summary>
        private void RestartServerFlow()
        {
            if (_busy) return;
            if (Launch.GameRunning())
            {
                bool ok = ConfirmDialog.Ask(this, "重启游戏服",
                    "游戏正在运行，重启会把当前登录断开（角色数据已落库，不会丢档）。\n确定重启吗？");
                if (!ok) return;
            }
            _busy = true;
            _svcSrv.Text = "重启中…";
            _svcSrv.Foreground = new SolidColorBrush(Theme.GoldBright);
            SetStatus("正在重启游戏服…");
            Task.Run(delegate
            {
                var st = _services.RestartServer(delegate (string msg)
                {
                    Dispatcher.BeginInvoke(new Action(delegate { SetStatus(msg); }));
                });
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _busy = false;
                    _state = st;
                    RefreshServices();
                    RefreshStatsAsync(force: true);
                    SetStatus(st.AllReady ? "游戏服已重启并就绪" : ("重启失败：" + st.Message));
                    if (!st.AllReady)
                    {
                        if (!_consoleOpen) ToggleConsole();
                        RefreshConsole();
                    }
                }));
            });
        }

        private void RefreshStatsAsync(bool force)
        {
            if (!force && (DateTime.UtcNow - _statsAt).TotalSeconds < 25) return;
            if (!_state.Http) return;
            _statsAt = DateTime.UtcNow;
            Task.Run(() =>
            {
                var map = Stats.Fetch(1500);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _stats = map;
                    /** ★ 2026-10-30：palrstats 现在带 UID，首次拿到就回填账号并落盘 */
                    bool changed = false;
                    foreach (var a in _store.Accounts)
                    {
                        AccountStat st2;
                        if (string.IsNullOrEmpty(a.Uid) && Stats.TryGet(map, a.Name, out st2) && st2.Ok && st2.Uid > 0)
                        {
                            a.Uid = st2.Uid.ToString();
                            changed = true;
                        }
                    }
                    if (changed) { try { _store.Save(); } catch { } }
                    RefreshAccounts();
                    RefreshDetail();
                    /** 启动器为主表：启动后对账一次（只在本机名单非空时做，防呆见服务端 pruneAccounts） */
                    if (!_reconciled && _store.Accounts.Count > 0)
                    {
                        _reconciled = true;
                        var names = _store.Accounts.ConvertAll(x => x.Name);
                        Task.Run(() =>
                        {
                            var removed = Stats.Reconcile(names, 2000);
                            if (!string.IsNullOrEmpty(removed))
                            {
                                Dispatcher.BeginInvoke(new Action(delegate { SetStatus("已清理服务端多余账号：" + removed.Replace(Environment.NewLine, "、")); }));
                            }
                        });
                    }
                }));
            });
        }

        private void OnTick(object sender, EventArgs e)
        {
            // 轻量检查（不阻塞 UI）：只探端口
            bool pg = Services.PortOpen(Services.PgPort, 120);
            bool ws = Services.PortOpen(Services.WsPort, 120);
            bool http = Services.PortOpen(Services.HttpPort, 120);
            _state.Postgres = pg;
            _state.Server = ws && http;
            _state.Http = http;
            RefreshServices();

            if (_consoleOpen) RefreshConsole();
            /**
             * 服务 30 秒还没起来 → 提示去跑「环境检查.bat」（2026-10-30）。
             * 目标电脑可能缺 .NET/VC++ 运行库或端口被占用，启动器自己没能力修。
             */
            if (!_state.AllReady && !_serviceHintShown && (DateTime.UtcNow - _startedAt).TotalSeconds > 30)
            {
                _serviceHintShown = true;
                string reason = _state.Message ?? "未知原因";
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        InfoDialog.Show(this, "本地服务未就绪",
                            "数据库或服务器没有启动成功（" + reason + "）。" + Environment.NewLine +
                            "请先双击便携包根目录的「环境检查.bat」，按提示补齐运行环境后重试。");
                    }
                    catch { }
                }));
            }

            if (_game != null)
            {
                PollLoginState();
                if (_game.HasExited)
                {
                    _game = null;
                    _enteredGame = false;
                    WindowState = WindowState.Normal;
                    Activate();
                    _store.Load();
                    RefreshAccounts();
                    RefreshDetail();
                    RefreshStatsAsync(force: true);
                    SetStatus("游戏已退出");
                }
            }
        }

        private void PollLoginState()
        {
            if (_enteredGame) return;
            string st = Launch.ReadState(_paths);
            if (string.IsNullOrEmpty(st)) return;
            var parts = st.Split('\t');
            string phase = parts[0];
            string name = parts.Length > 1 ? parts[1] : "";
            if (phase == "ok")
            {
                _enteredGame = true;
                SetStatus("已进入江湖：" + name);
            }
            else if (phase == "failed")
            {
                SetStatus("自动登录失败，请在游戏内手动登录");
            }
        }

        // ───────────────────────────── 交互 ─────────────────────────────

        private void EnterGameFlow()
        {
            if (_busy) return;
            if (!_state.AllReady) { SetStatus("服务还没就绪：" + (_state.Message ?? "稍候")); return; }
            if (Launch.GameRunning()) { SetStatus("游戏已在运行"); return; }
            var a = _selected == null ? null : _store.Find(_selected);
            if (a == null) { AddAccountFlow(); return; }

            _busy = true;
            StopPreviewBgm();
            try
            {
                var failed = Launch.WriteIntent(_paths, a.Name, a.Pwd);
                if (failed.Length > 0) { SetStatus("启动凭据写入失败：" + failed[0]); return; }
                Launch.ClearState(_paths);
                var failedStore = _store.Save();
                if (failedStore.Length > 0) SetStatus("账号库写入失败（继续启动）：" + failedStore[0]);

                SetStatus("正在启动游戏…");
                _game = Launch.StartGame(_paths);
                _game.EnableRaisingEvents = true;
                _enteredGame = false;
                NativeWindows.WatchAndHideDummy(_game.Id);
                WindowState = WindowState.Minimized;
            }
            catch (Exception ex)
            {
                SetStatus("启动失败：" + ex.Message);
            }
            finally { _busy = false; }
        }

        private void AddAccountFlow()
        {
            var dlg = new AccountDialog(this, null);
            if (dlg.ShowDialog() != true) return;
            string name = dlg.AccountName.Trim();
            if (name.Length == 0) return;
            _store.Upsert(name, dlg.AccountPassword);
            var failed = _store.Save();
            if (failed.Length > 0) { SetStatus("账号写入失败：" + failed[0]); return; }
            _selected = name;
            RefreshAccounts();
            RefreshDetail();
            SetStatus("已添加账号 " + name + "，点「进入游戏」首次登录会自动建号");
        }

        private void DeleteAccountFlow()
        {
            var a = _selected == null ? null : _store.Find(_selected);
            if (a == null) { SetStatus("先选择一个账号"); return; }
            if (Launch.GameRunning()) { SetStatus("游戏运行中不能删除账号"); return; }
            var ok = ConfirmDialog.Ask(this, "删除账号",
                "从本机列表移除「" + a.Name + "」？\n服务端存档不会被删除，重新添加同名账号即可找回进度。");
            if (!ok) return;
            _store.Remove(a.Name);
            var failed = _store.Save();
            if (failed.Length > 0) { SetStatus("删除失败：" + failed[0]); return; }
            _selected = _store.Accounts.Count > 0 ? _store.Accounts[0].Name : null;
            RefreshAccounts();
            RefreshDetail();
            SetStatus("已移除 " + a.Name + "（服务端存档保留）");
            /** 启动器为主表：同步删掉服务端账号表里的同名条目 */
            Task.Run(() =>
            {
                var names = _store.Accounts.ConvertAll(x => x.Name);
                var removed = Stats.Reconcile(names, 2000);
                if (!string.IsNullOrEmpty(removed))
                {
                    Dispatcher.BeginInvoke(new Action(delegate { SetStatus("已同步删除服务端账号：" + removed.Replace(Environment.NewLine, "、")); }));
                }
            });
        }

        // ───────────────────────────── 控制台 ─────────────────────────────

        private void ToggleConsole()
        {
            _consoleOpen = !_consoleOpen;
            _consoleBody.BeginAnimation(HeightProperty, new DoubleAnimation(_consoleOpen ? 186 : 0, TimeSpan.FromMilliseconds(140)));
            _consoleChevron.Text = _consoleOpen ? "▾" : "▸";
            if (_consoleOpen) RefreshConsole();
        }

        private void RefreshConsole()
        {
            if (_consoleText == null) return;
            string path = _consoleMode == "pg" ? _paths.PgLog : _paths.RunLog;
            string text = Services.Tail(path, 200, 64 * 1024);
            if (!_consoleOpen) return;
            if (_consoleText.Text != text)
            {
                _consoleText.Text = text;
                _consoleText.ScrollToEnd();
            }
        }

        /** 底栏已删除（用户要求不留版权信息）：状态改为写启动器日志，信息不丢。 */
        /** 更新启动器（占位实现：后续接更新通道） */
        private void CheckLauncherUpdate()
        {
            SetStatus("检查启动器更新…");
            try
            {
                InfoDialog.Show(this, "更新启动器",
                    "当前已是最新版本。自动更新通道尚未接入，后续版本可在此直接下载并替换。");
            }
            catch (Exception ex) { SetStatus("更新检查失败：" + ex.Message); }
        }

        private void SetStatus(string msg)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(_paths.ExeDir, "launcher.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + msg + Environment.NewLine);
            }
            catch { }
        }
    }
}
