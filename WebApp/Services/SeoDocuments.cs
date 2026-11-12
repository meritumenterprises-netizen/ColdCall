using System.Net;
using System.Text.Json;
using System.Xml.Linq;

namespace ColdCall.Services;

public static class SeoDocuments
{
    public const string Origin = "https://www.findmyclient.pl";
    public sealed record Page(string Path, string Title, string Description);
    public sealed record Guide(string Path, string Label, string Description, string Content);
    private static readonly IReadOnlyList<Guide> Guides = JsonSerializer.Deserialize<Guide[]>(ReadResource("guides.json"),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    public static readonly IReadOnlyList<Page> Pages = JsonSerializer.Deserialize<Page[]>(ReadResource("pages.json"),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    public static Page? Find(string path) => Pages.FirstOrDefault(p => p.Path == path);

    private static string ReadResource(string name)
    {
        using var stream = typeof(SeoDocuments).Assembly.GetManifestResourceStream("ColdCall.Seo." + name)
            ?? throw new InvalidOperationException("Missing SEO resource: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string Render(string html, Page page)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);
        var url = Origin + page.Path;
        var head = $"""
            <title>{E(page.Title)}</title>
            <meta name="description" content="{E(page.Description)}">
            <meta name="robots" content="index,follow,max-image-preview:large">
            <link rel="canonical" href="{E(url)}">
            <meta property="og:type" content="website">
            <meta property="og:site_name" content="FindMyClient.pl">
            <meta property="og:locale" content="pl_PL">
            <meta property="og:title" content="{E(page.Title)}">
            <meta property="og:description" content="{E(page.Description)}">
            <meta property="og:url" content="{E(url)}">
            <meta property="og:image" content="{Origin}/assets/findmyclient-logo.png">
            <meta name="twitter:card" content="summary_large_image">
            <meta name="twitter:title" content="{E(page.Title)}">
            <meta name="twitter:description" content="{E(page.Description)}">
            <meta name="twitter:image" content="{Origin}/assets/findmyclient-logo.png">
            """;
        const string startMarker = "<!-- SEO:START -->", endMarker = "<!-- SEO:END -->";
        var start = html.IndexOf(startMarker, StringComparison.Ordinal);
        var end = html.IndexOf(endMarker, StringComparison.Ordinal);
        if (start < 0 || end < start) throw new InvalidDataException("Rebuild Angular: SEO document markers missing.");
        html = html[..start] + startMarker + head + endMarker + html[(end + endMarker.Length)..];
        var content = page.Path switch
        {
            "/biura-rachunkowe" => ReadResource("accounting-offices.html"),
            "/agencje-marketingowe" => ReadResource("marketing-agencies.html"),
            "/agencje-lead-generation" => ReadResource("lead-generation.html"),
            "/plany" => "<h1>Plany FindMyClient.pl</h1><p>Wybierz plan wyszukiwania firm i eksportu CSV. Aktualny cennik jest dostępny po załadowaniu aplikacji.</p>",
            _ => Guides.FirstOrDefault(g => g.Path == page.Path) is { } guide
                ? "<a href=\"/\">Strona główna</a>" + guide.Content
                : ReadResource("landing.html")
        };
        var links = string.Join(" ", Pages.Select(p => $"<a href=\"{p.Path}\">{E(p.Title)}</a>"));
        if (page.Path is not ("/" or "/plany")) content = "<h1>" + E(page.Title) + "</h1>" + content;
        return html.Replace("<app-root>Loading...</app-root>",
            $"<app-root><main><nav aria-label=\"Menu strony\">{links}</nav><article>{content}</article></main></app-root>", StringComparison.Ordinal);
    }

    public static string Sitemap()
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        return new XDocument(new XElement(ns + "urlset", Pages.Select(p =>
            new XElement(ns + "url", new XElement(ns + "loc", Origin + p.Path))))).ToString();
    }
}
