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
    private const string DetailsLabel = "جزئیات خبر";
    private const string Admin = "#iman";
    private const string SectionBreak = "\n\n";
    private static readonly char[] SentenceEndings = { '.', '!', '?', '؟', '۔', '\n' };
    private static readonly char[] RepeatedTextSeparators = { ' ', '.', '!', '?', '؟', '۔', '،', ',', ':', '؛', '–', '—' };

    public static string GetRichHtml(News item)
    {
        var title = CleanText(item.Title);
        if (title.Length == 0)
        {
            title = "خبر فوری";
        }

        var summary = WithoutRepeatedOpening(CleanText(item.ShortDescription), title);
        var body = GetBody(item.FullDescription, title, summary);
        title = Shorten(title, 180);
        summary = Shorten(summary, 260);

        var source = GetSourceName(item);
        var sourceLabel = source.Length == 0 ? "متن کامل خبر" : $"متن کامل در {source}";
        var hasLink = Uri.TryCreate(item.Link, UriKind.Absolute, out var link)
            && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)
            && link.Host.Length > 0
            && link.UserInfo.Length == 0;
        var footerLabel = hasLink ? $"↗ {sourceLabel}" : source;
        var footer = hasLink
            ? $"<a href=\"{WebUtility.HtmlEncode(link.AbsoluteUri)}\">{WebUtility.HtmlEncode(footerLabel)}</a>"
            : WebUtility.HtmlEncode(footerLabel);

        var sections = new List<string> { $"<p><b>{WebUtility.HtmlEncode(title)}</b></p>" };
        if (summary.Length > 0)
        {
            sections.Add(ToParagraph(summary));
        }

        var fixedText = string.Join(SectionBreak, title, summary, DetailsLabel, footerLabel, Admin);
        var bodyBudget = MaxRichTextBytes - Encoding.UTF8.GetByteCount(fixedText) - SectionBreak.Length;
        body = FitBody(body, bodyBudget);
        var previewBreak = FindBreak(body, BodyPreviewLength);
        var preview = body[..previewBreak].TrimEnd();
        if (preview.Length > 0)
        {
            sections.Add(ToParagraph(preview));
        }

        var details = body[previewBreak..].TrimStart();
        if (details.Length > 0)
        {
            sections.Add($"<details><summary>{DetailsLabel}</summary>{ToParagraph(details)}</details>");
        }

        sections.Add(footer.Length > 0 ? $"<p>{footer}<br>{Admin}</p>" : $"<p>{Admin}</p>");
        return string.Join('\n', sections);
    }

    private static string GetBody(string text, string title, string summary)
    {
        text = Regex.Replace(text ?? "", @"</(?:p|div|li|h[1-6])\s*>|<br\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"https?://[^\s<]+", "", RegexOptions.IgnoreCase);
        var paragraphs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var paragraph = CleanText(line);
            paragraph = Regex.Replace(paragraph, @"^(طرفداری|ورزش[‌ ]*سه|خبرورزشی|فوتبال\s*[۳3][۶6][۰0])\s*[|｜]\s*", "");
            paragraph = WithoutRepeatedOpening(WithoutRepeatedOpening(paragraph, title), summary);

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
        return Regex.Replace(text, @"\s+", " ").Trim();
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

    private static string GetSourceName(News item)
    {
        return item.SportSourceId switch
        {
            var id when id == SportSourceEnumeration.Varzesh3.Id => "ورزش۳",
            var id when id == SportSourceEnumeration.Tarafdari.Id => SportSourceEnumeration.Tarafdari.Name,
            var id when id == SportSourceEnumeration.KhabarVarzeshi.Id => "خبرورزشی",
            var id when id == SportSourceEnumeration.Football360.Id => "فوتبال۳۶۰",
            _ => Shorten(CleanText(item.SportSource?.Title), 60)
        };
    }
}
