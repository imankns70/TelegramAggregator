using System.Text.RegularExpressions;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    // TextSimilarity.cs
    public static class TextSimilarity
    {
        public static double GetSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

            s1 = Normalize(s1);
            s2 = Normalize(s2);

            int distance = LevenshteinDistance(s1, s2);
            int maxLen = Math.Max(s1.Length, s2.Length);
            return maxLen == 0 ? 1.0 : 1.0 - (double)distance / maxLen;
        }

        private static string Normalize(string s) => string.IsNullOrEmpty(s) ? "" :
            s.ToLowerInvariant()
             .Replace(" ", "").Replace("‌", "").Replace("ك", "ک").Replace("ي", "ی")
             .Replace("ۀ", "ه").Replace("ة", "ه").Replace("ؤ", "و").Replace("إ", "ا")
             .Replace("أ", "ا").Replace("آ", "ا").Replace("ء", "").Replace("ئ", "ی")
             .Replace("ـ", "").Trim();

        private static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length, m = t.Length;
            int[,] d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }
            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                                       d[i - 1, j - 1] + (t[j - 1] == s[i - 1] ? 0 : 1));
            return d[n, m];
        }
    }
}
