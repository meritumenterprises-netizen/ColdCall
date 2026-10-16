using Microsoft.AspNetCore.Identity;

namespace ColdCall.Models;

public class LocalAccount : IdentityUser<int>
{
    public bool TrialOffered { get; set; }
}
