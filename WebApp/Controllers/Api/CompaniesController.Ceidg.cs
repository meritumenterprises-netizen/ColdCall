using ColdCall.Services;
using Microsoft.AspNetCore.Mvc;

namespace ColdCall.Controllers.Api;

public partial class CompaniesController
{
    [HttpGet("ceidg-id")]
    public async Task<JsonResult> GetCompanyId([FromQuery] string? nip, [FromQuery] string? status,
        [FromServices] CeidgService ceidg, CancellationToken token)
    {
        return new JsonResult(await ceidg.GetCompanyId(nip, status, token));
    }
}
