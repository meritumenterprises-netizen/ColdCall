namespace AdjustCeidgAddress.Data.FirmyTemp;

public partial class AdjustName
{
    public int AdjustId { get; set; }
    public string Nip { get; set; } = null!;
    public string Nazwa { get; set; } = null!;
    public bool Processed { get; set; }
    public bool Adjusted { get; set; }
    public DateTime DateModified { get; set; }
    public string? CompanyXml { get; set; }
    public int? WojewodztwoId { get; set; }
}
