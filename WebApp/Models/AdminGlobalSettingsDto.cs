using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class AdminGlobalSettingsDto
{
    public int GlobalSettingsId { get; set; }
    [Range(1, int.MaxValue)]
    public int GlobalDegreeOfParallelismImportByDate { get; set; }
    public bool ApplicationInService { get; set; }
    public bool RegistrationSuspended { get; set; }
}
