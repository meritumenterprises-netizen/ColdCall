using ColdCall.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpDelete("logins/{id:int}")]
    public async Task<IActionResult> DeleteLogin(int id, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        var deleted = await _db.UserLogins.Where(login => login.UserLoginId == id).ExecuteDeleteAsync(token);
        return deleted == 0 ? NotFound() : NoContent();
    }

    [HttpGet("logins")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ListLogins(int page = 1, string sortBy = "date", string direction = "desc", string? email = null, CancellationToken token = default)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (sortBy != "date" && sortBy != "user" || direction != "asc" && direction != "desc") return BadRequest();
        var query = from login in _db.UserLogins.AsNoTracking()
                    join user in _db.Users.AsNoTracking() on login.UserId equals user.Id into users
                    from user in users.DefaultIfEmpty()
                    where user == null || !user.IsAdmin
                    select new AdminUserLoginDto
                    {
                        UserLoginId = login.UserLoginId, UserId = login.UserId, LoginDate = login.LoginDate,
                        UserName = user == null ? null : user.DisplayName == null || user.DisplayName.Trim() == "" ? user.GoogleEmail : user.DisplayName,
                        Email = user == null ? null : user.GoogleEmail,
                        IpAddress = login.IpAddress, DnsAddress = login.DnsAddress
                    };
        if (!string.IsNullOrWhiteSpace(email))
        {
            var emailFilter = email.Trim();
            query = query.Where(row => row.Email != null && row.Email.Contains(emailFilter));
        }
        var total = await query.CountAsync(token);
        const int pageSize = 10;
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var ordered = sortBy == "user"
            ? direction == "asc" ? query.OrderBy(row => row.Email) : query.OrderByDescending(row => row.Email)
            : direction == "asc" ? query.OrderBy(row => row.LoginDate) : query.OrderByDescending(row => row.LoginDate);
        var items = await ordered.ThenByDescending(row => row.UserLoginId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token);
        foreach (var item in items) item.LoginDate = DateTime.SpecifyKind(item.LoginDate, DateTimeKind.Utc);
        return Ok(new { items, total, page, pageSize });
    }
}
