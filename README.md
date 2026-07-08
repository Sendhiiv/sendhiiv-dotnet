# Sendhiiv .NET SDK

Official .NET client for the [Sendhiiv](https://sendhiiv.com) email API.
Targets `netstandard2.0` — works on **.NET Framework 4.6.2+**, .NET 6/8+, and Mono.

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

Nothing special is required: the package targets `netstandard2.0`, so it
installs into Framework 4.6.2+ projects directly. All calls are async; if
you're stuck in synchronous code, `.GetAwaiter().GetResult()` is safe here
because the SDK awaits internally with `ConfigureAwait(false)` — it won't
deadlock on the ASP.NET/WinForms synchronization context.

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
  "retry": { "automatic": true, "retryable_temporary_failures": true }
}
```

| Property | Meaning |
| --- | --- |
| `Success` | `true` on 202. |
| `Status` | `"queued"`. |
| `Code` | `"QUEUED_FOR_DELIVERY"`. |
| `Message` | Human-readable summary. |
| `Total` | Number of recipients queued. |
| `Retry` | Server-side behavior: Sendhiiv retries temporary delivery failures itself after queueing. |

Queued means accepted for delivery, not delivered — delivery status shows up in
your dashboard's activity log.

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
since the message may already have been accepted. Sendhiiv itself retries
temporary delivery failures server-side after a message is queued.

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
