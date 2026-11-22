namespace TelegramAggregator.WebAPI.Infrastructure;

public static class NewsUrl
{
    public static string Normalize(string value)
    {
        var uri = new Uri(value, UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("News links must use HTTP or HTTPS.", nameof(value));
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };
        var parameters = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter => !parameter.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
                && !parameter.StartsWith("fbclid=", StringComparison.OrdinalIgnoreCase)
                && !parameter.StartsWith("gclid=", StringComparison.OrdinalIgnoreCase));
        builder.Query = string.Join("&", parameters);
        return builder.Uri.AbsoluteUri;
    }

    public static int GetSourceId(string value)
    {
        var host = new Uri(value, UriKind.Absolute).IdnHost;
        if (IsVarzesh3Host(host))
        {
            return SportSourceEnumeration.Varzesh3.Id;
        }

        if (IsHost(host, "tarafdari.com"))
        {
            return SportSourceEnumeration.Tarafdari.Id;
        }

        if (IsHost(host, "khabarvarzeshi.com"))
        {
            return SportSourceEnumeration.KhabarVarzeshi.Id;
        }

        if (IsHost(host, "football360.ir"))
        {
            return SportSourceEnumeration.Football360.Id;
        }

        throw new ArgumentException("Unknown news source host.", nameof(value));
    }

    public static bool IsVarzesh3Host(string host)
    {
        return IsHost(host, "varzesh3.ir") || IsHost(host, "varzesh3.com");
    }

    private static bool IsHost(string host, string domain)
    {
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }
}
