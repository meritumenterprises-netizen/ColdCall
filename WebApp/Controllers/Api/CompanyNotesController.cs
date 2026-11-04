using ColdCall.Data;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ColdCall.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/companies/{companyId:int}/notes")]
public class CompanyNotesController(ColdCallDbContext db, ICurrentUserService currentUserService, IMemoryCache searchCache) : ControllerBase
{
    public record NotesRequest(string Notes);

    private async Task<bool> HasAccess(AppUser user, CancellationToken token) =>
        user.IsAdmin ||
        await db.UserPlans.AnyAsync(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow, token);

    [HttpGet]
    public async Task<IActionResult> Get(int companyId, CancellationToken token)
    {
        var user = await currentUserService.GetOrCreateAsync(User);
        if (!await HasAccess(user, token)) return StatusCode(402, new { message = "Plan wygasl." });
        if (!await db.Companies.AnyAsync(x => x.Id == companyId, token)) return NotFound();
        var notes = await db.CompanyUserNotes.AsNoTracking().Where(x => x.CompanyId == companyId && x.UserId == user.Id)
            .Select(x => x.Notes).SingleOrDefaultAsync(token);
        return Ok(new { notes = notes ?? "" });
    }

    [HttpPut]
    public async Task<IActionResult> Save(int companyId, NotesRequest request, CancellationToken token)
    {
        if (request.Notes == null) return BadRequest(new { message = "Podaj tresc notatki." });
        var user = await currentUserService.GetOrCreateAsync(User);
        if (!await HasAccess(user, token)) return StatusCode(402, new { message = "Plan wygasl." });
        if (!await db.Companies.AnyAsync(x => x.Id == companyId, token)) return NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // Lock the company/user key range so concurrent first saves cannot create duplicates.
        var note = await db.CompanyUserNotes.FromSqlInterpolated($"SELECT * FROM [ColdCall].[CompanyUserNotes] WITH (UPDLOCK, HOLDLOCK) WHERE [CompanyID] = {companyId} AND [UserId] = {user.Id}")
            .SingleOrDefaultAsync(token);
        if (request.Notes.Length == 0)
        {
            if (note != null)
            {
                db.CompanyUserNotes.Remove(note);
                await db.SaveChangesAsync(token);
            }
            await transaction.CommitAsync(token);
            searchCache.Set("CompanyNotesVersion:" + user.Id, Guid.NewGuid(), TimeSpan.FromMinutes(20));
            return Ok(new { notes = "" });
        }
        if (note == null)
        {
            note = new CompanyUserNote { CompanyId = companyId, UserId = user.Id };
            db.CompanyUserNotes.Add(note);
        }
        note.Notes = request.Notes;
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        searchCache.Set("CompanyNotesVersion:" + user.Id, Guid.NewGuid(), TimeSpan.FromMinutes(20));
        return Ok(new { notes = note.Notes });
    }
}
