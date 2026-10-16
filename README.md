# Sendhiiv .NET SDK

Official .NET client for the [Sendhiiv](https://sendhiiv.com) email API.
On .NET Framework the package has **zero dependencies** — installing it never
adds, upgrades, or conflicts with Newtonsoft.Json or anything else in your
project. Modern targets use `System.Text.Json`.

## Supported runtimes

| Your project | Supported | Build NuGet installs |
| --- | --- | --- |
| .NET 10, 9, 8 | yes | `net8.0` |
| .NET 6, 7, .NET Core 2.0+ | yes | `netstandard2.0` |
| .NET Framework 4.5 – 4.8 | yes | `net45` (no dependencies) |
| Mono, Xamarin, Unity | yes | `netstandard2.0` |

The package ships three builds and NuGet picks the right one for you. .NET 9,
10, and future releases run the `net8.0` build — newer .NET runtimes load
assemblies built for earlier versions, which is why the package page lists
only the compiled targets while remaining compatible with everything above.

```
dotnet add package Sendhiiv
```

or in the NuGet Package Manager Console: `Install-Package Sendhiiv`

## Quickstart

```csharp
using Sendhiiv;

var sendhiiv = new SendhiivClient(Environment.GetEnvironmentVariable("SENDHIIV_API_KEY"));

var result = await sendhiiv.Messages.SendAsync(new SendMessageParams
{
    From = "Acme <hello@yourdomain.com>",
    To = { "customer@example.com" },
    Subject = "Welcome aboard",
    Html = "<p>Hi there, your account is ready.</p>",
});

Console.WriteLine(result.Message); // "1 email(s) queued for delivery"
```

Get an API key from your [Sendhiiv dashboard](https://app.sendhiiv.com) under
**Settings → API**. The free tier includes 3,000 emails/month. Keys look like
`sh_live_...` — keep them in an environment variable or user secrets, not in code.

## How the SDK is organized

There are only a handful of public types, and they map one-to-one onto the API:

| Type | What it's for |
| --- | --- |
| `SendhiivClient` | Entry point. Create one and reuse it for the life of your app. |
| `SendhiivClient.Messages` | The messages resource. `Messages.SendAsync(...)` is the send call. |
| `SendhiivClientOptions` | Optional constructor settings: timeout, retries, base URL, your own `HttpClient`. |
| `SendMessageParams` | The email you want to send. Plain object, all the fields are below. |
| `Attachment` | One file attached to a message. |
| `SendMessageResponse` | What a successful send returns (the API answers 202 Accepted). |
| `SendhiivException` | Thrown for any non-2xx response, network failure, or timeout. |
| `ComplianceInfo` | Extra detail on `SendhiivException` when content was blocked by review. |

The API currently has one endpoint (`POST /messages`), so `Messages.SendAsync`
is the only method you'll call. Everything else is data going in or coming out
of it. Resources are grouped the way you'd expect from Stripe-style clients, so
when more endpoints ship they'll appear as new properties on the client
(`sendhiiv.Domains`, etc.) without breaking anything.

`SendhiivClient` is thread-safe and holds a single `HttpClient` internally.
Make one instance and share it; don't create a client per request.

## SendMessageParams, field by field

| Property | JSON field | Required | Notes |
| --- | --- | --- | --- |
| `To` | `to` | yes | One or many recipient addresses. One request with 200 recipients is cheaper than 200 requests. |
| `Subject` | `subject` | usually | Can only be omitted when a *message* template supplies its own subject. |
| `Html` | `html` | see below | HTML body. When combined with a layout template, this content is placed inside the layout. |
| `Text` | `text` | see below | Plain-text body. |
| `TemplateKey` | `template_key` | see below | Key of a saved layout or message template, e.g. `"brand-layout"`. Template keys are listed on the Templates page of the dashboard. |
| `From` | `from` | no | Display sender, e.g. `"Acme <hello@yourdomain.com>"`. The domain must be verified in your account. Omit it to send from the shared sender. |
| `ReplyTo` | `reply_to` | no | Reply-To address. |
| `Variables` | `variables` | no | Dictionary of values for `{{merge}}` tags in the subject, body, or template. `Variables["firstName"] = "Ada"` fills `{{firstName}}`. |
| `Attachments` | `attachments` | no | List of `Attachment`. 10 MB total per message. |
| `SendMode` | `send_mode` | no | Set to `"drip"` to schedule recipients in batches instead of sending all at once. |
| `BatchSize` | `batch_size` | no | Drip only. Recipients per batch, default 50, max 500. |
| `BatchIntervalMinutes` | `batch_interval_minutes` | no | Drip only. Minutes between batches, default 15, max 1440. |

The one rule to remember: **every message needs `To` plus at least one of
`Html`, `Text`, or `TemplateKey`**. The rest is optional.

The serializer skips null properties, so you only set what you use — an unset
property is simply absent from the request.

## A complete console app

Starting from nothing:

```
dotnet new console -n EmailDemo
cd EmailDemo
dotnet add package Sendhiiv
```

`Program.cs`:

```csharp
using Sendhiiv;

var apiKey = Environment.GetEnvironmentVariable("SENDHIIV_API_KEY")
    ?? throw new InvalidOperationException("Set SENDHIIV_API_KEY first.");

var sendhiiv = new SendhiivClient(apiKey);

try
{
    var result = await sendhiiv.Messages.SendAsync(new SendMessageParams
    {
        To = { "you@example.com" },
        Subject = "Hello from the SDK",
        Html = "<p>It works.</p>",
    });

    Console.WriteLine($"{result.Status}: {result.Message} ({result.Total} recipient(s))");
}
catch (SendhiivException ex)
{
    Console.Error.WriteLine($"Send failed (HTTP {ex.Status}, code {ex.Code}): {ex.Message}");
}
```

Run it:

```
set SENDHIIV_API_KEY=sh_live_...     (Windows)
export SENDHIIV_API_KEY=sh_live_...  (macOS/Linux)
dotnet run
```

Note there's no `From` in that example — without a verified domain the message
goes out via the shared sender, which is fine for trying things out.

## Using it in ASP.NET Core

Register the client once as a singleton. If you use `IHttpClientFactory`,
hand its client to the SDK so your existing pooling and logging apply:

```csharp
// Program.cs
builder.Services.AddHttpClient("sendhiiv");
builder.Services.AddSingleton(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("sendhiiv");
    return new SendhiivClient(
        builder.Configuration["Sendhiiv:ApiKey"]!,
        new SendhiivClientOptions { HttpClient = httpClient });
});
```

with the key in `appsettings.json` / user secrets / environment:

```json
{ "Sendhiiv": { "ApiKey": "sh_live_..." } }
```

Then inject it wherever you send mail:

```csharp
public class SignupService
{
    private readonly SendhiivClient _sendhiiv;

    public SignupService(SendhiivClient sendhiiv) => _sendhiiv = sendhiiv;

    public async Task SendWelcomeAsync(string email, string firstName, CancellationToken ct)
    {
        await _sendhiiv.Messages.SendAsync(new SendMessageParams
        {
            From = "Acme <hello@yourdomain.com>",
            To = { email },
            Subject = "Welcome to Acme",
            TemplateKey = "welcome-email",
            Variables = new Dictionary<string, object> { ["firstName"] = firstName },
        }, ct);
    }
}
```

`SendAsync` takes an optional `CancellationToken`, so request-aborted
cancellation flows through naturally.

The SDK never disposes an `HttpClient` you pass in — lifetime stays yours.

## .NET Framework

Nothing special is required — `Install-Package Sendhiiv` works in Framework
projects from 4.5 up, including old ASP.NET MVC / Web API / WebForms apps.
Every Framework project (4.5 through 4.8) gets the dedicated `net45` build.

Details worth knowing on old Framework versions:

- **Zero dependencies, on purpose.** Legacy apps break when a package forces
  a Newtonsoft.Json upgrade, so the Framework build doesn't use Newtonsoft at
  all — it serializes with the framework's built-in serializer
  (`System.Web.Extensions`, already on every machine). Installing `Sendhiiv`
  adds exactly one entry to your `packages.config`: `Sendhiiv`. Whatever
  JSON library your app uses, at whatever version, stays untouched.
- **TLS is handled for you.** .NET 4.5 negotiates TLS 1.0 by default, which
  the API rejects. The Framework build enables TLS 1.2 itself the first time
  you create a `SendhiivClient`, so sends work without touching
  `ServicePointManager` or the registry.
- **Synchronous code is fine.** All calls are async, but
  `.GetAwaiter().GetResult()` won't deadlock on the ASP.NET/WinForms
  synchronization context, because the SDK awaits internally with
  `ConfigureAwait(false)`.

The test suite runs against both the modern build and the `net45` build on the
classic CLR, so the Framework path is exercised on every change, not just
compiled.

### "Could not load file or assembly 'Newtonsoft.Json'"

If you see `Could not load file or assembly 'Newtonsoft.Json, Version=...'`
(HRESULT 0x80131040) after installing, you're on an SDK version older than
0.3.0 — those briefly depended on Newtonsoft. Update the package
(`Update-Package Sendhiiv`) and the error is gone for good: 0.3.0+ has no
Newtonsoft reference at all. If the old SDK version upgraded Newtonsoft in
your project and that upgrade caused problems elsewhere, you're free to roll
Newtonsoft back to the version you had — this package no longer cares.

## What a successful send returns

The API queues messages and answers `202 Accepted`. `SendMessageResponse` looks
like this on the wire:

```json
{
  "success": true,
  "status": "queued",
  "code": "QUEUED_FOR_DELIVERY",
  "message": "1 email(s) queued for delivery",
  "total": 1,
  "data": [{ "id": "msg_6ad2009154c402f10b8caf8e", "to": "jane@example.com" }],
  "retry": { "automatic": true, "retryable_temporary_failures": true }
}
```

| Property | Meaning |
| --- | --- |
| `Success` | `true` on 202. |
| `Status` | `"queued"`, or `"accepted"` for a test-mode send. |
| `Code` | `"QUEUED_FOR_DELIVERY"`, or `"TEST_MODE_ACCEPTED"`. |
| `Message` | Human-readable summary. |
| `Total` | Number of recipients accepted. |
| `Data` | One `AcceptedMessage` per recipient, in the order you listed them. |
| `Mode` | `"drip"` for a batched send, `"test"` for a `sh_test_` key; otherwise null. |
| `Retry` | Server-side behavior: Sendhiiv retries temporary delivery failures itself after queueing. |

**Store the `Id` on each entry in `Data`.** It is what every webhook event for
that message carries as `message_id`, and what the status endpoint takes:

```csharp
var result = await sendhiiv.Messages.SendAsync(message);

foreach (var accepted in result.Data!)
{
    _logger.LogInformation("Queued {Id} for {To}", accepted.Id, accepted.To);
    await _receipts.RecordMessageIdAsync(accepted.To!, accepted.Id!);
}
```

Queued means accepted for delivery, not delivered — delivery status arrives on
your webhook endpoint, or from `GET /api/v1/messages/{id}`, which this SDK does
not wrap yet (call it with `HttpClient` for now).

## Idempotency

If a send is accepted but the response never reaches you — a timeout, a dropped
connection — retrying blind would deliver the message twice. Pass an
idempotency key and the retry is safe:

```csharp
await sendhiiv.Messages.SendAsync(message, $"receipt-{order.Id}");
```

A retry with the same key returns the **original** response, with the same
message ids, and sends nothing further. Keys are remembered for 24 hours, so
derive them from something stable in your own system — an order id, an invoice
number, a job id — rather than a fresh `Guid` per attempt.

Reusing a key with a different message throws a `SendhiivException` with code
`IDEMPOTENCY_KEY_REUSED` (HTTP 422). A request that was *rejected* frees its key
immediately, so you can fix it and retry with the same one.

## Test mode

A key beginning `sh_test_` runs every check a live send does — plan, required
fields, template rendering, attachment limits, sender and domain resolution,
your suppression list, content compliance — and then stops before delivery.

```csharp
var sendhiiv = new SendhiivClient(config["Sendhiiv:TestKey"]); // sh_test_...

var result = await sendhiiv.Messages.SendAsync(message);
// result.Mode   == "test"
// result.Status == "accepted"
// result.Data   holds real ids you can look up
```

Nothing is delivered, no quota is spent, and test sends stay out of your
delivery statistics and sender reputation — which makes this safe to use in
integration tests and staging. Your webhooks still fire, with `"mode": "test"`
in the payload, so a handler can be verified end to end. Create a test key from
the dashboard by choosing the Test environment.

## Error handling

Every non-2xx response throws a `SendhiivException`. It carries:

| Property | Meaning |
| --- | --- |
| `Status` | HTTP status code. `0` for network errors and timeouts. |
| `Code` | Machine-readable code such as `"QUOTA_EXCEEDED"`, or null when the API didn't send one. |
| `Compliance` | Score, severity, and reasons — only set when `Code` is `"CONTENT_COMPLIANCE_BLOCKED"`. |
| `ResponseBody` | The raw response body, when one was received. Useful for logging. |
| `Message` | The API's error text, or a description of the network failure. |

```csharp
try
{
    await sendhiiv.Messages.SendAsync(message);
}
catch (SendhiivException ex)
{
    switch (ex.Code)
    {
        case "CONTENT_COMPLIANCE_BLOCKED":
            logger.LogWarning("Blocked: {Reasons}", string.Join("; ", ex.Compliance?.Reasons ?? new()));
            break;
        case "QUOTA_EXCEEDED":       // 429 — monthly plan quota reached
        case "ATTACHMENT_TOO_LARGE": // 413 — 10 MB total limit
        default:
            logger.LogError("Sendhiiv HTTP {Status}: {Message}", ex.Status, ex.Message);
            break;
    }
}
```

| Status | Meaning |
| --- | --- |
| 202 | Accepted — message(s) queued for delivery |
| 400 | Invalid request (missing `to`, bad attachments, content blocked by compliance review — check `Code`) |
| 401 | Missing, invalid, or revoked API key |
| 402 | Pay-as-you-go balance exhausted |
| 403 | Plan does not include API access |
| 413 | Attachments exceed 10 MB total |
| 429 | Rate limit (100 requests/min) or plan quota reached |

## Retries and timeouts

The SDK retries **only** HTTP 429 responses (honoring `Retry-After`), because
the rate limiter runs before anything is queued — a retry can never
double-send. Network errors and 5xx responses are not retried automatically,
since the message may already have been accepted.

If you want those retried too — through Polly, or a hosted-service retry loop —
pass an idempotency key: with one, a repeated request returns the original
response instead of sending again. Sendhiiv also retries temporary *delivery*
failures server-side after a message is queued, which is a separate thing from
retrying the API call.

All the knobs live on `SendhiivClientOptions`:

```csharp
var sendhiiv = new SendhiivClient(apiKey, new SendhiivClientOptions
{
    Timeout = TimeSpan.FromSeconds(30), // per-request timeout (default 30s)
    MaxRetries = 2,                     // 429 retries (default 2)
    HttpClient = httpClientFromFactory, // optional: bring your own HttpClient
    BaseUrl = "https://api.sendhiiv.com/api/v1", // default; override for testing
});
```

## License

MIT
