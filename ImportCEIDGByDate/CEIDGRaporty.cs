using System.Text.Json.Serialization;

namespace ImportCEIDGByDate;

public sealed class CEIDGRaporty
{
    [JsonPropertyName("raporty")]
    public required List<CEIDGRaport> Raporty { get; init; }

    [JsonPropertyName("properties")]
    public CEIDGRaportyProperties? Properties { get; init; }
}

public sealed class CEIDGRaport
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("nazwa")]
    public required string Nazwa { get; init; }

    [JsonPropertyName("format")]
    public required string Format { get; init; }

    [JsonPropertyName("raport")]
    public required string Raport { get; init; }

    [JsonPropertyName("data-utworzenia")]
    public required string DataUtworzenia { get; init; }
}

public sealed class CEIDGRaportyProperties
{
    [JsonPropertyName("dc:title")]
    public string? Title { get; init; }

    [JsonPropertyName("dc:description")]
    public string? Description { get; init; }

    [JsonPropertyName("dc:language")]
    public string? Language { get; init; }

    [JsonPropertyName("schema:provider")]
    public string? Provider { get; init; }

    [JsonPropertyName("schema:datePublished")]
    public string? DatePublished { get; init; }
}
