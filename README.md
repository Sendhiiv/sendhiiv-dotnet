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

Get an API key from your [Sendhiiv dashboard](https://app.sendhiiv.com) —
the free tier includes 3,000 emails/month.

## Sending options

```csharp
await sendhiiv.Messages.SendAsync(new SendMessageParams
{
    To = { "a@example.com", "b@example.com" },
    Subject = "March invoice",
    TemplateKey = "brand-layout",              // saved layout or message template
    Html = "<p>Your invoice is attached.</p>",
    Variables = new Dictionary<string, object> { ["firstName"] = "Ada" },
    ReplyTo = "billing@yourdomain.com",
    Attachments = new List<Attachment>
    {
        new Attachment
        {
            Filename = "invoice.pdf",
            Content = Convert.ToBase64String(pdfBytes),
            ContentType = "application/pdf",
        },
    },
});
```

Drip mode spreads large recipient lists into scheduled batches:

```csharp
await sendhiiv.Messages.SendAsync(new SendMessageParams
{
    To = recipients,
    Subject = "Product update",
    Html = html,
    SendMode = "drip",
    BatchSize = 100,             // default 50, max 500
    BatchIntervalMinutes = 30,   // default 15, max 1440
});
```

## Error handling

Every non-2xx response throws a `SendhiivException` with `Status`, `Code`,
and the raw `ResponseBody`:

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
        case "QUOTA_EXCEEDED":   // 429 — monthly plan quota reached
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

```csharp
var sendhiiv = new SendhiivClient(apiKey, new SendhiivClientOptions
{
    Timeout = TimeSpan.FromSeconds(30), // per-request timeout (default 30s)
    MaxRetries = 2,                     // 429 retries (default 2)
    HttpClient = httpClientFromFactory, // optional: bring your own HttpClient
});
```

## License

MIT
