using ColdCall.Models;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public static class AdminCompanyAddressPage
{
    public static async Task<int[]> LoadIdsAsync(IQueryable<Company> query, bool descending,
        int offset, int pageSize, CancellationToken token)
    {
        var ordered = descending
            ? query.OrderByDescending(company => EF.Property<string>(company, "AddressSortPrefix"))
            : query.OrderBy(company => EF.Property<string>(company, "AddressSortPrefix"));
        var page = await ordered.ThenBy(company => company.Id).Skip(offset).Take(pageSize)
            .Select(company => new { company.Id, Prefix = EF.Property<string>(company, "AddressSortPrefix") })
            .ToListAsync(token);
        var ids = page.Select(company => company.Id).ToArray();

        // Only addresses exceeding the index key need a full-string sort within their prefix group.
        foreach (var group in page.Where(company => company.Prefix?.Length >= 800).GroupBy(company => company.Prefix))
        {
            var prefix = group.Key;
            var firstId = group.First().Id;
            var matches = query.Where(company => EF.Property<string>(company, "AddressSortPrefix") == prefix);
            var groupOffset = await matches.CountAsync(company => company.Id < firstId, token);
            var fullOrder = descending
                ? matches.OrderByDescending(company => EF.Property<string>(company, "AddressSort"))
                : matches.OrderBy(company => EF.Property<string>(company, "AddressSort"));
            var resolvedIds = await fullOrder.ThenBy(company => company.Id).Skip(groupOffset).Take(group.Count())
                .Select(company => company.Id).ToArrayAsync(token);
            var start = page.FindIndex(company => company.Id == firstId);
            Array.Copy(resolvedIds, 0, ids, start, resolvedIds.Length);
        }
        return ids;
    }
}
