using System.Security.Cryptography;
using System.Text.Json;
using ColdCall.Data;
using ColdCall.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public record TrialOffer(string Code, string DescriptionHtml, DateTime ExpiresAtUtc, string Token);
public record TrialAcceptance(string Token, bool TermsAccepted);
public record TrialOfferTicket(int UserId, int PlanId, DateTime ExpiresAtUtc);

public class TrialPlanService
{
    private readonly ColdCallDbContext _db;
    private readonly ITimeLimitedDataProtector _protector;
    private readonly CsvExportCleanupService _cleanup;

    public TrialPlanService(ColdCallDbContext db, IDataProtectionProvider protection, CsvExportCleanupService cleanup)
    {
        _db = db;
        _cleanup = cleanup;
        _protector = protection.CreateProtector("ColdCall.NewUserTrial.v1").ToTimeLimitedDataProtector();
    }

    public async Task<TrialOffer?> OfferAsync(int userId)
    {
        var plan = await _db.PlanDefinitions.AsNoTracking().Where(p => p.IsTrial && p.IsActive && p.ExpiresAfterDays > 0)
            .OrderBy(p => p.Order).ThenBy(p => p.Id).FirstOrDefaultAsync();
        if (plan == null) return null;
        var expiration = DateTime.UtcNow.AddDays(plan.ExpiresAfterDays!.Value);
        var ticket = new TrialOfferTicket(userId, plan.Id, expiration);
        return new TrialOffer(plan.Code, plan.DescriptionHtml, expiration,
            _protector.Protect(JsonSerializer.Serialize(ticket), TimeSpan.FromMinutes(15)));
    }

    public async Task DeactivateExpiredAsync(int userId, CancellationToken token)
    {
        await _cleanup.DeleteForExpiredTrialAsync(userId, token);
    }

    public async Task EnsureAssignedAsync(int userId, CancellationToken token)
    {
        if (await _db.UserPlans.AnyAsync(p => p.AppUserId == userId, token)) return;
        await using var transaction = await _db.Database.BeginTransactionAsync(token);
        var resource = "TrialPlan:" + userId;
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 50001, 'Trial assignment lock unavailable', 1;", token);
        if (await _db.UserPlans.AnyAsync(p => p.AppUserId == userId, token)) return;
        if (!await _db.Users.AnyAsync(u => u.Id == userId && u.IsEnabled && !u.IsAdmin, token)) return;
        var plan = await _db.PlanDefinitions.AsNoTracking()
            .Where(p => p.IsTrial && p.IsActive && p.ExpiresAfterDays > 0)
            .OrderBy(p => p.Order).ThenBy(p => p.Id).FirstOrDefaultAsync(token);
        if (plan == null) return;
        var now = DateTime.UtcNow;
        _db.UserPlans.Add(new UserPlan
        {
            AppUserId = userId, PlanDefinitionId = plan.Id, PurchasedAtUtc = now,
            ExpiresAtUtc = now.AddDays(plan.ExpiresAfterDays!.Value),
            PricePaid = 0, Source = "Trial", IsActive = true
        });
        await _db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task<bool> AcceptAsync(int userId, TrialAcceptance acceptance, CancellationToken token)
    {
        if (!acceptance.TermsAccepted || string.IsNullOrWhiteSpace(acceptance.Token)) return false;
        TrialOfferTicket? ticket;
        try { ticket = JsonSerializer.Deserialize<TrialOfferTicket>(_protector.Unprotect(acceptance.Token)); }
        catch (Exception e) when (e is CryptographicException or JsonException) { return false; }
        if (ticket == null || ticket.UserId != userId || ticket.ExpiresAtUtc <= DateTime.UtcNow) return false;
        await using var transaction = await _db.Database.BeginTransactionAsync(token);
        var resource = "TrialPlan:" + userId;
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 50001, 'Trial assignment lock unavailable', 1;", token);
        if (await _db.UserPlans.AnyAsync(p => p.AppUserId == userId, token)) return false;
        if (!await _db.PlanDefinitions.AnyAsync(p => p.Id == ticket.PlanId && p.IsTrial && p.IsActive, token)) return false;
        _db.UserPlans.Add(new UserPlan
        {
            AppUserId = userId, PlanDefinitionId = ticket.PlanId, PurchasedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = ticket.ExpiresAtUtc, PricePaid = 0, Source = "Trial", IsActive = true
        });
        await _db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return true;
    }
}
