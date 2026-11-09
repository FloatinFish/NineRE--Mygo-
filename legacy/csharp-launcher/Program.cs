using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace PalrLauncher
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        private static int Main(string[] args)
        {
            bool made = false;
            try
            {
                _mutex = new Mutex(true, @"Global\PALRLauncher", out made);
                if (!made)
                {
                    MessageBox.Show("NineRE Launcher 已经在运行了。", "NineRE Launcher",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return 0;
                }

                // 无界面模式：--restart-server 只重启游戏服（供脚本/排错用）
                if (Array.IndexOf(args, "--restart-server") >= 0)
                {
                    var paths = Paths.Detect();
                    if (paths.Mode == LayoutMode.Unknown)
                    {
                        Log("--restart-server：找不到游戏目录");
                        return 2;
                    }
                    var svc = new Services(paths);
                    var st = svc.RestartServer(delegate (string msg) { Log("[restart] " + msg); });
                    Log("[restart] 结果 PostgreSQL=" + st.Postgres + " 服务端=" + st.Server + " " + st.Message);
                    return st.AllReady ? 0 : 1;
                }

                var app = new Application();
                app.DispatcherUnhandledException += delegate (object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    Log("UI 异常：" + e.Exception);
                    e.Handled = true;
                };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
                {
                    Log("未处理异常：" + e.ExceptionObject);
                };

                Log("===== 启动器开始 =====");
                var win = new MainWindow();
                Log("MainWindow 构造完成，Content=" + (win.Content == null ? "null" : win.Content.GetType().Name));
                if (win.Content == null)
                {
                    // 探测失败：MainWindow 已给出说明界面，正常显示
                }
                win.Closed += delegate { Log("窗口已关闭"); };
                int rc = app.Run(win);
                Log("app.Run 返回 " + rc);
                return rc;
            }
            catch (Exception ex)
            {
                Log("启动失败：" + ex);
                MessageBox.Show("启动器启动失败：" + ex.Message, "NineRE Launcher",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            finally
            {
                if (made) _mutex.ReleaseMutex();
            }
        }

        private static void Log(string msg)
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                File.AppendAllText(Path.Combine(dir, "launcher.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + msg + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
