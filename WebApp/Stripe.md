# Stripe Checkout

Purchases use one-time Checkout payments in PLN with card and BLIK. The backend
selects the product name, price and duration from PlanDefinitions. Angular never
sends a price or a return URL. Existing StripePriceId values are not required.

## Configuration

- Stripe:SecretKey: keep in .NET user-secrets or a server secret store, never Git.
- Stripe:WebhookSecret: signing secret for this endpoint or local CLI listener.
- Stripe:FrontendUrl: trusted HTTPS URL of Angular, currently https://localhost:44345/.

Development secrets use the ID ColdCall-LocalDevelopment. The supplied test key
is configured locally, not in appsettings.json. Production needs its own secrets.

## Local webhooks

Install Stripe CLI, then run Scripts/Start-StripeListener.ps1. Its StripeCli
parameter accepts an explicit path to stripe.exe when it is not on PATH.
The script starts a hidden test-only listener and saves its signing secret to
user-secrets. Restart the backend afterward if it was already running.
The listener must remain running to deliver events to localhost. The script's
certificate-verification exception is for the local development certificate only.

For a deployed backend, register a public HTTPS webhook in Stripe instead:
POST /api/plans/stripe-webhook. Subscribe to checkout.session.completed and
checkout.session.async_payment_succeeded, and configure that endpoint's secret.

## Confirmation

Stripe redirects to /payment/success?session_id={CHECKOUT_SESSION_ID}.
The authenticated page calls POST /api/plans/confirm-checkout, refreshes the user
and redirects to /profile/plan after the server confirms payment. A one-time dialog
shows the purchased plan, HTML description and expiration. Clicking OK opens
/dashboard. Opening the profile normally does not show a purchase confirmation.

The webhook and return page use the same fulfillment function. It retrieves the
session from Stripe, checks its application, owner, paid status, amount and currency,
then updates the existing UserPlans row (or creates one if none exists) with the
purchased PlanDefinitionId, IsActive=true, Source=Stripe, StripeSessionId and an
expiration of assignment time UTC plus PlanDefinition.ExpiresAfterDays for regular
plans. Upgrades add PlanDefinition.ExpiresAfterDays to the existing UserPlan's
ExpiresAtUtc instead, preserving the remaining period. StripePayments
records processed sessions separately so replaying an earlier purchase cannot
revert a newer plan or renew its expiration. SQL application locks prevent duplicate
grants, including concurrent webhook and browser requests.
Canceling or leaving an unpaid Checkout session does not grant a plan.

Use only Stripe test payment details with the test key. Hosted Checkout handles
card/BLIK input; neither Angular nor the backend receives card numbers or BLIK codes.
