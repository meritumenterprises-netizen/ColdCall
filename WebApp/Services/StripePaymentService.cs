using ColdCall.Data;
using ColdCall.Models;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using System.Globalization;
using System.Text.Json;

namespace ColdCall.Services;

public interface IStripeCheckoutGateway
{
    Task<Session> CreateAsync(SessionCreateOptions options, CancellationToken token);
    Task<Session> GetAsync(string id, CancellationToken token);
}

public class StripeCheckoutGateway(IConfiguration configuration, HttpClient httpClient) : IStripeCheckoutGateway
{
    private StripeClient CreateClient()
    {
        var key = configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Nie skonfigurowano klucza Stripe.");
        return new StripeClient(new StripeClientOptions { ApiKey = key, HttpClient = new SystemNetHttpClient(httpClient) });
    }

    public async Task<Session> CreateAsync(SessionCreateOptions options, CancellationToken token)
    {
        var taxRateId = configuration["Stripe:Vat23TaxRateId"];
        var key = configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Nie skonfigurowano klucza Stripe.");
        var client = CreateClient();
        TaxRate rate;
        try
        {
            rate = await new TaxRateService(client).GetAsync(taxRateId, cancellationToken: token);
        }
        catch (StripeException exception) when (exception.StripeError?.Code == "resource_missing")
        {
            var environment = key.Contains("_test_", StringComparison.Ordinal) ? "testowym" : "produkcyjnym";
            throw new InvalidOperationException(
                $"Nie znaleziono stawki VAT w srodowisku {environment} Stripe. Ustaw Stripe:Vat23TaxRateId na stawke 23% Exclusive z tego samego srodowiska.", exception);
        }
        if (!rate.Active || rate.Inclusive || rate.Percentage != 23m)
            throw new InvalidOperationException("Stawka Stripe VAT musi byc aktywna, wynosic 23% i byc doliczana do ceny netto (exclusive).");
        return await new SessionService(client).CreateAsync(options, cancellationToken: token);
    }

    public Task<Session> GetAsync(string id, CancellationToken token) =>
        new SessionService(CreateClient()).GetAsync(id, cancellationToken: token);
}

public class StripePaymentService(ColdCallDbContext db, IStripeCheckoutGateway stripe, IConfiguration configuration, InFaktService inFakt,
    ILogger<StripePaymentService>? logger = null)
{
    public async Task<Session> CreateAsync(AppUser user, string planCode, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        token = timeout.Token;
        if (user.DisablePlanPurchase)
            throw new InvalidOperationException("Zakup planow jest wylaczony dla tego konta.");
        if (user.IsBetaUser && UserProfileCompletion.IsRequired(user))
            throw new InvalidOperationException("Uzupelnij wymagane dane w Profil / Dane przed zakupem planu.");
        var plan = await db.PlanDefinitions.AsNoTracking().SingleOrDefaultAsync(p => p.Code == planCode, token);
        if (user.IsAdmin || !user.IsEnabled || plan == null || !plan.IsActive || plan.IsTrial || !plan.ShowInOffer
            || plan.ExpiresAfterDays is not > 0 || plan.Price <= 0 || !string.Equals(plan.Currency, "PLN", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ten plan nie jest dostepny do zakupu.");
        var current = await db.UserPlans.AsNoTracking().Where(p => p.AppUserId == user.Id && p.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(p => p.ExpiresAtUtc).ThenByDescending(p => p.Id).Select(p => p.PlanDefinition).FirstOrDefaultAsync(token);
        var allowed = PlanOfferRules.CanPurchase(current, plan);
        if (!allowed) throw new InvalidOperationException("Wybrany plan nie jest dostepny dla Twojego aktualnego planu.");
        if (!Uri.TryCreate(configuration["Stripe:FrontendUrl"], UriKind.Absolute, out var frontend)
            || frontend.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(frontend.Query) || !string.IsNullOrEmpty(frontend.Fragment))
            throw new InvalidOperationException("Nie skonfigurowano adresu HTTPS witryny dla Stripe.");
        var amount = checked((long)(plan.Price * 100));
        if (amount != plan.Price * 100) throw new InvalidOperationException("Niepoprawna cena planu.");
        var taxRateId = configuration["Stripe:Vat23TaxRateId"]?.Trim();
        if (string.IsNullOrWhiteSpace(taxRateId) || !taxRateId.StartsWith("txr_", StringComparison.Ordinal))
            throw new InvalidOperationException("Ustaw Stripe:Vat23TaxRateId na identyfikator stawki VAT 23% exclusive w Stripe.");
        var vatAmount = checked((long)decimal.Round(amount * 0.23m, 0, MidpointRounding.AwayFromZero));
        var grossAmount = checked(amount + vatAmount);
        return await stripe.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            CustomerEmail = user.GoogleEmail,
            ClientReferenceId = user.Id.ToString(CultureInfo.InvariantCulture),
            SuccessUrl = new Uri(frontend, "payment/success").AbsoluteUri + "?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = new Uri(frontend, "plans").AbsoluteUri + "?canceled=true",
            PaymentMethodTypes = new List<string> { "card", "blik" },
            LineItems = new List<SessionLineItemOptions>
            {
                new() { Quantity = 1, TaxRates = new List<string> { taxRateId }, PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = "pln", UnitAmount = amount, TaxBehavior = "exclusive",
                    ProductData = new SessionLineItemPriceDataProductDataOptions { Name = plan.Code }
                } }
            },
            Metadata = new Dictionary<string, string>
            {
                ["application"] = "ColdCall",
                ["userId"] = user.Id.ToString(CultureInfo.InvariantCulture),
                ["planId"] = plan.Id.ToString(CultureInfo.InvariantCulture),
                ["expiresAfterDays"] = plan.ExpiresAfterDays.Value.ToString(CultureInfo.InvariantCulture),
                ["amount"] = grossAmount.ToString(CultureInfo.InvariantCulture),
                ["netAmount"] = amount.ToString(CultureInfo.InvariantCulture),
                ["vatPercent"] = "23"
            }
        }, token);
    }

    public async Task<bool> FulfillAsync(string sessionId, int? expectedUserId, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        token = timeout.Token;
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 255 || !sessionId.StartsWith("cs_", StringComparison.Ordinal))
            throw new InvalidOperationException("Niepoprawna sesja platnosci.");
        var session = await stripe.GetAsync(sessionId, token);
        var metadata = session.Metadata ?? new Dictionary<string, string>();
        if (session.Mode != "payment" || metadata.GetValueOrDefault("application") != "ColdCall"
            || !int.TryParse(metadata.GetValueOrDefault("userId"), out var userId)
            || !int.TryParse(metadata.GetValueOrDefault("planId"), out var planId)
            || !int.TryParse(metadata.GetValueOrDefault("expiresAfterDays"), out var days) || days <= 0
            || !long.TryParse(metadata.GetValueOrDefault("amount"), out var amount) || amount <= 0
            || session.ClientReferenceId != userId.ToString(CultureInfo.InvariantCulture))
            throw new InvalidOperationException("Niepoprawne dane sesji Stripe.");
        if (expectedUserId.HasValue && expectedUserId != userId) throw new UnauthorizedAccessException();
        if (session.PaymentStatus != "paid" || session.Status != "complete") return false;
        if (session.AmountTotal != amount || session.Currency != "pln")
            throw new InvalidOperationException("Kwota lub waluta platnosci nie zgadza sie z zakupem.");
        var netAmount = amount;
        if (metadata.ContainsKey("netAmount"))
        {
            if (!long.TryParse(metadata["netAmount"], out netAmount) || netAmount <= 0
                || metadata.GetValueOrDefault("vatPercent") != "23")
                throw new InvalidOperationException("Niepoprawne dane VAT sesji Stripe.");
            var vatAmount = checked((long)decimal.Round(netAmount * 0.23m, 0, MidpointRounding.AwayFromZero));
            if (amount != checked(netAmount + vatAmount) || session.AmountSubtotal != netAmount
                || session.TotalDetails?.AmountTax != vatAmount
                || session.TotalDetails.AmountDiscount != 0 || session.TotalDetails.AmountShipping != 0)
                throw new InvalidOperationException("Kwoty netto, VAT i brutto sesji Stripe nie zgadzaja sie z zakupem.");
        }
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            foreach (var resource in new[] { "StripeSession:" + sessionId, "TrialPlan:" + userId })
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    DECLARE @result int;
                    EXEC @result=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
                    IF @result < 0 THROW 50001, 'Payment assignment lock unavailable', 1;", token);
            if (!await db.StripePayments.AnyAsync(p => p.StripeSessionId == sessionId, token))
            {
                var purchasedPlan = await db.PlanDefinitions.AsNoTracking()
                    .SingleOrDefaultAsync(p => p.Id == planId && !p.IsTrial, token);
                var purchasingUser = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsEnabled && !u.IsAdmin, token);
                if (purchasingUser == null
                    || purchasedPlan == null || purchasedPlan.ExpiresAfterDays is not > 0)
                    throw new InvalidOperationException("Nie znaleziono uzytkownika lub zakupionego planu.");
                var now = DateTime.UtcNow;
                var legacy = await db.UserPlans.AsNoTracking().FirstOrDefaultAsync(p => p.StripeSessionId == sessionId, token);
                if (legacy != null)
                {
                    db.StripePayments.Add(new StripePayment
                    {
                        StripeSessionId = sessionId,
                        AppUserId = legacy.AppUserId,
                        PlanDefinitionId = legacy.PlanDefinitionId,
                        PaidAtUtc = legacy.PurchasedAtUtc,
                        AmountPaid = amount / 100m
                    });
                }
                else
                {
                    var userPlan = await db.UserPlans.Where(p => p.AppUserId == userId)
                        .OrderByDescending(p => p.ExpiresAtUtc).ThenByDescending(p => p.Id).FirstOrDefaultAsync(token);
                    if (userPlan != null) await db.Entry(userPlan).ReloadAsync(token);
                    var expirationBase = purchasedPlan.IsUpgrade && userPlan != null ? userPlan.ExpiresAtUtc : now;
                    if (userPlan == null)
                    {
                        userPlan = new UserPlan { AppUserId = userId };
                        db.UserPlans.Add(userPlan);
                    }
                    else if (userPlan.Source == "Stripe" && !string.IsNullOrWhiteSpace(userPlan.StripeSessionId)
                        && !await db.StripePayments.AnyAsync(p => p.StripeSessionId == userPlan.StripeSessionId, token))
                    {
                        db.StripePayments.Add(new StripePayment
                        {
                            StripeSessionId = userPlan.StripeSessionId,
                            AppUserId = userId,
                            PlanDefinitionId = userPlan.PlanDefinitionId,
                            PaidAtUtc = userPlan.PurchasedAtUtc,
                            AmountPaid = userPlan.PricePaid
                        });
                    }
                    userPlan.PlanDefinitionId = planId;
                    userPlan.PurchasedAtUtc = now;
                    userPlan.ExpiresAtUtc = expirationBase.AddDays(purchasedPlan.ExpiresAfterDays.Value);
                    userPlan.PricePaid = amount / 100m;
                    userPlan.Source = "Stripe";
                    userPlan.StripeSessionId = sessionId;
                    userPlan.IsActive = true;
                    db.StripePayments.Add(new StripePayment
                    {
                        StripeSessionId = sessionId,
                        AppUserId = userId,
                        PlanDefinitionId = planId,
                        PaidAtUtc = now,
                        AmountPaid = amount / 100m
                    });
                }
                purchasingUser.IsBetaUser = false;
                await db.SaveChangesAsync(token);
            }
            await transaction.CommitAsync(token);
        }
        var invoicingEnabled = await db.GlobalSettings.AsNoTracking()
            .OrderBy(settings => settings.GlobalSettingsId)
            .Select(settings => settings.InFaktInvoicing).FirstOrDefaultAsync(token);
        if (invoicingEnabled) await EnsureInvoiceAsync(sessionId, netAmount / 100m, inFakt, token);
        return true;
    }

    private async Task EnsureInvoiceAsync(string sessionId, decimal netPrice, InFaktService inFakt, CancellationToken token)
    {
        var stage = "LoadPayment";
        try
        {
            var payment = await db.StripePayments.SingleAsync(p => p.StripeSessionId == sessionId, token);
            await db.Entry(payment).ReloadAsync(token);
            if (!string.IsNullOrWhiteSpace(payment.InvoiceId))
            {
                if (payment.PDFInvoice is { Length: > 0 }) return;
                stage = "MarkExistingInvoicePaid";
                await inFakt.MarkInvoicePaid(payment.InvoiceId, payment.PaidAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), token);
                stage = "DownloadExistingPdf";
                var existingPdf = await inFakt.GetInvoice(payment.InvoiceId, token);
                if (existingPdf.Length == 0) throw new InvalidDataException("inFakt returned an empty invoice PDF.");
                payment.PDFInvoice = existingPdf;
                await db.SaveChangesAsync(token);
                return;
            }
            var planDefinition = await db.PlanDefinitions.AsNoTracking()
                .SingleAsync(plan => plan.Id == payment.PlanDefinitionId, token);
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == payment.AppUserId, token);
            stage = "ValidateBuyer";
            var buyer = new InFaktInvoiceBuyer(
                string.IsNullOrWhiteSpace(user.CompanyName) ? null : user.CompanyName.Trim(),
                user.FirstName?.Trim(), user.LastName?.Trim(),
                string.IsNullOrWhiteSpace(user.Nip) ? null : user.Nip.Trim(),
                user.Street?.Trim() ?? "", user.StreetNumber?.Trim() ?? "",
                user.City?.Trim() ?? "", user.PostalCode?.Trim() ?? "");
            var buyerName = buyer.CompanyName ?? string.Join(" ", new[] { buyer.FirstName, buyer.LastName }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            if (string.IsNullOrWhiteSpace(buyerName)) buyerName = user.DisplayName?.Trim();
            if (string.IsNullOrWhiteSpace(buyerName))
                throw new InvalidOperationException("The buyer's name is required for invoicing.");
            if (string.IsNullOrWhiteSpace(buyer.City) || string.IsNullOrWhiteSpace(buyer.PostalCode)
                || string.IsNullOrWhiteSpace(buyer.Street) || string.IsNullOrWhiteSpace(buyer.StreetNumber))
                throw new InvalidOperationException("The buyer's complete address is required for invoicing.");
            if (!new PolishNipAttribute().IsValid(buyer.Nip))
                throw new InvalidOperationException("The buyer's NIP is invalid.");

            var maximumInvoiceId = checked(await db.StripePayments.CountAsync(token) + 1);
            var invoiceId = maximumInvoiceId;
            stage = "CreateInvoice";
            var jsonElement = await inFakt.CreateInvoice(buyerName, invoiceId, payment.PaidAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), planDefinition.Code, netPrice, token, buyer, payment.AmountPaid);

            if (!jsonElement.TryGetProperty("uuid", out var uuidElement)
                || uuidElement.ValueKind != JsonValueKind.String
                || !Guid.TryParse(uuidElement.GetString(), out var uuid))
            {
                throw new InvalidDataException("inFakt returned a missing or invalid invoice UUID.");
            }

            payment.InvoiceId = uuid.ToString();
            payment.InvoiceNumber = jsonElement.TryGetProperty("number", out var numberElement)
                && numberElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(numberElement.GetString())
                    ? numberElement.GetString()
                    : invoiceId.ToString(CultureInfo.InvariantCulture);
            stage = "SaveInvoiceId";
            await db.SaveChangesAsync(token);

            stage = "MarkInvoicePaid";
            await inFakt.MarkInvoicePaid(payment.InvoiceId, payment.PaidAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), token);
            stage = "DownloadPdf";
            var invoiceContent = await inFakt.GetInvoice(payment.InvoiceId, token);
            if (invoiceContent is { Length: > 0 })
            {
                payment.PDFInvoice = invoiceContent;
                await db.SaveChangesAsync(token);

            }
            else
            {
                throw new InvalidDataException("inFakt returned an empty invoice PDF.");
            }

        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Console.WriteLine("InFakt invoicing canceled because the confirmation request was aborted. The payment may already be recorded.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("InFakt HTTP request timed out. Its outcome is unknown; check inFakt before retrying invoice creation.");
        }
        catch (Exception exc)
        {
            logger?.LogError(exc, "InFakt invoice failed at {Stage} for Stripe session {SessionId}. HTTP status: {HttpStatus}",
                stage, sessionId, (exc as HttpRequestException)?.StatusCode);
            Console.WriteLine(exc.Message);
        }
    }
}
