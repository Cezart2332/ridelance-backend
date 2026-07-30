# Clean Architecture Template

What's included in the template?

- SharedKernel project with common Domain-Driven Design abstractions.
- Domain layer with sample entities.
- Application layer with abstractions for:
  - CQRS
  - Example use cases
  - Cross-cutting concerns (logging, validation)
- Infrastructure layer with:
  - Authentication
  - Permission authorization
  - EF Core, PostgreSQL
  - Serilog
- Seq for searching and analyzing structured logs
  - Seq is available at http://localhost:8081 by default
- Testing projects
  - Architecture testing

I'm open to hearing your feedback about the template and what you'd like to see in future iterations.

If you're ready to learn more, check out [**Pragmatic Clean Architecture**](https://www.milanjovanovic.tech/pragmatic-clean-architecture?utm_source=ca-template):

- Domain-Driven Design
- Role-based authorization
- Permission-based authorization
- Distributed caching with Redis
- OpenTelemetry
- Outbox pattern
- API Versioning
- Unit testing
- Functional testing
- Integration testing

Stay awesome!

## Stripe

Nothing about what we sell is tied to a Stripe account. Prices live in
`Domain/Payments/StripeCatalog.cs` and are looked up by `lookup_key`; the first payment on a
flow creates the product and price in whatever account is configured. Moving to another
account — or from test to live — means changing three secrets and nothing else:

- `Stripe__SecretKey` and the frontend's `VITE_PUBLIC_STRIPE` — they must come from the same
  account and the same mode, because checkout is embedded (the client secret is issued with
  the secret key and mounted with the publishable one).
- `Stripe__WebhookSecret` — webhook endpoints are per account and per mode, so create a new
  endpoint pointing at `POST /payments/webhook/stripe` and subscribe it to
  `checkout.session.completed`, `invoice.payment_succeeded`, `invoice.payment_failed` and
  `customer.subscription.deleted`. Outside Development the app refuses to start without this
  secret: the endpoint is anonymous, so unverified payloads would let anyone grant themselves
  a subscription.

A Stripe price cannot be edited after creation. To change an amount, change the `lookup_key`
in the catalog too (e.g. append `_v2`) — otherwise the old price keeps being found and the new
amount never takes effect. A mismatch between the account and the catalog is logged as a warning.
