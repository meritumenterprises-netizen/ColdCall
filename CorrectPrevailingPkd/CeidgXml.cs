using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CorrectPrevailingPkd;

internal static partial class CeidgXml
{
    public static IEnumerable<XElement> ReadDetails(string path, Action<int>? reportProgress = null)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            IgnoreComments = true, IgnoreWhitespace = true
        });
        var lastPercent = 0;
        reportProgress?.Invoke(0);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Detail") continue;
            using var subtree = reader.ReadSubtree();
            var detail = XElement.Load(subtree);
            // XmlReader buffers ahead; reserve 100% for successful processing of the final batch.
            var percent = stream.Length == 0 ? 0 : Math.Min(99, (int)(100d * stream.Position / stream.Length));
            if (percent > lastPercent)
            {
                lastPercent = percent;
                reportProgress?.Invoke(percent);
            }
            yield return detail;
        }
    }

    public static Assignment? Parse(XElement detail)
    {
        string Value(string name) => detail.Descendants().FirstOrDefault(e =>
            e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";
        var nip = Value("NIP").Replace("-", "").Replace(" ", "");
        var code = Value("GlownyKodPkd").Replace(".", "").ToUpperInvariant();
        return NipPattern().IsMatch(nip) && CodePattern().IsMatch(code) ? new(nip, code) : null;
    }

    [GeneratedRegex(@"\A[0-9]{10}\z")]
    private static partial Regex NipPattern();
    [GeneratedRegex(@"\A[0-9]{4}[A-Z]\z")]
    private static partial Regex CodePattern();
}

internal sealed record Assignment(string Nip, string CodeWithoutDots);
