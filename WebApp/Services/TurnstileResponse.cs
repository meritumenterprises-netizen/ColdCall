using System.Text.Json.Serialization;

namespace ColdCall.Services;

public class TurnstileResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }
    [JsonPropertyName("action")]
    public string? Action { get; set; }
    [JsonPropertyName("error-codes")]
    public string[] ErrorCodes { get; set; } = [];
}
