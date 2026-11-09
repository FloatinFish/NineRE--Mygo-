using System;
using System.Globalization;
using Microsoft.Win32;

namespace PalrLauncher
{
    /// <summary>
    /// 游戏设置读写（Unity PlayerPrefs → Windows 注册表）。
    ///
    /// 逆向依据（tmp/decomp）：登录页的画面/音量设置全部落在 PlayerPrefs，
    /// Windows 下存于 `HKCU\Software\SOFTSTAR\The Nine Regions`，键名形如
    /// `<原文>_h<hash>`，hash 算法为 **djb2-xor**（对 Bloom/FPSHigh/CharShadow/
    /// AntiAliasing/Quality/Is2DLiveBoardGirl 六个真实键 6/6 命中）。
    ///
    /// 已知键（来自反编译 PlayerPrefs.SetInt/GetInt 调用点）：
    ///   Quality(1=Standard 2=Low 3=High 4=Ultra) / AntiAliasing / Bloom / FPSHigh /
    ///   CharShadow / BattleCharOutline / Is2DLiveBoardGirl /
    ///   BGMVolume / SEVolume(0..1) / BGMMute / SEMute / CurrLanguageValue
    /// </summary>
    public static class GamePrefs
    {
        private const string SubKey = @"Software\SOFTSTAR\The Nine Regions";

        private static uint Hash(string key)
        {
            uint h = 5381;
            foreach (char ch in key)
            {
                h = (h * 33) & 0xFFFFFFFFu;
                h = (h ^ (byte)ch) & 0xFFFFFFFFu;
            }
            return h;
        }

        private static string RegName(string key)
        {
            return key + "_h" + Hash(key).ToString(CultureInfo.InvariantCulture);
        }

        private static RegistryKey Open()
        {
            try
            {
                return Registry.CurrentUser.CreateSubKey(SubKey);
            }
            catch
            {
                return null;
            }
        }

        public static int ReadInt(string key, int def)
        {
            using (var rk = Open())
            {
                if (rk == null) return def;
                var v = rk.GetValue(RegName(key));
                if (v is int) return (int)v;
                var s = v as string;
                int parsed;
                return s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : def;
            }
        }

        public static void WriteInt(string key, int value)
        {
            using (var rk = Open())
            {
                if (rk == null) return;
                rk.SetValue(RegName(key), value, RegistryValueKind.DWord);
            }
        }

        public static float ReadFloat(string key, float def)
        {
            using (var rk = Open())
            {
                if (rk == null) return def;
                var v = rk.GetValue(RegName(key));
                var s = v as string;
                float parsed;
                if (s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return parsed;
                if (v is int) return (int)v;
                return def;
            }
        }

        public static void WriteFloat(string key, float value)
        {
            using (var rk = Open())
            {
                if (rk == null) return;
                /** Unity 在 Windows 上把 float 存成字符串 */
                rk.SetValue(RegName(key), value.ToString("0.###", CultureInfo.InvariantCulture), RegistryValueKind.String);
            }
        }

        public static bool ReadBool(string key, bool def)
        {
            return ReadInt(key, def ? 1 : 0) != 0;
        }

        public static void WriteBool(string key, bool value)
        {
            WriteInt(key, value ? 1 : 0);
        }
    }
}
