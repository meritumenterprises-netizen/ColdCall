using ColdCall.Models;
using Microsoft.AspNetCore.Identity;

namespace ColdCall.Services;

public sealed class AdminUserPasswordService(UserManager<LocalAccount> manager)
{
    public async Task<IdentityResult> SetAsync(int userId, string email, string password)
    {
        var account = await manager.FindByIdAsync(userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (account == null)
        {
            account = new LocalAccount
            {
                Id = userId, UserName = email, Email = email,
                EmailConfirmed = true, LockoutEnabled = true
            };
            return await manager.CreateAsync(account, password);
        }

        var token = await manager.GeneratePasswordResetTokenAsync(account);
        var result = await manager.ResetPasswordAsync(account, token, password);
        if (!result.Succeeded || account.EmailConfirmed) return result;
        // An administrator's explicit approval replaces the email confirmation step.
        account.EmailConfirmed = true;
        return await manager.UpdateAsync(account);
    }
}
