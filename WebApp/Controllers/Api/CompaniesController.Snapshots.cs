using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using System.Data;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ColdCall.Controllers.Api;

public partial class CompaniesController
{
    private async Task<PagedResult<CompanyShortDto>?> TrySnapshotPageAsync(
        CompanySearchQuery search, SearchProfileCriteria criteria, int[] pkdIds, int userId,
        string? sortBy, string? direction, bool addressLocality, bool notesOnly, int? days,
        bool refresh, int recordLimit, int page, int pageSize, Action<string> mark, CancellationToken token)
    {
        // Membership is independent of both the selected period and sorting.
        var membership = JsonSerializer.SerializeToNode(criteria)!.AsObject();
        membership.Remove(nameof(criteria.SortColumn));
        membership.Remove(nameof(criteria.SortDirection));
        var orderKey = (sortBy ?? "name").ToLowerInvariant() + ":" +
            (string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            userId, criteria = membership, pkdIds = pkdIds.Where(id => id > 0).Distinct().OrderBy(id => id).ToArray(),
            addressLocality, notesOnly,
            notesVersion = _searchCache.Get<Guid?>("CompanyNotesVersion:" + userId)
        }))));
        if (!refresh && _snapshots!.IsTooLarge(key)) return null;
        var cached = _snapshots!.TryGet(key, out CompanySearchSnapshot? snapshot) && snapshot != null;
        // A direct recent-name search should not first search all historical names.
        // Reuse a complete snapshot when available; otherwise query the recent subset.
        if (days.HasValue && !pkdIds.Any(id => id > 0) && (refresh || !cached)) return null;
        if (refresh || snapshot == null)
        {
            var all = await search.BuildCompaniesAsync(criteria, pkdIds, null, addressLocality, token: token);
            var hasNotes = await _db.CompanyUserNotes.AsNoTracking()
                .AnyAsync(note => note.UserId == userId && note.Notes.Trim() != "", token);
            if (hasNotes) hasNotes = await search.WithUserNotes(all, userId).AnyAsync(token);
            if (notesOnly) all = search.WithUserNotes(all, userId);
            var ids = new List<int>();
            var dates = new List<DateTime?>();
            await foreach (var row in search.OrderedCompanies(all, sortBy, direction)
                .Select(company => new { company.Id, company.ImportedOn })
                .TagWith("CompanySearch.AllSnapshot").AsAsyncEnumerable().WithCancellation(token))
            {
                // Large searches retain the existing bounded-memory database path.
                if (ids.Count == CompanySearchSnapshotCache.MaximumRecords)
                {
                    _snapshots!.MarkTooLarge(key);
                    return null;
                }
                ids.Add(row.Id);
                dates.Add(row.ImportedOn);
            }
            snapshot = new CompanySearchSnapshot(ids.ToArray(), dates.ToArray(), hasNotes, orderKey);
            _snapshots!.Set(key, snapshot);
        }
        var orderedIndices = snapshot.GetOrder(orderKey);
        if (orderedIndices == null)
        {
            // Sort only the matched IDs, preserving SQL collation and null ordering.
            var matchedIds = _db.Database.SqlQueryRaw<int>(
                "SELECT [Value] FROM OPENJSON(@SnapshotIds) WITH ([Value] int '$')",
                new SqlParameter("@SnapshotIds", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(snapshot.Ids) });
            var orderedIds = await search.OrderedIds(_db.Companies.AsNoTracking()
                .Where(company => matchedIds.Contains(company.Id)), sortBy, direction)
                .TagWith("CompanySearch.SnapshotOrder").ToArrayAsync(token);
            var indices = snapshot.Ids.Select((id, index) => (id, index)).ToDictionary(row => row.id, row => row.index);
            orderedIndices = orderedIds.Select(id => indices[id]).ToArray();
            snapshot.SetOrder(orderKey, orderedIndices);
        }
        mark("prepare");
        var now = DateTime.Now;
        var since = now.AddDays(-days.GetValueOrDefault());
        var eligible = orderedIndices.Where(index => !days.HasValue
            || (snapshot.ImportedOn[index] >= since && snapshot.ImportedOn[index] <= now)).ToArray();
        var fullCount = eligible.Length;
        var total = Math.Min(fullCount, recordLimit);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        mark("summary");
        var offset = (page - 1) * pageSize;
        var idsOnPage = eligible.Skip(offset).Take(Math.Min(pageSize, total - offset))
            .Select(index => snapshot.Ids[index]).ToArray();
        mark("page");
        var items = await search.SelectDetails(_db.Companies.AsNoTracking().Where(company => idsOnPage.Contains(company.Id)))
            .TagWith("CompanySearch.PageDetails").ToListAsync(token);
        var positions = idsOnPage.Select((id, index) => new { id, index }).ToDictionary(row => row.id, row => row.index);
        items.Sort((left, right) => positions[left.Id].CompareTo(positions[right.Id]));
        mark("details");
        var notes = await _db.CompanyUserNotes.AsNoTracking()
            .Where(note => note.UserId == userId && idsOnPage.Contains(note.CompanyId))
            .ToDictionaryAsync(note => note.CompanyId, note => note.Notes, token);
        foreach (var item in items) item.UserNotes = notes.GetValueOrDefault(item.Id);
        mark("notes");
        return new PagedResult<CompanyShortDto>(items, total, page, pageSize,
            QueryableExtensions.CompanyShortSortableColumns, fullCount, snapshot.HasNotes);
    }
}
