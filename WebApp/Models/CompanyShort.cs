namespace ColdCall.Models;

public class CompanyShortView
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Nip { get; set; }
    public string FullAddress { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Www { get; set; }
    public DateTime? DataAktywnosci { get; set; }
    public string? StatusDzialalnosci { get; set; }
    public string? TypDzialalnosci { get; set; }
}

public class CompanyShortDto : CompanyShortView
{
    public DateTime? DataModifiedOn { get; set; }
    public string? UserNotes { get; set; }
    public string? TypyDzialalnosci { get; set; }
}
