using ColdCall.Services;
using Microsoft.AspNetCore.Mvc;

namespace ColdCall.Controllers.Api;

public partial class CompaniesController
{
    [HttpGet("exports/deletion-quota")]
    public async Task<IActionResult> DeletionQuota(CancellationToken token)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        return Ok(await CsvDeletionQuota.GetAsync(_db, user.Id, token));
    }
}
