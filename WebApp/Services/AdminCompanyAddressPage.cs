using ColdCall.Models;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public static class AdminCompanyAddressPage
{
    public static async Task<int[]> LoadIdsAsync(IQueryable<Company> query, bool descending,
        int offset, int pageSize, CancellationToken token)
    {
        var ordered = descending
            ? query.OrderByDescending(company => EF.Property<string>(company, "AddressSort"))
            : query.OrderBy(company => EF.Property<string>(company, "AddressSort"));
        return await ordered.ThenBy(company => company.Id).Skip(offset).Take(pageSize)
            .Select(company => company.Id).ToArrayAsync(token);
    }
}
