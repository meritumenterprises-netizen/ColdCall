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
    private readonly ILogger<PkdController>? _logger;

    public PkdController(ColdCallDbContext db, ILogger<PkdController>? logger = null)
    {
        _db = db;
        _logger = logger;
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
        catch (HttpRequestException ex)
        {
            _logger?.LogWarning(ex, "Ollama PKD request failed with upstream HTTP {StatusCode}.", ex.StatusCode);
            var message = ex.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "Ollama odrzuci\u0142a dost\u0119p. Sprawd\u017a klucz API i uprawnienia modelu w konfiguracji backendu.",
                System.Net.HttpStatusCode.NotFound => "Ollama nie znalaz\u0142a modelu. Sprawd\u017a ustawienie Ollama:Model w backendzie.",
                System.Net.HttpStatusCode.TooManyRequests => "Przekroczono limit us\u0142ugi Ollama. Spr\u00f3buj ponownie p\u00f3\u017aniej.",
                null => "Nie mo\u017cna po\u0142\u0105czy\u0107 si\u0119 z Ollam\u0105. Sprawd\u017a po\u0142\u0105czenie backendu z ollama.com.",
                _ => $"Ollama zwr\u00f3ci\u0142a b\u0142\u0105d HTTP {(int)ex.StatusCode.Value}. Sprawd\u017a konfiguracj\u0119 modelu i logi backendu."
            };
            return StatusCode(502, new { message });
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            _logger?.LogWarning(ex, "Ollama PKD response failed validation.");
            return StatusCode(502, new { message = "Ollama zwr\u00f3ci\u0142a niepoprawny format odpowiedzi lub kod spoza listy PKD. Spr\u00f3buj ponownie." });
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
