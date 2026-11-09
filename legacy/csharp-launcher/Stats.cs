using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace PalrLauncher
{
    public sealed class AccountStat
    {
        public int Species;   // 卡种数
        public int Copies;    // 卡牌张数
        public int Tier;      // 段位等级 1..25
        public int Uid;       // 玩家 UID（0=服务端未分配）
        public int Level;     // 角色等级（pveProgress.level 优先）
        public int Stages;    // 主线已通关关卡数
        public int Money;     // 金币
        public int Jade;      // 玉
        public int LadderStar; // 段位星
        public bool Ok;
    }

    /// <summary>
    /// 账号资料：本地服务端 GET /r/1.0.50/palrstats（纯文本，制表符分隔）。
    /// 失败不抛错，返回空表；UI 显示 —。
    /// </summary>
    public static class Stats
    {
        public const string Url = "http://127.0.0.1:4010/r/1.0.50/palrstats";

        // 段位名（源：服务端 pvpCfg.json ranks[].stairName；静态内嵌，避免依赖运行期文件）
        private static readonly string[] RankNames =
        {
            "化身 壹", "化身 贰", "化身 叁", "人位 壹", "人位 贰", "人位 叁", "人位 肆", "人位 伍",
            "地位 壹", "地位 贰", "地位 叁", "地位 肆", "地位 伍",
            "天位 壹", "天位 贰", "天位 叁", "天位 肆", "天位 伍",
            "圣位 壹", "圣位 贰", "圣位 叁", "圣位 肆", "圣位 伍", "圣位 陆", "至尊",
        };

        public static string RankName(int tier)
        {
            if (tier <= 0) return "";
            if (tier <= RankNames.Length) return RankNames[tier - 1];
            return "段位 " + tier;
        }

        /**
         * 取某账号的资料：**精确名优先**，找不到再按大小写不敏感兜底。
         * （本地服务端可能同时存在 MiaoYu / Miaoyu 两个账号，直接忽略大小写会串号。）
         */
        public static bool TryGet(Dictionary<string, AccountStat> map, string name, out AccountStat st)
        {
            if (map != null && name != null && map.TryGetValue(name, out st))
            {
                return true;
            }
            if (map != null && name != null)
            {
                foreach (var kv in map)
                {
                    if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        st = kv.Value;
                        return true;
                    }
                }
            }
            st = null;
            return false;
        }

        /**
         * 账号表对账（启动器为主表）：把本地账号名单发给服务端，服务端删除名单外的账号。
         * 返回被服务端删掉的名字（每行一个）；失败返回空串。
         */
        public static string Reconcile(IEnumerable<string> names, int timeoutMs)
        {
            try
            {
                var csv = string.Join("|", names);
                if (string.IsNullOrEmpty(csv)) return "";
                string body = "op=prune&names=" + Uri.EscapeDataString(csv);
                var data = Encoding.UTF8.GetBytes(body);
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:4010/r/1.0.50/palracc");
                req.Method = "POST";
                req.ContentType = "application/x-www-form-urlencoded";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.Proxy = null;
                req.KeepAlive = false;
                using (var st = req.GetRequestStream()) { st.Write(data, 0, data.Length); }
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return sr.ReadToEnd().Trim();
                }
            }
            catch { return ""; }
        }

        public static Dictionary<string, AccountStat> Fetch(int timeoutMs)
        {
            var map = new Dictionary<string, AccountStat>(StringComparer.Ordinal);
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(Url);
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.Proxy = null;
                req.KeepAlive = false;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string all = sr.ReadToEnd();
                    foreach (var raw in all.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0) continue;
                        string[] c = line.Split('\t');
                        if (c.Length < 4) continue;
                        int species, copies, tier, uid = 0;
                        if (!int.TryParse(c[1], out species)) species = 0;
                        if (!int.TryParse(c[2], out copies)) copies = 0;
                        if (!int.TryParse(c[3], out tier)) tier = 0;
                        if (c.Length >= 5) int.TryParse(c[4], out uid);
                        int level = 0, stages = 0, money = 0, jade = 0, star = 0;
                        if (c.Length >= 6) int.TryParse(c[5], out level);
                        if (c.Length >= 7) int.TryParse(c[6], out stages);
                        if (c.Length >= 8) int.TryParse(c[7], out money);
                        if (c.Length >= 9) int.TryParse(c[8], out jade);
                        if (c.Length >= 10) int.TryParse(c[9], out star);
                        map[c[0]] = new AccountStat { Species = species, Copies = copies, Tier = tier, Uid = uid,
                            Level = level, Stages = stages, Money = money, Jade = jade, LadderStar = star, Ok = true };
                    }
                }
            }
            catch { }
            return map;
        }
    }
}
