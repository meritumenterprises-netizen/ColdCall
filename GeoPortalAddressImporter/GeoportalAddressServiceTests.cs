using System.Net;

namespace GeoPortalAddressImporter;

internal static class GeoportalAddressServiceTests
{
    public static async Task RunAsync(string directory)
    {
        await CheckAsync(directory, "http-error", (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }, GeoportalAddressErrorCode.RequestFailed);
        await CheckAsync(directory, "invalid-json", (_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>error</html>") }),
            GeoportalAddressErrorCode.InvalidResponse);
        await CheckAsync(directory, "invalid-payload", (_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"error\":\"service unavailable\"}") }),
            GeoportalAddressErrorCode.InvalidResponse);
        await CheckAsync(directory, "io-error", (_, _) => throw new IOException("Synthetic I/O failure"),
            GeoportalAddressErrorCode.IoError);
        await CheckAsync(directory, "timeout", (_, _) => throw new OperationCanceledException("Synthetic timeout"),
            GeoportalAddressErrorCode.Timeout);

        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var service = new GeoportalAddressService(http, Path.Combine(directory, "cancelled"));
        try
        {
            await service.FindAsync(Request(), cancellation.Token);
            throw new InvalidOperationException("Explicit cancellation was returned as a lookup result.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Console.WriteLine("PASS: GeoPortal service returns typed HTTP, timeout, response and I/O errors; explicit cancellation propagates.");
    }

    private static async Task CheckAsync(string directory, string name,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, GeoportalAddressErrorCode expected)
    {
        using var http = new HttpClient(new Handler(send));
        using var service = new GeoportalAddressService(http, Path.Combine(directory, name));
        var result = await service.FindAsync(Request());
        if (result.Found || result.ErrorCode != expected || string.IsNullOrWhiteSpace(result.Reason))
            throw new InvalidOperationException($"Unexpected {name} result: {result.ErrorCode} / {result.Reason}");
    }

    private static GeoportalAddressRequest Request() => new()
    {
        CompanyId = 1, ProvinceId = 2, CountyId = 1, MunicipalityId = 1, CityId = 1,
        Province = "Test", County = "Test", Municipality = "Test", City = "Test",
        Street = "Test", BuildingNumber = "1", PostalCode = "00-001"
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
