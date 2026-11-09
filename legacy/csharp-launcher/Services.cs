using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PalrLauncher
{
    public sealed class ServiceState
    {
        public bool Postgres;
        public bool Server;      // 8800 监听
        public bool Http;        // 4010 /ver 可查
        public string Message = "";
        public bool AllReady { get { return Postgres && Server && Http; } }
    }

    /// <summary>
    /// 本地服务：PostgreSQL(5432) + 游戏服(8800/4010)。
    /// 就绪判定不只「端口在听」：PG 用 pg_isready，服务端用 HTTP /ver 返回 200。
    /// </summary>
    public sealed class Services
    {
        public const int PgPort = 5432;
        public const int WsPort = 8800;
        public const int HttpPort = 4010;
        public const string VerUrl = "http://127.0.0.1:4010/r/1.0.50/ver";

        private readonly Paths _paths;
        public Services(Paths paths) { _paths = paths; }

        public static bool PortOpen(int port, int timeoutMs)
        {
            try
            {
                using (var c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect("127.0.0.1", port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    c.EndConnect(ar);
                    return true;
                }
            }
            catch { return false; }
        }

        public static bool HttpOk(string url, int timeoutMs)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.Proxy = null;
                req.KeepAlive = false;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    return (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 500;
                }
            }
            catch { return false; }
        }

        public bool PgReady()
        {
            string exe = Path.Combine(_paths.PgBin, "pg_isready.exe");
            if (!File.Exists(exe)) return PortOpen(PgPort, 400);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "-h 127.0.0.1 -p " + PgPort,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = _paths.PgBin,
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(5000);
                    return p.HasExited && p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        public bool EnsurePostgres(ServiceState st)
        {
            if (PgReady()) { st.Postgres = true; return true; }
            if (!File.Exists(Path.Combine(_paths.PgBin, "pg_ctl.exe")) || !Directory.Exists(_paths.PgData))
            {
                st.Message = "找不到 PostgreSQL（" + _paths.PgBin + "）或数据目录（" + _paths.PgData + "）";
                return false;
            }
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(_paths.PgBin, "pg_ctl.exe"),
                    Arguments = string.Format("-D \"{0}\" -l \"{1}\" -o \"-p {2}\" start", _paths.PgData, _paths.PgLog, PgPort),
                    WorkingDirectory = _paths.PgBin,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
            }
            catch (Exception ex) { st.Message = "数据库启动失败：" + ex.Message; return false; }

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 60000)
            {
                if (PgReady()) { st.Postgres = true; return true; }
                Thread.Sleep(400);
            }
            st.Message = "数据库 60 秒内未就绪（看 pgdata.log）";
            return false;
        }

        public bool EnsureServer(ServiceState st)
        {
            if (HttpOk(VerUrl, 1200) && PortOpen(WsPort, 400)) { st.Server = true; st.Http = true; return true; }
            if (!File.Exists(_paths.ServerExe))
            {
                st.Message = "找不到服务端：" + _paths.ServerExe;
                return false;
            }
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"\"" + _paths.ServerExe + "\" >> run.log 2>&1\"",
                    WorkingDirectory = _paths.ServerDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
            }
            catch (Exception ex) { st.Message = "服务端启动失败：" + ex.Message; return false; }

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 60000)
            {
                if (HttpOk(VerUrl, 1200) && PortOpen(WsPort, 400)) { st.Server = true; st.Http = true; return true; }
                Thread.Sleep(500);
            }
            st.Message = "服务端 60 秒内未就绪（看 run.log）";
            return false;
        }

        /// <summary>结束游戏服进程（按进程名；不动数据库）。返回结束的进程数。</summary>
        public int KillServer()
        {
            int killed = 0;
            try
            {
                foreach (var p in Process.GetProcessesByName("nine-regions-backend"))
                {
                    try { p.Kill(); p.WaitForExit(5000); killed++; }
                    catch { }
                }
            }
            catch { }
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 8000 && (PortOpen(WsPort, 200) || PortOpen(HttpPort, 200)))
                Thread.Sleep(300);
            return killed;
        }

        /**
         * 关闭启动器时调用（2026-10-30 用户要求）：**停掉全部本地服务**——
         * 先杀游戏服进程，再用 pg_ctl fast 停数据库。全程吞异常，超时也有上限。
         */
        public void StopAll()
        {
            try { KillServer(); } catch { }
            try
            {
                string pgCtl = Path.Combine(_paths.PgBin, "pg_ctl.exe");
                if (File.Exists(pgCtl) && Directory.Exists(_paths.PgData))
                {
                    var psi = new ProcessStartInfo(pgCtl, "-D \"" + _paths.PgData + "\" -m fast stop")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    using (var proc = Process.Start(psi))
                    {
                        if (proc != null) proc.WaitForExit(15000);
                    }
                }
            }
            catch { }
        }

        /// <summary>重启游戏服：停→等端口释放→重新拉起→健康检查。数据库不动（不健康时顺带拉起）。</summary>
        public ServiceState RestartServer(Action<string> progress)
        {
            var st = new ServiceState();
            if (progress != null) progress("正在停止游戏服…");
            KillServer();
            if (progress != null) progress("正在启动游戏服…");
            st.Postgres = PgReady();
            if (!st.Postgres) EnsurePostgres(st);
            st.Message = "";
            EnsureServer(st);
            if (st.AllReady) st.Message = "";
            return st;
        }

        public ServiceState Check()
        {
            var st = new ServiceState();
            st.Postgres = PgReady();
            st.Http = HttpOk(VerUrl, 1200);
            st.Server = st.Http && PortOpen(WsPort, 400);
            if (!st.Postgres) st.Message = "数据库未就绪";
            else if (!st.Http) st.Message = "游戏服未就绪";
            else if (!st.Server) st.Message = "游戏服 WS 未监听";
            return st;
        }

        public ServiceState EnsureAll(Action<string> progress)
        {
            var st = new ServiceState();
            if (progress != null) progress("检查本地服务…");
            if (PgReady()) st.Postgres = true;
            else
            {
                if (progress != null) progress("正在启动数据库…");
                EnsurePostgres(st);
            }
            if (st.Postgres && !st.Message.StartsWith("找不到"))
            {
                if (HttpOk(VerUrl, 1200) && PortOpen(WsPort, 400))
                {
                    st.Http = st.Server = true;
                }
                else
                {
                    if (progress != null) progress("正在启动游戏服…");
                    st.Message = "";
                    EnsureServer(st);
                }
            }
            if (st.AllReady) st.Message = "";
            return st;
        }

        /// <summary>读取日志尾部（最多 maxLines 行），失败返回空串。</summary>
        public static string Tail(string path, int maxLines, long maxBytes)
        {
            try
            {
                if (!File.Exists(path)) return "";
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = Math.Max(0, fs.Length - maxBytes);
                    fs.Seek(start, SeekOrigin.Begin);
                    var buf = new byte[fs.Length - start];
                    int read = fs.Read(buf, 0, buf.Length);
                    string text = Encoding.UTF8.GetString(buf, 0, read);
                    string[] lines = text.Replace("\r\n", "\n").Split('\n');
                    int skip = Math.Max(0, lines.Length - maxLines);
                    var sb = new StringBuilder();
                    for (int i = skip; i < lines.Length; i++)
                    {
                        if (lines[i].Length == 0) continue;
                        sb.AppendLine(lines[i]);
                    }
                    return sb.ToString();
                }
            }
            catch { return ""; }
        }
    }
}
