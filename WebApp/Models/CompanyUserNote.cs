namespace ColdCall.Models;

public class CompanyUserNote
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string Notes { get; set; } = "";
}
