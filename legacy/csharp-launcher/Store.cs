using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PalrLauncher
{
    public sealed class Account
    {
        public string Name = "";
        public string Pwd = "";
        public string Uid = "";
        public int Level = 1;
        public long Last;
        public long Secs;
    }

    /// <summary>
    /// 账号库（palr_local_store.txt）：D 盘游戏目录 + LocalLow 两份镜像。
    /// 读取时按 name 合并（last 新的优先）；写入时两份都写，任一失败即整体失败。
    /// </summary>
    public sealed class Store
    {
        public readonly List<Account> Accounts = new List<Account>();
        public int ScreenFull = 1;
        public int ScreenW;
        public int ScreenH;

        private readonly Paths _paths;

        public Store(Paths paths)
        {
            _paths = paths;
        }

        public IEnumerable<string> Files()
        {
            if (!string.IsNullOrEmpty(_paths.StoreGame)) yield return _paths.StoreGame;
            if (!string.IsNullOrEmpty(_paths.StoreLocalLow)) yield return _paths.StoreLocalLow;
        }

        public void Load()
        {
            Accounts.Clear();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var f in Files())
            {
                string text = null;
                for (int attempt = 0; attempt < 3 && text == null; attempt++)
                {
                    try { text = File.ReadAllText(f, Encoding.UTF8); }
                    catch { System.Threading.Thread.Sleep(80); }
                }
                if (string.IsNullOrEmpty(text)) continue;

                foreach (var rawLine in text.Split('\n'))
                {
                    string line = rawLine.TrimEnd('\r');
                    if (line.Length < 2 || line[1] != '\t') continue;
                    char kind = line[0];
                    string[] cols = line.Split('\t');
                    if (kind == 'A' && cols.Length >= 7)
                    {
                        var a = new Account
                        {
                            Name = cols[1],
                            Pwd = cols[2],
                            Uid = cols[3],
                            Level = ParseInt(cols[4], 1),
                            Last = ParseLong(cols[5]),
                            Secs = ParseLong(cols[6]),
                        };
                        if (a.Name.Length == 0) continue;
                        int idx;
                        if (seen.TryGetValue(a.Name, out idx))
                        {
                            if (a.Last >= Accounts[idx].Last) Accounts[idx] = a;
                        }
                        else
                        {
                            seen[a.Name] = Accounts.Count;
                            Accounts.Add(a);
                        }
                    }
                    else if (kind == 'S' && cols.Length >= 4)
                    {
                        // 两份取后读到的（LocalLow 通常是热修最新写入的）
                        ScreenFull = ParseInt(cols[1], 1);
                        ScreenW = ParseInt(cols[2], 0);
                        ScreenH = ParseInt(cols[3], 0);
                    }
                }
            }
            Accounts.Sort((x, y) => y.Last.CompareTo(x.Last));
        }

        public Account Find(string name)
        {
            foreach (var a in Accounts) if (string.Equals(a.Name, name, StringComparison.Ordinal)) return a;
            return null;
        }

        public void Upsert(string name, string pwd)
        {
            var old = Find(name);
            if (old != null)
            {
                old.Pwd = string.IsNullOrEmpty(pwd) ? old.Pwd : pwd;
                old.Last = Now();
                return;
            }
            Accounts.Insert(0, new Account { Name = name, Pwd = pwd ?? "", Uid = "", Level = 1, Last = Now(), Secs = 0 });
        }

        public bool Remove(string name)
        {
            var a = Find(name);
            return a != null && Accounts.Remove(a);
        }

        public void SetScreen(int full, int w, int h)
        {
            ScreenFull = full;
            ScreenW = w;
            ScreenH = h;
        }

        /// <summary>双写账号库；返回写入失败的路径（空数组=全部成功）。</summary>
        public string[] Save()
        {
            var sb = new StringBuilder();
            sb.Append("V\t1\n");
            foreach (var a in Accounts)
            {
                sb.Append("A\t").Append(Clean(a.Name)).Append('\t')
                  .Append(Clean(a.Pwd)).Append('\t')
                  .Append(Clean(a.Uid)).Append('\t')
                  .Append(a.Level).Append('\t')
                  .Append(a.Last).Append('\t')
                  .Append(a.Secs).Append('\n');
            }
            sb.Append("S\t").Append(ScreenFull).Append('\t').Append(ScreenW).Append('\t').Append(ScreenH).Append('\n');
            string text = sb.ToString();

            var failed = new List<string>();
            foreach (var f in Files())
            {
                try
                {
                    string dir = Path.GetDirectoryName(f);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string tmp = f + ".tmp";
                    File.WriteAllText(tmp, text, new UTF8Encoding(false));
                    if (File.Exists(f)) File.Delete(f);
                    File.Move(tmp, f);
                    string back = File.ReadAllText(f, Encoding.UTF8);
                    if (back != text) failed.Add(f + "（回读不一致）");
                }
                catch (Exception ex) { failed.Add(f + "：" + ex.Message); }
            }
            return failed.ToArray();
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();
        }
        private static int ParseInt(string s, int def) { int v; return int.TryParse(s, out v) ? v : def; }
        private static long ParseLong(string s) { long v; return long.TryParse(s, out v) ? v : 0; }
        public static long Now() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds; }
    }
}
