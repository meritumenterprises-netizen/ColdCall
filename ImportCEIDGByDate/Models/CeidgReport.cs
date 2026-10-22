using System.Text.Json.Serialization;

namespace ImportCEIDGByDate;

public sealed class CeidgReport
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("nazwa")]
    public required string Name { get; set; }

    public string FileName
    {
        get
        {
            var date = DateTime.Parse(this.CreatedAt);
            var fname = this.Name + "_" + this.CreatedAtDateTime + this.Format;
            return fname;
        }
    }

    public string FileNameZip
    {
        get
        {
            var date = DateTime.Parse(this.CreatedAt);
            var fname = this.Name + "_" + this.CreatedAtDateTime + ".zip";
            return fname;
        }
    }

    public string FullLocalZipName
    {
        get; set;
    }

    [JsonPropertyName("format")]
    public required string Format { get; set; }

    [JsonPropertyName("raport")]
    public required string Report { get; set; }

    [JsonPropertyName("data-utworzenia")]
    public required string CreatedAt { get; set; }

    public string CreatedAtDate
    {
        get
        {
            return DateTime.Parse(CreatedAt).ToString("yyyy-MM-dd");
        }
    }

    public string CreatedAtDateTime
    {
        get
        {
            return DateTime.Parse(CreatedAt).ToString("yyyyMMdd_HHmmss");
        }
    }

    public string MD5
    {
        get; set;
    }
}
