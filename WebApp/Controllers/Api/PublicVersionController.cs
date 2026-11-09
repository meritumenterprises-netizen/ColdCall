using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCall.Controllers.Api;

[ApiController, AllowAnonymous, Route("api/public/version")]
public class PublicVersionController : ControllerBase
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Get() => Ok(new { backendVersion = ApplicationVersion.Current });
}
