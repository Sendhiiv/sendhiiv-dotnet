using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace Sendhiiv.Tests;

public class SmsTests
{
    private const string BatchJson = """
        {
          "success": true,
          "data": {
            "object": "sms_batch",
            "batch_id": "smsb_1",
            "type": "transactional",
            "from": null,
            "reference": "order_4521",
            "messages": [ { "id": "sms_6740a1b2c3d4e5f6a7b8c9d0", "to": "+2348031234567", "status": "queued", "pages": 1 } ],
            "rejected": [ { "to": "123", "reason": "Not a valid mobile number." } ],
            "recipients": 1,
            "pages_per_message": 1,
            "encoding": "GSM-7",
            "cost": { "amount": 750, "currency": "NGN" },
            "balance": { "amount": 4267250, "currency": "NGN" },
            "test": false
          }
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

    private static SendSmsParams Sms() => new()
    {
        To = { "08031234567", "123" },
        Message = "Your code is 418302",
        Reference = "order_4521",
    };

    private static string Header(HttpRequestMessage request, string name)
        => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : "";

    [Fact]
    public async Task SendsAndParsesTheBatch()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.Accepted, BatchJson);

        var batch = await client.Sms.SendAsync(Sms());

        var (request, body) = handler.Calls[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.sendhiiv.com/api/v1/sms/messages", request.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Your code is 418302", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("order_4521", doc.RootElement.GetProperty("reference").GetString());
        Assert.False(doc.RootElement.TryGetProperty("from", out _), "nulls are left out");

        Assert.Equal("smsb_1", batch.BatchId);
        Assert.Equal("sms_6740a1b2c3d4e5f6a7b8c9d0", batch.Messages[0].Id);
        Assert.Equal("123", batch.Rejected[0].To);
        Assert.Equal(750, batch.Cost!.Amount);
        Assert.Equal(7.50m, batch.Cost.Major);
        Assert.Equal("NGN", batch.Balance!.Currency);
    }

    [Fact]
    public async Task GeneratesAnIdempotencyKeyUnlessGiven()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.Accepted, BatchJson);

        await client.Sms.SendAsync(Sms());
        Assert.StartsWith("sdk_", Header(handler.Calls[0].Request, "Idempotency-Key"));

        await client.Sms.SendAsync(Sms(), "order-4521");
        Assert.Equal("order-4521", Header(handler.Calls[1].Request, "Idempotency-Key"));
    }

    [Fact]
    public async Task RetriesAServerErrorWithTheSameKey()
    {
        var (client, handler) = Make(maxRetries: 1);
        handler
            .Enqueue(HttpStatusCode.ServiceUnavailable, """{ "success": false, "code": "SMS_UNAVAILABLE", "error": "x" }""")
            .Enqueue(HttpStatusCode.Accepted, BatchJson);

        var batch = await client.Sms.SendAsync(Sms());

        Assert.Equal("smsb_1", batch.BatchId);
        Assert.Equal(2, handler.Calls.Count);
        Assert.Equal(Header(handler.Calls[0].Request, "Idempotency-Key"), Header(handler.Calls[1].Request, "Idempotency-Key"));
    }

    [Fact]
    public async Task ALowBalanceThrowsWithItsCode()
    {
        var (client, handler) = Make();
        handler.Enqueue((HttpStatusCode)402, """{ "success": false, "code": "SMS_INSUFFICIENT_BALANCE", "error": "Your SMS balance is too low for this send.", "short_by": { "amount": 650, "currency": "NGN" } }""");

        var ex = await Assert.ThrowsAsync<SendhiivException>(() => client.Sms.SendAsync(Sms()));

        Assert.Equal(402, ex.Status);
        Assert.Equal("SMS_INSUFFICIENT_BALANCE", ex.Code);
        Assert.Contains("short_by", ex.ResponseBody);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task GetsListsAndReadsTheBalance()
    {
        var (client, handler) = Make();
        handler
            .Enqueue(HttpStatusCode.OK, """{ "success": true, "data": { "id": "sms_6740a1b2c3d4e5f6a7b8c9d0", "status": "failed", "error": "The network couldn't deliver it.", "cost": { "amount": 0, "currency": "NGN" } } }""")
            .Enqueue(HttpStatusCode.OK, """{ "success": true, "data": [ { "id": "sms_a", "status": "delivered" } ], "has_more": true, "next_cursor": "sms_a" }""")
            .Enqueue(HttpStatusCode.OK, """{ "success": true, "data": { "balance": { "amount": 4268000, "currency": "NGN" }, "free_sending": false, "tier": { "name": "Starter", "price_per_page": { "transactional": { "amount": 750, "currency": "NGN" }, "promotional": { "amount": 700, "currency": "NGN" } } }, "daily_limit": { "pages": 2000, "used_pages": 3, "remaining_pages": 1997 } } }""");

        var sms = await client.Sms.GetAsync("sms_6740a1b2c3d4e5f6a7b8c9d0");
        var page = await client.Sms.ListAsync(new SmsListParams
        {
            Status = "delivered",
            BatchId = "smsb_1",
            CreatedAfter = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            Limit = 50,
        });
        var balance = await client.Sms.GetBalanceAsync();

        Assert.Equal("failed", sms.Status);
        Assert.Equal("The network couldn't deliver it.", sms.Error);
        Assert.Equal("https://api.sendhiiv.com/api/v1/sms/messages?status=delivered&batch_id=smsb_1&created_after=2026-11-01T00%3A00%3A00Z&limit=50",
            handler.Calls[1].Request.RequestUri!.ToString());
        Assert.True(page.HasMore);
        Assert.Equal("sms_a", page.NextCursor);
        Assert.Equal("delivered", page.Data[0].Status);
        Assert.Equal(4268000, balance.Balance!.Amount);
        Assert.Equal(750, balance.Tier!.PricePerPage!.Transactional!.Amount);
        Assert.Equal(1997, balance.DailyLimit!.RemainingPages);
    }
}
