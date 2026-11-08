namespace ColdCall.Models;

public class AdminUserLoginDto
{
    public int UserLoginId { get; set; }
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public DateTime LoginDate { get; set; }
    public string? IpAddress { get; set; }
    public string? DnsAddress { get; set; }
}
