namespace ImportCEIDGByDate.Data.FirmyTemp;

public class CompanyXml
{
    public int CompanyXmlId { get; set; }
    public string? Nip { get; set; }
    public string XmlContent { get; set; } = null!;
    public string Md5 { get; set; } = null!;
}
