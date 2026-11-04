namespace ColdCall.Models;

public class GlobalSetting
{
    public int GlobalSettingsId { get; set; }
    public int GlobalDegreeOfParallelismImportByDate { get; set; }
    public bool ApplicationInService { get; set; }
    public bool RegistrationSuspended { get; set; }
    public string? Regulamin { get; set; }
    public string? RegulaminBeta { get; set; }
}
