using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace Sendhiiv.Tests;

public class VerifyTests
{
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

    private const string PendingJson = """
        { "success": true, "data": { "id": "vrf_6740a1b2c3d4e5f6a7b8c9d0", "to": "+2348031234567", "status": "pending",
          "attempts": 0, "attempts_remaining": 5, "expires_at": "2026-11-17T12:10:00.000Z",
          "sms": { "id": "sms_6740a1b2c3d4e5f6a7b8c9d1", "status": "queued", "error": null },
          "cost": { "amount": 750, "currency": "NGN" }, "test": false } }
        """;

    [Fact]
    public async Task SendsSnakeCaseFieldsWithAnIdempotencyKey()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.Accepted, PendingJson);

        var v = await client.Verify.SendAsync(new StartVerificationParams { To = "08031234567", AppName = "Lagos Bloom", CodeLength = 4 });

        var (request, body) = handler.Calls[0];
        Assert.Equal("https://api.sendhiiv.com/api/v1/verify", request.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Lagos Bloom", doc.RootElement.GetProperty("app_name").GetString());
        Assert.Equal(4, doc.RootElement.GetProperty("code_length").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("message", out _));
        Assert.True(request.Headers.Contains("Idempotency-Key"));

        Assert.Equal("vrf_6740a1b2c3d4e5f6a7b8c9d0", v.Id);
        Assert.Equal(5, v.AttemptsRemaining);
        Assert.Equal("queued", v.Sms!.Status);
        Assert.Equal(750, v.Cost!.Amount);
    }

    [Fact]
    public async Task ChecksAndReadsValidAndResult()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK, """
            { "success": true, "data": { "id": "vrf_6740a1b2c3d4e5f6a7b8c9d0", "status": "pending", "attempts": 1,
              "attempts_remaining": 4, "valid": false, "result": "incorrect" } }
            """);

        var check = await client.Verify.CheckAsync(new CheckVerificationParams { Id = "vrf_6740a1b2c3d4e5f6a7b8c9d0", Code = "123456" });

        using var doc = JsonDocument.Parse(handler.Calls[0].Body);
        Assert.Equal("123456", doc.RootElement.GetProperty("code").GetString());
        Assert.False(doc.RootElement.TryGetProperty("to", out _));
        Assert.False(check.Valid);
        Assert.Equal("incorrect", check.Result);
        Assert.Equal(4, check.AttemptsRemaining);
    }

    [Fact]
    public async Task NeverRetriesACheck()
    {
        var (client, handler) = Make(maxRetries: 2);
        handler
            .Enqueue(HttpStatusCode.ServiceUnavailable, """{ "success": false, "error": "down" }""")
            .Enqueue(HttpStatusCode.OK, """{ "success": true, "data": { "valid": true, "result": "approved" } }""");

        await Assert.ThrowsAsync<SendhiivException>(
            () => client.Verify.CheckAsync(new CheckVerificationParams { Id = "vrf_1", Code = "418302" }));
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task GetsAndCancels()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK, PendingJson);

        await client.Verify.GetAsync("vrf_6740a1b2c3d4e5f6a7b8c9d0");
        await client.Verify.CancelAsync("vrf_6740a1b2c3d4e5f6a7b8c9d0");

        Assert.Equal(HttpMethod.Get, handler.Calls[0].Request.Method);
        Assert.Equal(HttpMethod.Post, handler.Calls[1].Request.Method);
        Assert.Equal("https://api.sendhiiv.com/api/v1/verify/vrf_6740a1b2c3d4e5f6a7b8c9d0/cancel", handler.Calls[1].Request.RequestUri!.ToString());
    }
}
