using System.Security.Cryptography;
using System.Text;
using ColdCall.Data;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpPost("users/{id:int}/password")]
    public async Task<IActionResult> SetUserPassword(int id, AdminSetUserPasswordRequest request,
        [FromServices] LocalAuthDbContext credentials,
        [FromServices] AdminUserPasswordService passwords, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        if (user == null) return NotFound();

        await using var transaction = await _db.Database.BeginTransactionAsync(token);
        await credentials.Database.UseTransactionAsync(transaction.GetDbTransaction(), token);
        try
        {
            var email = user.GoogleEmail.Trim().ToLowerInvariant();
            var resource = "RegisterEmail:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email)));
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                    @LockOwner='Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 50001, 'User password lock unavailable', 1;", token);
            var result = await passwords.SetAsync(user.Id, email, request.Password);
            if (!result.Succeeded)
                return BadRequest(new { message = string.Join(" ", result.Errors.Select(error => error.Description)) });
            await transaction.CommitAsync(token);
            return Ok(new { message = "Haslo uzytkownika zostalo ustawione." });
        }
        finally
        {
            await credentials.Database.UseTransactionAsync(null, CancellationToken.None);
        }
    }
}
