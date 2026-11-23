using System.Text;
namespace Worthless
{
    public static class PlayerNameRules
    {
        public const int MaxBytes = 48;
        public const string Default = "玩家";
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Default;
            var sb = new StringBuilder();
            foreach (var c in raw) { if (c == '\r' || c == '\n' || char.IsControl(c)) continue; sb.Append(c); }
            var text = sb.ToString().Trim();
            if (text.Length == 0) return Default;
            while (Encoding.UTF8.GetByteCount(text) > MaxBytes)
            {
                var cut = text.Length - 1;
                if (cut > 0 && char.IsLowSurrogate(text[cut]) && char.IsHighSurrogate(text[cut - 1])) cut--;
                text = text.Substring(0, cut);
            }
            return text.Length == 0 ? Default : text;
        }
    }
}