using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PalrLauncher
{
    public static class Launch
    {
        /// <summary>percent-encode：name/pwd 里可能有空格、中文、制表符</summary>
        public static string Encode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(s))
            {
                char c = (char)b;
                bool safe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                            || c == '-' || c == '_' || c == '.' || c == '~';
                if (safe) sb.Append(c);
                else sb.Append('%').Append(b.ToString("X2"));
            }
            return sb.ToString();
        }

        public static bool GameRunning()
        {
            try { return Process.GetProcessesByName("MC").Length > 0; }
            catch { return false; }
        }

        public static Process StartGame(Paths paths)
        {
            var psi = new ProcessStartInfo
            {
                FileName = paths.McExe,
                WorkingDirectory = paths.GameDir,
                UseShellExecute = false,
            };
            return Process.Start(psi);
        }

        /// <summary>写启动凭据（双份，先写 .tmp 再替换）</summary>
        public static string[] WriteIntent(Paths paths, string name, string pwd)
        {
            string nonce = Guid.NewGuid().ToString("N").Substring(0, 12);
            long t = Store.Now();
            string text = string.Format("V\t1\nT\t{0}\nN\t{1}\nA\t{2}\nP\t{3}\n",
                t, nonce, Encode(name), Encode(pwd ?? ""));

            var failed = new System.Collections.Generic.List<string>();
            foreach (var path in new[] { paths.IntentGame, paths.IntentLocalLow })
            {
                if (string.IsNullOrEmpty(path)) continue;
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, text, new UTF8Encoding(false));
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                }
                catch (Exception ex) { failed.Add(path + "：" + ex.Message); }
            }
            return failed.ToArray();
        }

        public static void ClearState(Paths paths)
        {
            foreach (var p in new[] { paths.StateGame, paths.StateLocalLow })
            {
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            }
        }

        /// <summary>读取热修写回的状态："phase\tunix\tname"（读不到返回空串）</summary>
        public static string ReadState(Paths paths)
        {
            string best = "";
            long bestT = -1;
            foreach (var p in new[] { paths.StateGame, paths.StateLocalLow })
            {
                try
                {
                    if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
                    string text = File.ReadAllText(p, Encoding.UTF8);
                    foreach (var raw in text.Split('\n'))
                    {
                        if (!raw.StartsWith("S\t")) continue;
                        string[] c = raw.TrimEnd('\r').Split('\t');
                        if (c.Length < 5) continue;
                        long t;
                        long.TryParse(c[2], out t);
                        if (t >= bestT) { bestT = t; best = c[1] + "\t" + c[4]; }
                    }
                }
                catch { }
            }
            return best;
        }
    }
}

namespace PalrLauncher
{
    /// <summary>
    /// 隐藏游戏进程自己创建的小窗口（标题 Dummy，约 1223×639）。
    /// 它是客户端启动/视频播放时挂出来的辅助窗口，会挡在桌面上；隐藏后不影响渲染。
    /// </summary>
    public static class NativeWindows
    {
        private delegate bool EnumProc(IntPtr h, IntPtr l);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr h, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr h);

        public static int HideDummyWindows(int pid)
        {
            int hidden = 0;
            try
            {
                EnumWindows(delegate (IntPtr h, IntPtr l)
                {
                    int p;
                    GetWindowThreadProcessId(h, out p);
                    if (p == pid)
                    {
                        var sb = new System.Text.StringBuilder(256);
                        GetWindowText(h, sb, 256);
                        if (sb.ToString() == "Dummy")
                        {
                            ShowWindow(h, 0);
                            hidden++;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return hidden;
        }

        /// <summary>后台盯 2 分钟：游戏启动过程中随时冒出来的 Dummy 窗口都收掉</summary>
        public static void WatchAndHideDummy(int pid)
        {
            System.Threading.Tasks.Task.Run(delegate
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 120000)
                {
                    HideDummyWindows(pid);
                    System.Threading.Thread.Sleep(700);
                }
            });
        }
    }
}
