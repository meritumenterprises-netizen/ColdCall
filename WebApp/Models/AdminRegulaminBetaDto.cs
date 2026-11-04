using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public sealed class AdminRegulaminBetaDto
{
    public int GlobalSettingsId { get; set; }
    [StringLength(2000000)]
    public string? RegulaminBeta { get; set; }
}
