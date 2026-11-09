using System;
using System.IO;

namespace PalrLauncher
{
    public enum LayoutMode { Portable, Dev, Unknown }

    /// <summary>
    /// 目录探测：同一份 exe 既能在便携包根目录跑，也能在开发仓库里跑。
    /// </summary>
    public sealed class Paths
    {
        public LayoutMode Mode = LayoutMode.Unknown;
        public string Root = "";
        public string GameDir = "";
        public string ServerDir = "";
        public string PgBin = "";
        public string PgData = "";
        public string PgLog = "";
        public string RunLog = "";
        public string SoftLog = "";
        public string ExeDir = "";
        public string LocalLowDir = "";

        public string McExe { get { return Path.Combine(GameDir, "MC.exe"); } }
        public string ServerExe { get { return Path.Combine(ServerDir, "nine-regions-backend.exe"); } }
        public string StoreGame { get { return Path.Combine(GameDir, @"File\palr_local_store.txt"); } }
        public string StoreLocalLow
        {
            get { return string.IsNullOrEmpty(LocalLowDir) ? "" : Path.Combine(LocalLowDir, "palr_local_store.txt"); }
        }
        public string IntentGame { get { return Path.Combine(GameDir, @"File\palr_launch.txt"); } }
        public string IntentLocalLow
        {
            get { return string.IsNullOrEmpty(LocalLowDir) ? "" : Path.Combine(LocalLowDir, "palr_launch.txt"); }
        }
        public string StateGame { get { return Path.Combine(GameDir, @"File\palr_login_state.txt"); } }
        public string StateLocalLow
        {
            get { return string.IsNullOrEmpty(LocalLowDir) ? "" : Path.Combine(LocalLowDir, "palr_login_state.txt"); }
        }

        public static Paths Detect()
        {
            var p = new Paths();
            string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            p.ExeDir = exeDir;
            p.LocalLowDir = FindLocalLow();

            if (File.Exists(Path.Combine(exeDir, @"Game\The Nine Regions\MC.exe")))
                return p.FillPortable(exeDir);

            if (File.Exists(Path.Combine(exeDir, "MC.exe")))
                return p.FillDev(Directory.GetParent(exeDir) != null ? Directory.GetParent(exeDir).FullName : exeDir, exeDir);

            string dir = exeDir;
            for (int i = 0; i < 4 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, @"Game\The Nine Regions\MC.exe"))) return p.FillPortable(dir);
                if (File.Exists(Path.Combine(dir, @"The Nine Regions\MC.exe")))
                    return p.FillDev(dir, Path.Combine(dir, "The Nine Regions"));
                dir = Directory.GetParent(dir) != null ? Directory.GetParent(dir).FullName : null;
            }

            string ini = Path.Combine(exeDir, "launcher.ini");
            if (File.Exists(ini))
            {
                foreach (var raw in File.ReadAllLines(ini))
                {
                    string t = raw.Trim();
                    if (t.StartsWith("root=", StringComparison.OrdinalIgnoreCase))
                    {
                        string root = t.Substring(5).Trim().Trim('"');
                        if (File.Exists(Path.Combine(root, @"Game\The Nine Regions\MC.exe"))) return p.FillPortable(root);
                        if (File.Exists(Path.Combine(root, @"The Nine Regions\MC.exe")))
                            return p.FillDev(root, Path.Combine(root, "The Nine Regions"));
                    }
                }
            }
            return p;
        }

        private Paths FillPortable(string root)
        {
            Mode = LayoutMode.Portable;
            Root = root;
            GameDir = Path.Combine(root, @"Game\The Nine Regions");
            ServerDir = Path.Combine(root, "Server");
            PgBin = Path.Combine(root, @"pgsql\bin");
            PgData = Path.Combine(root, "pgdata");
            PgLog = Path.Combine(root, "pgdata.log");
            RunLog = Path.Combine(ServerDir, "run.log");
            return this;
        }

        private Paths FillDev(string root, string gameDir)
        {
            Mode = LayoutMode.Dev;
            Root = root;
            GameDir = gameDir;
            ServerDir = Path.Combine(root, @"NineRegions-server\backend\dist\pack");
            if (!File.Exists(Path.Combine(ServerDir, "nine-regions-backend.exe")))
            {
                string dev = Path.Combine(root, @"NineRegions-server-dev\backend\dist\pack");
                if (File.Exists(Path.Combine(dev, "nine-regions-backend.exe"))) ServerDir = dev;
            }
            PgBin = FindPgBin(root);
            PgData = Path.Combine(root, @"NineRegions-server\backend\database");
            PgLog = Path.Combine(root, @"NineRegions-server\backend\db.log");
            RunLog = Path.Combine(ServerDir, "run.log");
            return this;
        }

        private static string FindPgBin(string root)
        {
            string env = Environment.GetEnvironmentVariable("PG_BIN");
            if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "pg_ctl.exe"))) return env;

            string[] candidates =
            {
                Path.Combine(root, @"pgsql\bin"),
                @"D:\Tools\postgresql-18.6-4-windows-x64-binaries\bin",
                @"C:\Program Files\PostgreSQL\18\bin",
                @"C:\Program Files\PostgreSQL\17\bin",
                @"C:\Program Files\PostgreSQL\16\bin",
            };
            foreach (var c in candidates)
                if (File.Exists(Path.Combine(c, "pg_ctl.exe"))) return c;
            return candidates[0];
        }

        private static string FindLocalLow()
        {
            string lz = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // LocalLow 不在 SpecialFolder 里：%USERPROFILE%\AppData\LocalLow
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string baseDir = Path.Combine(userProfile, @"AppData\LocalLow");
            string direct = Path.Combine(baseDir, @"SOFTSTAR\The Nine Regions");
            if (Directory.Exists(direct)) return direct;
            try
            {
                foreach (var company in Directory.GetDirectories(baseDir))
                {
                    string cand = Path.Combine(company, "The Nine Regions");
                    if (Directory.Exists(cand)) return cand;
                }
            }
            catch { }
            return direct; // 不存在也返回预期路径（写盘时会创建目录）
        }

        public string Describe()
        {
            return string.Format("模式={0}  根目录={1}\n游戏={2}\n服务端={3}\n数据库={4}\n数据目录={5}",
                Mode, Root, GameDir, ServerDir, PgBin, PgData);
        }
    }
}
