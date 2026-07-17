using UnityEngine;

namespace SwarmECS.Runtime
{
    public enum SwarmUiLanguage : byte
    {
        English = 0,
        SimplifiedChinese = 1,
    }

    /// <summary>
    /// Presentation-only localization state for the technical lab. Language
    /// selection never enters authoritative state, snapshots, hashes or replay.
    /// </summary>
    public static class SwarmUiLocalization
    {
        public const string PreferenceKey = "SwarmECS.UiLanguage";

        private static readonly string[] EnglishViewLabels =
        {
            "OVERVIEW",
            "NAV",
            "AVOID",
            "COLLISION",
            "ROLLBACK",
            "NETWORK",
        };

        private static readonly string[] ChineseViewLabels =
        {
            "总览",
            "导航",
            "避障",
            "碰撞",
            "回滚",
            "网络",
        };

        public static SwarmUiLanguage LoadLanguage()
        {
            int value = PlayerPrefs.GetInt(PreferenceKey, (int)SwarmUiLanguage.English);
            return value == (int)SwarmUiLanguage.SimplifiedChinese
                ? SwarmUiLanguage.SimplifiedChinese
                : SwarmUiLanguage.English;
        }

        public static void SaveLanguage(SwarmUiLanguage language)
        {
            PlayerPrefs.SetInt(PreferenceKey, (int)language);
            PlayerPrefs.Save();
        }

        public static string Select(SwarmUiLanguage language, string english, string chinese)
        {
            return language == SwarmUiLanguage.SimplifiedChinese ? chinese : english;
        }

        public static string GetViewLabel(SwarmUiLanguage language, int index)
        {
            string[] labels = language == SwarmUiLanguage.SimplifiedChinese
                ? ChineseViewLabels
                : EnglishViewLabels;
            return (uint)index < (uint)labels.Length ? labels[index] : string.Empty;
        }
    }
}
