using System.Xml;
using System.Xml.Linq;

namespace AdjustCeidgAddress;

public static class CompanyXmlReader
{
    public static IEnumerable<XElement> Read(string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true
        };
        using var reader = XmlReader.Create(path, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Detail")
                continue;

            using var subtree = reader.ReadSubtree();
            var detail = XElement.Load(subtree);
            yield return detail;
        }
    }
}
