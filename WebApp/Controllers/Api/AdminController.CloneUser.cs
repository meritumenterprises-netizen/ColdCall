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
    [HttpPost("users/{id:int}/clone")]
    public async Task<IActionResult> CloneBetaUser(int id, CloneBetaUserRequest request,
        [FromServices] LocalAuthDbContext credentials, [FromServices] UserManager<LocalAccount> manager,
        CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            return BadRequest(new { message = "Podaj imie i nazwisko." });
        var email = request.Email.Trim().ToLowerInvariant();
        await using var transaction = await _db.Database.BeginTransactionAsync(token);
        await credentials.Database.UseTransactionAsync(transaction.GetDbTransaction(), token);
        try
        {
            var resource = "RegisterEmail:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email)));
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                    @LockOwner='Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 50001, 'Registration lock unavailable', 1;", token);
            if (await _db.Users.AnyAsync(u => u.GoogleEmail == email, token) || await manager.FindByEmailAsync(email) != null)
                return Conflict(new { message = "Uzytkownik z takim adresem email juz istnieje." });
            var source = await _db.Users.AsNoTracking().Include(u => u.Plans)
                .Include(u => u.SearchProfiles).ThenInclude(s => s.Criteria)
                .Include(u => u.SearchProfiles).ThenInclude(s => s.PkdCodes)
                .AsSplitQuery().SingleOrDefaultAsync(u => u.Id == id && u.IsBetaUser && !u.IsAdmin, token);
            if (source == null) return NotFound(new { message = "Nie znaleziono beta uzytkownika." });
            var gmail = email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase)
                || email.EndsWith("@googlemail.com", StringComparison.OrdinalIgnoreCase);
            var nip = GenerateCloneNip(source.Nip);
            var clone = new AppUser
            {
                GoogleEmail = email, GoogleSubject = (gmail ? "clone:" : "local:") + Guid.NewGuid().ToString("N"),
                UsesGmail = gmail, FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(),
                DisplayName = request.FirstName.Trim() + " " + request.LastName.Trim(),
                IsBetaUser = true, IsAdmin = false, IsEnabled = source.IsEnabled,
                DisablePlanPurchase = source.DisablePlanPurchase, TermsOfServiceAccepted = false,
                CompanyName = $"Moja firma {request.FirstName.Trim()} {request.LastName.Trim()} {nip}",
                Nip = nip, Phone = source.Phone,
                PostalCode = source.PostalCode, City = source.City, Street = source.Street, StreetNumber = source.StreetNumber,
                Plans = source.Plans.Select(p => new UserPlan
                {
                    PlanDefinitionId = p.PlanDefinitionId, PurchasedAtUtc = p.PurchasedAtUtc,
                    ExpiresAtUtc = p.ExpiresAtUtc, IsActive = p.IsActive, PricePaid = 0, Source = "Manual"
                }).ToList(),
                SearchProfiles = source.SearchProfiles.Select(s => new SearchProfile
                {
                    Name = s.Name, RecordCount = s.RecordCount, SearchProfileHash = s.SearchProfileHash,
                    LastSavedAtUtc = DateTime.UtcNow,
                    PkdCodes = s.PkdCodes.Select(p => new SearchProfilePkd { PkdCodeId = p.PkdCodeId }).ToList(),
                    Criteria = s.Criteria == null ? null : CopyCloneCriteria(s.Criteria)
                }).ToList()
            };
            _db.Users.Add(clone);
            await _db.SaveChangesAsync(token);
            if (!gmail)
            {
                var result = await manager.CreateAsync(new LocalAccount
                {
                    Id = clone.Id, UserName = email, Email = email, EmailConfirmed = true, LockoutEnabled = true
                });
                if (!result.Succeeded)
                    return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });
            }
            await transaction.CommitAsync(token);
            return Ok(new { clone.Id });
        }
        finally { await credentials.Database.UseTransactionAsync(null, CancellationToken.None); }
    }

    private static string GenerateCloneNip(string? originalNip)
    {
        ReadOnlySpan<int> weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        Span<char> digits = stackalloc char[10];
        while (true)
        {
            var sum = 0;
            for (var index = 0; index < weights.Length; index++)
            {
                var digit = RandomNumberGenerator.GetInt32(index == 0 ? 1 : 0, 10);
                digits[index] = (char)('0' + digit);
                sum += digit * weights[index];
            }
            var checksum = sum % 11;
            if (checksum == 10) continue;
            digits[9] = (char)('0' + checksum);
            var nip = new string(digits);
            if (nip != originalNip && new PolishNipAttribute().IsValid(nip)) return nip;
        }
    }

    private static SearchProfileCriteria CopyCloneCriteria(SearchProfileCriteria c) => new()
    {
        Name = c.Name, CompanyName = c.CompanyName, CompanyStatusIds = c.CompanyStatusIds,
        SortColumn = c.SortColumn, SortDirection = c.SortDirection, Address = c.Address,
        HasPhone = c.HasPhone, HasEmail = c.HasEmail, HasWebsite = c.HasWebsite,
        DateRangeEnabled = c.DateRangeEnabled, DateFrom = c.DateFrom, DateTo = c.DateTo,
        RecordLimit = c.RecordLimit, WojewodztwoId = c.WojewodztwoId, PowiatId = c.PowiatId,
        GminaId = c.GminaId, MiejscowoscId = c.MiejscowoscId,
        HasFirmyWPromieniu = c.HasFirmyWPromieniu, FirmyWPromieniu = c.FirmyWPromieniu
    };
}
