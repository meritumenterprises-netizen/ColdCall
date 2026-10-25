using System.Security.Cryptography;
using System.Text;
using ColdCall.Data;
using ColdCall.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser(AdminCreateUserRequest request,
        [FromServices] LocalAuthDbContext credentials,
        [FromServices] UserManager<LocalAccount> manager, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        var email = request.Email.Trim().ToLowerInvariant();
        var plan = await _db.PlanDefinitions.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == request.PlanDefinitionId && !p.IsUpgrade, token);
        if (plan?.ExpiresAfterDays is not > 0)
            return BadRequest(new { message = "Wybierz plan z poprawna liczba dni waznosci." });
        var now = DateTime.UtcNow;
        DateTime expiration;
        try { expiration = now.AddDays(plan.ExpiresAfterDays.Value); }
        catch (ArgumentOutOfRangeException)
        {
            return BadRequest(new { message = "Niepoprawna liczba dni waznosci planu." });
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(token);
        await credentials.Database.UseTransactionAsync(transaction.GetDbTransaction(), token);
        try
        {
            // Use the same email lock as public registration to prevent duplicate accounts.
            var resource = "RegisterEmail:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email)));
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                    @LockOwner='Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 50001, 'Registration lock unavailable', 1;", token);
            if (await _db.Users.AnyAsync(u => u.GoogleEmail == email, token)
                || await manager.FindByEmailAsync(email) != null)
                return Conflict(new { message = "Uzytkownik z takim adresem email juz istnieje." });

            var user = new AppUser
            {
                GoogleEmail = email, GoogleSubject = "local:" + Guid.NewGuid().ToString("N"),
                DisplayName = email, DisablePlanPurchase = request.DisablePlanPurchase,
                UsesGmail = false, IsAdmin = false, IsEnabled = true,
                CreatedAtUtc = now, LastLoginAtUtc = now
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(token);
            var account = new LocalAccount
            {
                Id = user.Id, UserName = email, Email = email,
                EmailConfirmed = true, LockoutEnabled = true
            };
            var created = await manager.CreateAsync(account, request.Password);
            if (!created.Succeeded)
                return BadRequest(new { message = string.Join(" ", created.Errors.Select(e => e.Description)) });
            _db.UserPlans.Add(new UserPlan
            {
                AppUserId = user.Id, PlanDefinitionId = plan.Id, PurchasedAtUtc = now,
                ExpiresAtUtc = expiration, PricePaid = 0, Source = null, IsActive = true
            });
            await _db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return Ok(new { user.Id, Email = user.GoogleEmail });
        }
        finally
        {
            await credentials.Database.UseTransactionAsync(null, CancellationToken.None);
        }
    }
}
