namespace ImportCEIDGByDate.Data.FirmyTemp;

public class UserLogin
{
    public int UserLoginId { get; set; }
    public int UserId { get; set; }
    public DateTime LoginDate { get; set; }
    public string? IpAddress { get; set; }
    public string? DnsAddress { get; set; }
}
