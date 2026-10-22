namespace ImportCEIDGByDate.Data.FirmyTemp;

public class CompanyPkd
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int PkdCodeId { get; set; }
    public bool Prevailing { get; set; }
    public virtual Company Company { get; set; } = null!;
    public virtual PkdCode PkdCode { get; set; } = null!;
}
