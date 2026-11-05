using System.Globalization;
using ColdCall.Data;
using ColdCall.Models;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

namespace ColdCall.Services;

public interface IStripeCheckoutGateway
{
    Task<Session> CreateAsync(SessionCreateOptions options, CancellationToken token);
    Task<Session> GetAsync(string id, CancellationToken token);
}

public class StripeCheckoutGateway(IConfiguration configuration) : IStripeCheckoutGateway
{
    private SessionService Sessions()
    {
        var key = configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Nie skonfigurowano klucza Stripe.");
        return new SessionService(new StripeClient(key));
    }

    public Task<Session> CreateAsync(SessionCreateOptions options, CancellationToken token) =>
        Sessions().CreateAsync(options, cancellationToken: token);

    public Task<Session> GetAsync(string id, CancellationToken token) =>
        Sessions().GetAsync(id, cancellationToken: token);
}

public class StripePaymentService(ColdCallDbContext db, IStripeCheckoutGateway stripe, IConfiguration configuration, InFaktService inFakt)
{
    public async Task<Session> CreateAsync(AppUser user, string planCode, CancellationToken token)
    {
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
        return await stripe.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment", CustomerEmail = user.GoogleEmail,
            ClientReferenceId = user.Id.ToString(CultureInfo.InvariantCulture),
            SuccessUrl = new Uri(frontend, "payment/success").AbsoluteUri + "?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = new Uri(frontend, "plans").AbsoluteUri + "?canceled=true",
            PaymentMethodTypes = new List<string> { "card", "blik" },
            LineItems = new List<SessionLineItemOptions>
            {
                new() { Quantity = 1, PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = "pln", UnitAmount = amount,
                    ProductData = new SessionLineItemPriceDataProductDataOptions { Name = plan.Code }
                } }
            },
            Metadata = new Dictionary<string, string>
            {
                ["application"] = "ColdCall", ["userId"] = user.Id.ToString(CultureInfo.InvariantCulture),
                ["planId"] = plan.Id.ToString(CultureInfo.InvariantCulture),
                ["expiresAfterDays"] = plan.ExpiresAfterDays.Value.ToString(CultureInfo.InvariantCulture),
                ["amount"] = amount.ToString(CultureInfo.InvariantCulture)
            }
        }, token);
    }

    public async Task<bool> FulfillAsync(string sessionId, int? expectedUserId, CancellationToken token)
    {
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
                if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsEnabled && !u.IsAdmin, token)
                    || purchasedPlan == null || purchasedPlan.ExpiresAfterDays is not > 0)
                    throw new InvalidOperationException("Nie znaleziono uzytkownika lub zakupionego planu.");
                var now = DateTime.UtcNow;
                var legacy = await db.UserPlans.AsNoTracking().FirstOrDefaultAsync(p => p.StripeSessionId == sessionId, token);
                if (legacy != null)
                {
                    db.StripePayments.Add(new StripePayment
                    {
                        StripeSessionId = sessionId, AppUserId = legacy.AppUserId, PlanDefinitionId = legacy.PlanDefinitionId,
                        PaidAtUtc = legacy.PurchasedAtUtc, AmountPaid = legacy.PricePaid
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
                            StripeSessionId = userPlan.StripeSessionId, AppUserId = userId, PlanDefinitionId = userPlan.PlanDefinitionId,
                            PaidAtUtc = userPlan.PurchasedAtUtc, AmountPaid = userPlan.PricePaid
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
                        StripeSessionId = sessionId, AppUserId = userId, PlanDefinitionId = planId,
                        PaidAtUtc = now, AmountPaid = amount / 100m
                    });
                }
                await db.SaveChangesAsync(token);
            }
            await transaction.CommitAsync(token);
        }
        await EnsureInvoiceAsync(sessionId, token);
        return true;
    }

    private async Task EnsureInvoiceAsync(string sessionId, CancellationToken token)
    {
        // A session-owned lock serializes webhook/browser confirmation without keeping
        // a database transaction open during external HTTP calls.
        var resource = "InFaktInvoice:" + sessionId;
        await db.Database.OpenConnectionAsync(token);
        var acquired = false;
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=30000;
                IF @result < 0 THROW 50001, 'Invoice generation lock unavailable', 1;", token);
            acquired = true;
            var payment = await db.StripePayments.SingleAsync(p => p.StripeSessionId == sessionId, token);
            await db.Entry(payment).ReloadAsync(token);
            if (payment.PDFInvoice is { Length: > 0 } && !string.IsNullOrWhiteSpace(payment.InvoiceId)) return;

            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == payment.AppUserId, token);
            var companyName = (user.CompanyName ?? (user.FirstName + " " + user.LastName)).Trim();
            if (string.IsNullOrWhiteSpace(companyName))
                throw new InvalidOperationException("The user's company name or first and last name is required for invoicing.");
            if (!payment.ClientId.HasValue)
            {
                InFaktClient? company = null;
                string? pageUrl = null;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    if (!visited.Add(pageUrl ?? "first"))
                        throw new InvalidDataException("inFakt returned a repeated client pagination URL.");
                    var clients = await inFakt.RetrieveCompanies(token, pageUrl);
                    company = clients.Entities.FirstOrDefault(client =>
                        string.Equals(client.CompanyName?.Trim(), companyName, StringComparison.OrdinalIgnoreCase));
                    pageUrl = string.IsNullOrWhiteSpace(clients.Metainfo.Next) ? null : clients.Metainfo.Next;
                } while (company == null && pageUrl != null);
                company ??= await inFakt.CreateCompany(new InFaktCreateCompanyRequest
                {
                    CompanyName = companyName, Street = user.Street ?? "", StreetNumber = user.StreetNumber ?? "",
                    City = user.City ?? "", Nip = user.Nip ?? "", PostCode = user.PostalCode ?? "", Country = "PL"
                }, token);
                if (company.Id <= 0 || company.Id > int.MaxValue)
                    throw new InvalidDataException("inFakt returned an invalid client ID.");
                payment.ClientId = (int)company.Id;
                await db.SaveChangesAsync(token);
            }
            if (string.IsNullOrWhiteSpace(payment.InvoiceId))
            {
                if (string.IsNullOrWhiteSpace(payment.InvoiceNumber))
                {
                    // Reserve MAX + 1 across all payments and persist it before calling inFakt.
                    await using (var numbering = await db.Database.BeginTransactionAsync(token))
                    {
                        await db.Database.ExecuteSqlRawAsync(@"
                            DECLARE @result int;
                            EXEC @result=sys.sp_getapplock @Resource='StripePayments:InvoiceNumber', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
                            IF @result < 0 THROW 50001, 'Invoice numbering lock unavailable', 1;", token);
                        var maximum = await db.Database.SqlQuery<int?>($"SELECT MAX(TRY_CONVERT(int, InvoiceNumber)) AS [Value] FROM ColdCall.StripePayments").SingleAsync(token) ?? 0;
                        payment.InvoiceNumber = checked(maximum + 1).ToString(CultureInfo.InvariantCulture);
                        await db.SaveChangesAsync(token);
                        await numbering.CommitAsync(token);
                    }
                }
                var planCode = await db.PlanDefinitions.Where(plan => plan.Id == payment.PlanDefinitionId)
                    .Select(plan => plan.Code).SingleAsync(token);
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")));
                var invoice = await inFakt.CreateInvoice(companyName, payment.InvoiceNumber,
                    today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), planCode, payment.AmountPaid, token);
                if (!invoice.TryGetProperty("uuid", out var uuid) || uuid.ValueKind != System.Text.Json.JsonValueKind.String
                    || !Guid.TryParse(uuid.GetString(), out var invoiceId))
                    throw new InvalidDataException("inFakt returned an invalid invoice UUID.");
                payment.InvoiceId = invoiceId.ToString();
                await db.SaveChangesAsync(token);
            }
            var pdf = await inFakt.GetInvoice(payment.InvoiceId, token);
            if (pdf.Length == 0) throw new InvalidDataException("inFakt returned an empty invoice PDF.");
            payment.PDFInvoice = pdf;
            await db.SaveChangesAsync(token);
        }
        finally
        {
            try
            {
                if (acquired)
                    await db.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource={resource}, @LockOwner='Session';", CancellationToken.None);
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
}
