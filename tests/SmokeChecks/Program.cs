using System.Net;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Services;

public class Program
{
    public static async Task Main()
    {
        Check(NewsUrl.GetSourceId("https://www.tarafdari.com/node/123") == 2, "Source identity uses article host");
        Check(NewsUrl.GetSourceId("https://football360.ir/post/123") == 4, "Football360 identity");
        var url = NewsUrl.Normalize("https://WWW.TARAFDARI.COM/News/ABC?id=1&utm_source=test#section");
        Check(url == "https://www.tarafdari.com/News/ABC?id=1", "Preserve case and identity query, strip tracking");
        Check(NewsUrl.Normalize("https://tarafdari.com/news?id=1")
            != NewsUrl.Normalize("https://tarafdari.com/news?id=2"), "Distinct query-based articles stay distinct");
        try
        {
            NewsUrl.GetSourceId("https://tarafdari.com.example.org/post/1");
            throw new Exception("Unrecognized host was accepted.");
        }
        catch (ArgumentException)
        {
            Console.WriteLine("PASS: Reject unrelated source host");
        }

        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var helper = new HttpHelperService(client, NullLogger<HttpHelperService>.Instance);
        var content = await helper.GetStringAsync("https://tarafdari.com/rss?category=football");
        Check(content == "<rss />", "HTTP response decoding");
        Check(handler.RequestedUri.Query == "?category=football", "Feed query stays intact");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await helper.GetStringAsync("https://tarafdari.com/rss", cancellation.Token);
            throw new Exception("Cancellation was swallowed.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("PASS: HTTP cancellation propagates");
        }

        var root = Path.GetFullPath(GetRepositoryRoot());
        var files = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToList();
        foreach (var file in files)
        {
            var tree = CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(file));
            var errors = tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
            Check(errors.Count == 0, "C# syntax: " + Path.GetRelativePath(root, file));
        }

        Console.WriteLine($"Passed URL/HTTP regressions and syntax checks for {files.Count} C# files.");
    }

    private static string GetRepositoryRoot()
    {
        return Path.Combine(AppContext.BaseDirectory, "../../../../../");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }

        Console.WriteLine("PASS: " + message);
    }

    private class CapturingHandler : HttpMessageHandler
    {
        public Uri RequestedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<rss />")
            });
        }
    }
}
