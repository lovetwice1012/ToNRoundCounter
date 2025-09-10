using System.Collections.Generic;

namespace ToNRoundCounter.Domain
{
    /// <summary>
    /// Helper methods for <see cref="RoundType"/>.
    /// </summary>
    public static class RoundTypeExtensions
    {
        private static readonly Dictionary<RoundType, string> JapaneseNames = new()
        {
            { RoundType.Classic, "クラシック" },
            { RoundType.Run, "走れ！" },
            { RoundType.Alternate, "オルタネイト" },
            { RoundType.Punished, "パニッシュ" },
            { RoundType.Cracked, "狂気" },
            { RoundType.Sabotage, "サボタージュ" },
            { RoundType.Fog, "霧" },
            { RoundType.Bloodbath, "ブラッドバス" },
            { RoundType.DoubleTrouble, "ダブルトラブル" },
            { RoundType.EX, "EX" },
            { RoundType.Midnight, "ミッドナイト" },
            { RoundType.Ghost, "ゴースト" },
            { RoundType.EightPages, "8ページ" },
            { RoundType.Unbound, "アンバウンド" },
            { RoundType.ColdNight, "寒い夜" },
            { RoundType.MysticMoon, "ミスティックムーン" },
            { RoundType.BloodMoon, "ブラッドムーン" },
            { RoundType.Twilight, "トワイライト" },
            { RoundType.Solstice, "ソルスティス" }
        };

        /// <summary>
        /// Gets the Japanese display name for a round type.
        /// </summary>
        public static string ToJapanese(this RoundType type)
            => JapaneseNames.TryGetValue(type, out var name) ? name : type.ToString();

        /// <summary>
        /// Gets the display name for a round type.
        /// </summary>
        public static string GetDisplayName(RoundType type)
            => type.ToJapanese();
    }
}

