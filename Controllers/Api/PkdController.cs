using ColdCall.Data;
using ColdCall.Models;
using ColdCall.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/pkd")]
[Authorize]
public class PkdController : ControllerBase
{
    private readonly ColdCallDbContext _db;

    public PkdController(ColdCallDbContext db)
    {
        _db = db;
    }

    public record ClassifyRequest([Required, StringLength(10000)] string Description);

    [HttpPost("classify")]
    public async Task<IActionResult> Classify([FromBody] ClassifyRequest request,
        [FromServices] PkdClassifier classifier, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { message = "Wpisz opis dzialalnosci." });
        var codes = await _db.PkdCodes.AsNoTracking().OrderBy(code => code.Code)
            .Select(code => new PkdDto(code.Id, code.Code, code.Name)).ToListAsync(cancellationToken);
        try
        {
            var selected = codes.Count == 0 ? Array.Empty<PkdDto>()
                : await classifier.ClassifyAsync(request.Description, codes, cancellationToken);
            var status = selected.Count > 0 ? "OK" : "Nie znaleziono kodów";
            return Ok(new { status, pkdCodes = selected, text = string.Join("\n", new[] { status }.Concat(selected.Select(code => code.Code))) });
        }
        catch (PkdClassificationRejectedException ex)
        {
            return Ok(new { status = ex.Message, pkdCodes = Array.Empty<PkdDto>(), text = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = ex.Message });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(504, new { message = "Ollama nie odpowiedziala w wymaganym czasie." });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException)
        {
            return StatusCode(502, new { message = "Nie udalo sie uzyskac poprawnej odpowiedzi Ollama. Sprobuj ponownie." });
        }
    }

    [HttpGet]
    public async Task<IReadOnlyList<PkdDto>> Search([FromQuery] string? term = null)
    {
        term = term?.Trim();

        var query = _db.PkdCodes.AsQueryable();
        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(x => x.Code.Contains(term) || x.Name.Contains(term));
        }

        return await query
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Name)
            .Select(x => new PkdDto(x.Id, x.Code, x.Name))
            .ToListAsync();
    }
}
