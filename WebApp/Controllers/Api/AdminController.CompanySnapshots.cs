using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    private async Task<(int[] Ids, int Total, int Page)?> TryAdminSnapshotPageAsync(
        IQueryable<Company> source, SearchProfileCriteria criteria, int[]? pkdIds, int adminId,
        string? sortBy, string? direction, int? days, bool refresh, int page, int pageSize, CancellationToken token)
    {
        if (_companySnapshots == null || criteria.CompanyName.Trim().Length < 3) return null;
        var key = "Admin:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            adminId, criteria, pkdIds = (pkdIds ?? []).Where(id => id > 0).Distinct().OrderBy(id => id).ToArray(),
            source = source.ToQueryString()
        }))));
        if (!refresh && _companySnapshots.IsTooLarge(key)) return null;
        _companySnapshots.TryGet(key, out var snapshot);
        if (days.HasValue && (refresh || snapshot == null)) return null;
        var orderKey = (sortBy ?? "name").ToLowerInvariant() + ":" +
            (string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc");
        var search = new CompanySearchQuery(_db);
        if (refresh || snapshot == null)
        {
            var all = await search.BuildCompaniesAsync(criteria, pkdIds, null, includeAllCompanies: true,
                source: source, token: token);
            var ids = new List<int>();
            var dates = new List<DateTime?>();
            await foreach (var row in all.ApplyCompanySort(sortBy, direction).ThenBy(company => company.Id)
                .Select(company => new { company.Id, company.ImportedOn })
                .TagWith("AdminCompanySearch.Snapshot").AsAsyncEnumerable().WithCancellation(token))
            {
                if (ids.Count == CompanySearchSnapshotCache.MaximumRecords)
                {
                    _companySnapshots.MarkTooLarge(key);
                    return null;
                }
                ids.Add(row.Id);
                dates.Add(row.ImportedOn);
            }
            snapshot = new CompanySearchSnapshot(ids.ToArray(), dates.ToArray(), false, orderKey);
            _companySnapshots.Set(key, snapshot);
        }
        var indices = snapshot.GetOrder(orderKey);
        if (indices == null)
        {
            var matchedIds = _db.Database.SqlQueryRaw<int>(
                "SELECT [Value] FROM OPENJSON(@AdminSnapshotIds) WITH ([Value] int '$')",
                new SqlParameter("@AdminSnapshotIds", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(snapshot.Ids) });
            var orderedIds = await _db.Companies.AsNoTracking().Where(company => matchedIds.Contains(company.Id))
                .ApplyCompanySort(sortBy, direction).ThenBy(company => company.Id)
                .Select(company => company.Id).ToArrayAsync(token);
            var positions = snapshot.Ids.Select((id, index) => (id, index)).ToDictionary(row => row.id, row => row.index);
            indices = orderedIds.Select(id => positions[id]).ToArray();
            snapshot.SetOrder(orderKey, indices);
        }
        var now = DateTime.Now;
        var since = now.AddDays(-days.GetValueOrDefault());
        var eligible = indices.Where(index => !days.HasValue
            || (snapshot.ImportedOn[index] >= since && snapshot.ImportedOn[index] <= now)).ToArray();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(eligible.Length / (double)pageSize)));
        return (eligible.Skip((page - 1) * pageSize).Take(pageSize).Select(index => snapshot.Ids[index]).ToArray(),
            eligible.Length, page);
    }
}
