using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Sendhiiv.Tests;

/// <summary>Fake handler that replays scripted responses and records requests.</summary>
internal class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();
    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();

    public FakeHandler Enqueue(HttpStatusCode status, string json, string? retryAfter = null)
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (retryAfter != null)
                response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync();
        Calls.Add((request, body));
        var next = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
        return next();
    }
}

public class SendhiivClientTests
{
    private const string QueuedJson = """
        {
          "success": true,
          "status": "queued",
          "code": "QUEUED_FOR_DELIVERY",
          "message": "1 email(s) queued for delivery",
          "total": 1,
          "retry": { "automatic": true, "retryable_temporary_failures": true }
        }
        """;

    private static (SendhiivClient Client, FakeHandler Handler) Make(int maxRetries = 2)
    {
        var handler = new FakeHandler();
        var client = new SendhiivClient("sh_live_test", new SendhiivClientOptions
        {
            HttpClient = new HttpClient(handler),
            MaxRetries = maxRetries,
        });
        return (client, handler);
    }

    private static SendMessageParams Message() => new()
    {
        To = { "a@example.com" },
        Subject = "Hi",
        Html = "<p>Hi</p>",
    };

    [Fact]
    public void RequiresApiKey()
    {
        Assert.Throws<ArgumentException>(() => new SendhiivClient(""));
        Assert.Throws<ArgumentException>(() => new SendhiivClient("  "));
    }

    [Fact]
    public async Task SendsWithBearerAuthAndParsesQueueConfirmation()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.Accepted, QueuedJson);

        var result = await client.Messages.SendAsync(Message());

        Assert.True(result.Success);
        Assert.Equal("QUEUED_FOR_DELIVERY", result.Code);
        Assert.Equal(1, result.Total);
        Assert.True(result.Retry!.Automatic);

        var (request, _) = Assert.Single(handler.Calls);
        Assert.Equal("https://api.sendhiiv.com/api/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("sh_live_test", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SerializesSnakeCaseWireFormatAndOmitsNulls()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.Accepted, QueuedJson);

        await client.Messages.SendAsync(new SendMessageParams
        {
            To = { "a@example.com" },
            Subject = "Hi",
            Html = "<p>Hi</p>",
            ReplyTo = "reply@example.com",
            TemplateKey = "brand-layout",
            SendMode = "drip",
            BatchSize = 100,
            BatchIntervalMinutes = 30,
            Attachments = new List<Attachment>
            {
                new() { Filename = "a.pdf", Content = "aGk=", ContentType = "application/pdf" },
            },
        });

        using var sent = JsonDocument.Parse(handler.Calls[0].Body);
        var root = sent.RootElement;
        Assert.Equal("reply@example.com", root.GetProperty("reply_to").GetString());
        Assert.Equal("brand-layout", root.GetProperty("template_key").GetString());
        Assert.Equal("drip", root.GetProperty("send_mode").GetString());
        Assert.Equal(100, root.GetProperty("batch_size").GetInt32());
        Assert.Equal(30, root.GetProperty("batch_interval_minutes").GetInt32());
        Assert.Equal("application/pdf",
            root.GetProperty("attachments")[0].GetProperty("content_type").GetString());
        Assert.False(root.TryGetProperty("text", out _)); // null omitted
        Assert.False(root.TryGetProperty("variables", out _));
    }

    [Fact]
    public async Task ThrowsWithCodeAndComplianceDetails()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.BadRequest, """
            {
              "success": false,
              "error": "Content blocked for high-risk promotional/spam patterns",
              "code": "CONTENT_COMPLIANCE_BLOCKED",
              "compliance": { "score": 101, "severity": "high", "reasons": ["lottery"] }
            }
            """);

        var ex = await Assert.ThrowsAsync<SendhiivException>(
            () => client.Messages.SendAsync(Message()));

        Assert.Equal(400, ex.Status);
        Assert.Equal("CONTENT_COMPLIANCE_BLOCKED", ex.Code);
        Assert.Equal(new[] { "lottery" }, ex.Compliance!.Reasons);
    }

    [Fact]
    public async Task ReadsMsgFieldOnQuotaErrors()
    {
        var (client, handler) = Make(maxRetries: 0);
        handler.Enqueue((HttpStatusCode)429, """
            { "msg": "You have used all 3,000 emails included in your Free plan this month", "code": "QUOTA_EXCEEDED" }
            """);

        var ex = await Assert.ThrowsAsync<SendhiivException>(
            () => client.Messages.SendAsync(Message()));

        Assert.Equal(429, ex.Status);
        Assert.Equal("QUOTA_EXCEEDED", ex.Code);
        Assert.Contains("Free plan", ex.Message);
    }

    [Fact]
    public async Task Retries429HonoringRetryAfterThenSucceeds()
    {
        var (client, handler) = Make();
        handler
            .Enqueue((HttpStatusCode)429, """{ "success": false, "error": "Rate limit exceeded" }""", retryAfter: "0")
            .Enqueue(HttpStatusCode.Accepted, QueuedJson);

        var result = await client.Messages.SendAsync(Message());

        Assert.True(result.Success);
        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task GivesUpOn429AfterMaxRetries()
    {
        var (client, handler) = Make(maxRetries: 1);
        handler.Enqueue((HttpStatusCode)429, """{ "success": false, "error": "Rate limit exceeded" }""", retryAfter: "0");

        var ex = await Assert.ThrowsAsync<SendhiivException>(
            () => client.Messages.SendAsync(Message()));

        Assert.Equal(429, ex.Status);
        Assert.Equal(2, handler.Calls.Count); // initial + 1 retry
    }

    [Fact]
    public async Task DoesNotRetry5xx()
    {
        var (client, handler) = Make();
        handler
            .Enqueue(HttpStatusCode.InternalServerError, """{ "success": false, "error": "Internal server error" }""")
            .Enqueue(HttpStatusCode.Accepted, QueuedJson);

        var ex = await Assert.ThrowsAsync<SendhiivException>(
            () => client.Messages.SendAsync(Message()));

        Assert.Equal(500, ex.Status);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task WrapsNetworkErrorsWithStatusZero()
    {
        var handler = new ThrowingHandler();
        var client = new SendhiivClient("sh_live_test", new SendhiivClientOptions
        {
            HttpClient = new HttpClient(handler),
        });

        var ex = await Assert.ThrowsAsync<SendhiivException>(
            () => client.Messages.SendAsync(Message()));

        Assert.Equal(0, ex.Status);
        Assert.Contains("Network error", ex.Message);
    }

    private class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("connection refused");
    }
}
