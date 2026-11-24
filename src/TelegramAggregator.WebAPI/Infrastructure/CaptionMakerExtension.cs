using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using TelegramAggregator.WebAPI.Models.Entities;

namespace TelegramAggregator.WebAPI.Infrastructure;

public static class CaptionMakerExtension
{
    private const int MaxRichTextBytes = 32_768;
    private const int BodyPreviewLength = 220;
    private const string HeadlinePrefix = "⚽ ";
    private const string DetailsLabel = "متن کامل خبر";
    private const string SectionBreak = "\n\n";
    private static readonly char[] SentenceEndings = { '.', '!', '?', '؟', '۔', '\n' };
    private static readonly char[] RepeatedTextSeparators = { ' ', '\n', '.', '!', '?', '؟', '۔', '،', ',', ':', '؛', '–', '—' };
    private static readonly Regex SourceNames = new(
        @"(?<![\p{L}\p{M}\p{N}])
          (?:
              (?:خبر[\s\u200C]+)?ورزش[\s\u200C]*(?:سه|[3۳٣]) | varzesh[\s\u200C]*3
            | طرفدار[یي] | tarafdari
            | خبر[\s\u200C]*ورزش[یي] | khabar[\s\u200C]*varzeshi
            | فوتبال[\s\u200C]*(?:[3۳٣][6۶٦][0۰٠]|فور[یي]) | football[\s\u200C]*360
          )
          (?![\p{L}\p{M}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace);

    public static string GetRichHtml(News item)
    {
        var title = CleanText(item.Title);
        if (title.Length == 0)
        {
            title = "خبر فوتبال";
        }

        var summary = WithoutRepeatedOpening(CleanText(item.ShortDescription), title);
        var body = GetBody(item.FullDescription, title);
        var hasDetails = body.Length > BodyPreviewLength;
        var preview = WithoutRepeatedOpening(body, summary);
        if (hasDetails)
        {
            preview = summary.Length == 0 ? body[..FindBreak(body, BodyPreviewLength)].TrimEnd() : "";
        }

        title = Shorten(title, 180);
        summary = Shorten(summary, 260);

        var headline = HeadlinePrefix + title;
        var sections = new List<string> { $"<h2>{WebUtility.HtmlEncode(headline)}</h2>" };
        if (summary.Length > 0)
        {
            sections.Add(ToParagraph(summary));
        }

        if (preview.Length > 0)
        {
            sections.Add(ToParagraph(preview));
        }

        if (hasDetails)
        {
            var fixedText = string.Join(SectionBreak, headline, summary, preview, DetailsLabel);
            var bodyBudget = MaxRichTextBytes - Encoding.UTF8.GetByteCount(fixedText) - SectionBreak.Length;
            var details = FitBody(body, bodyBudget);
            sections.Add($"<details><summary><b>{DetailsLabel}</b></summary>{ToParagraph(details)}</details>");
        }

        return string.Join('\n', sections);
    }

    private static string GetBody(string text, string title)
    {
        text = Regex.Replace(text ?? "", @"</(?:p|div|li|h[1-6])\s*>|<br\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"https?://[^\s<]+", "", RegexOptions.IgnoreCase);
        var paragraphs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var paragraph = WithoutRepeatedOpening(CleanText(line), title);

            if (paragraph.Length > 0 && seen.Add(paragraph))
            {
                paragraphs.Add(paragraph);
            }
        }

        return string.Join(SectionBreak, paragraphs);
    }

    private static string ToParagraph(string text)
    {
        // Keep article breaks inside one Telegram block, even for articles with many paragraphs.
        var html = WebUtility.HtmlEncode(text).Replace(SectionBreak, "<br><br>");
        return $"<p>{html}</p>";
    }

    private static string FitBody(string body, int byteBudget)
    {
        if (Encoding.UTF8.GetByteCount(body) <= byteBudget)
        {
            return body;
        }

        const string ellipsis = "…";
        var remaining = byteBudget - Encoding.UTF8.GetByteCount(ellipsis);
        var end = 0;
        while (end < body.Length)
        {
            var length = StringInfo.GetNextTextElementLength(body.AsSpan(end));
            remaining -= Encoding.UTF8.GetByteCount(body.AsSpan(end, length));
            if (remaining < 0)
            {
                break;
            }

            end += length;
        }

        return body[..end].TrimEnd() + ellipsis;
    }

    private static string CleanText(string text)
    {
        text = Regex.Replace(text ?? "", @"</?[a-zA-Z][a-zA-Z0-9]*\b[^<>]*>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[\u200B\uFEFF\u2066-\u2069\u202A-\u202E]", "");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return SourceNames.Replace(text, "خبر فوری");
    }

    private static string WithoutRepeatedOpening(string text, string opening)
    {
        opening = opening.TrimEnd(RepeatedTextSeparators);
        if (opening.Length == 0 || !text.StartsWith(opening, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        if (text.Length == opening.Length || RepeatedTextSeparators.Contains(text[opening.Length]))
        {
            return text[opening.Length..].TrimStart(RepeatedTextSeparators);
        }

        return text;
    }

    private static string Shorten(string text, int limit)
    {
        if (text.Length <= limit)
        {
            return text;
        }

        return text[..FindBreak(text, limit - 1)].TrimEnd() + "…";
    }

    private static int FindBreak(string text, int limit)
    {
        if (text.Length <= limit)
        {
            return text.Length;
        }

        var textElementOffsets = StringInfo.ParseCombiningCharacters(text);
        var cutoff = textElementOffsets.Last(offset => offset <= limit);
        if (cutoff == 0)
        {
            return 0;
        }

        var paragraphEnd = text.LastIndexOf(SectionBreak, cutoff - 1, StringComparison.Ordinal);
        if (paragraphEnd > 0)
        {
            return paragraphEnd;
        }

        var end = text.LastIndexOfAny(SentenceEndings, cutoff - 1);
        if (end >= cutoff * 2 / 3 && Array.BinarySearch(textElementOffsets, end + 1) >= 0)
        {
            return end + 1;
        }

        var space = text.LastIndexOf(' ', cutoff - 1);
        if (space >= cutoff * 2 / 3 && Array.BinarySearch(textElementOffsets, space) >= 0)
        {
            cutoff = space;
        }

        return cutoff;
    }

}
