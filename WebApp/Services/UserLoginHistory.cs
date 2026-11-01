using ColdCall.Data;
using ColdCall.Models;

namespace ColdCall.Services;

public static class UserLoginHistory
{
    public static void Add(ColdCallDbContext db, AppUser user)
    {
        if (!user.IsAdmin && user.TermsOfServiceAccepted != true) return;
        db.UserLogins.Add(new UserLogin { UserId = user.Id, LoginDate = DateTime.UtcNow });
    }
}
