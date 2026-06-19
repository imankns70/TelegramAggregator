namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class TextSimilarity
    {
        // Fast-exit: if lengths differ by more than this ratio, texts can't be >=0.90 similar
        private const double SimilarityThreshold = 0.90;

        public static double GetSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

            s1 = Normalize(s1);
            s2 = Normalize(s2);

            if (s1.Length == 0 && s2.Length == 0) return 1.0;
            if (s1.Length == 0 || s2.Length == 0) return 0.0;

            // Fast-exit: if length ratio is too different, similarity is < threshold
            double lenRatio = (double)Math.Min(s1.Length, s2.Length) / Math.Max(s1.Length, s2.Length);
            if (lenRatio < SimilarityThreshold) return lenRatio;

            int distance = LevenshteinDistanceTwoRows(s1, s2);
            int maxLen = Math.Max(s1.Length, s2.Length);
            return maxLen == 0 ? 1.0 : 1.0 - (double)distance / maxLen;
        }

        private static string Normalize(string s) => string.IsNullOrEmpty(s) ? "" :
            s.ToLowerInvariant()
             .Replace(" ", "").Replace("‌", "").Replace("ك", "ک").Replace("ي", "ی")
             .Replace("ۀ", "ه").Replace("ة", "ه").Replace("ؤ", "و").Replace("إ", "ا")
             .Replace("أ", "ا").Replace("آ", "ا").Replace("ء", "").Replace("ئ", "ی")
             .Replace("ـ", "").Trim();

        /// <summary>
        /// Two-row Levenshtein: O(min(n,m)) memory instead of O(n*m).
        /// Significantly reduces GC pressure for long article texts.
        /// </summary>
        private static int LevenshteinDistanceTwoRows(string s, string t)
        {
            // Ensure s is the shorter string to minimise row allocation
            if (s.Length > t.Length) (s, t) = (t, s);

            int n = s.Length, m = t.Length;
            var prev = new int[n + 1];
            var curr = new int[n + 1];

            for (int i = 0; i <= n; i++) prev[i] = i;

            for (int j = 1; j <= m; j++)
            {
                curr[0] = j;
                for (int i = 1; i <= n; i++)
                {
                    int cost = t[j - 1] == s[i - 1] ? 0 : 1;
                    curr[i] = Math.Min(Math.Min(curr[i - 1] + 1, prev[i] + 1), prev[i - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }

            return prev[n];
        }
    }
}
