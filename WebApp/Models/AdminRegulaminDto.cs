using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class AdminRegulaminDto
{
    public int GlobalSettingsId { get; set; }
    [StringLength(2000000)]
    public string? Regulamin { get; set; }
}
