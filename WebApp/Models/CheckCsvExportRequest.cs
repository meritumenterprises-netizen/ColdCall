using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class CheckCsvExportRequest
{
    [Required, RegularExpression("^(all|3|7|notes)$")]
    public string ResultTab { get; set; } = "all";
}
