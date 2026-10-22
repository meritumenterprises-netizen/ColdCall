using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImportCEIDGByDate;

public sealed class CeidgReports
{
    [JsonPropertyName("raporty")]
    public required List<CeidgReport> Reports { get; set; }

    [JsonPropertyName("properties")]
    public Dictionary<string, JsonElement> Properties { get; set; } = new();
}
