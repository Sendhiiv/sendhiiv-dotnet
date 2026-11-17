using System.Collections.Generic;
#if !NET45
using System.Text.Json.Serialization;
#endif

namespace Sendhiiv
{
    /// <summary>An amount in the currency's minor unit (kobo for NGN: 750 = ₦7.50).</summary>
    public class Money
    {
        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        /// <summary>The amount in major units, e.g. 7.50 for 750 kobo.</summary>
        public decimal Major => Amount / 100m;
    }

    /// <summary>Parameters for POST /api/v1/sms/messages.</summary>
    public class SendSmsParams
    {
        /// <summary>One number or up to 1,000. 08031234567 and +2348031234567 both work.</summary>
        [JsonPropertyName("to")]
        public List<string> To { get; set; } = new List<string>();

        /// <summary>Up to 6 pages. A page is 160 characters, or 70 with emoji or ₦.</summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>"transactional" (default; reaches DND numbers) or "promotional".</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>An approved sender ID on your account. Omit for the default.</summary>
        [JsonPropertyName("from")]
        public string? From { get; set; }

        /// <summary>Your own id, up to 100 characters. Returned with the message and filterable.</summary>
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        /// <summary>Up to 20 keys / 2 KB of your own data, returned with the message.</summary>
        [JsonPropertyName("metadata")]
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>The result of an SMS send.</summary>
    public class SmsBatch
    {
        [JsonPropertyName("batch_id")]
        public string? BatchId { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        /// <summary>One entry per number accepted. Store each Id.</summary>
        [JsonPropertyName("messages")]
        public List<SmsAccepted> Messages { get; set; } = new List<SmsAccepted>();

        /// <summary>Numbers that couldn't be used. Not sent, not charged.</summary>
        [JsonPropertyName("rejected")]
        public List<SmsRejected> Rejected { get; set; } = new List<SmsRejected>();

        [JsonPropertyName("recipients")]
        public int Recipients { get; set; }

        [JsonPropertyName("pages_per_message")]
        public int PagesPerMessage { get; set; }

        [JsonPropertyName("encoding")]
        public string? Encoding { get; set; }

        [JsonPropertyName("cost")]
        public Money? Cost { get; set; }

        /// <summary>Balance after this send; null for accounts that send free.</summary>
        [JsonPropertyName("balance")]
        public Money? Balance { get; set; }

        [JsonPropertyName("test")]
        public bool Test { get; set; }
    }

    /// <summary>One number an SMS send accepted.</summary>
    public class SmsAccepted
    {
        /// <summary>SMS id, e.g. "sms_6740a1b2c3d4e5f6a7b8c9d0".</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("pages")]
        public int Pages { get; set; }
    }

    /// <summary>A number in "to" that couldn't be used, and why.</summary>
    public class SmsRejected
    {
        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    /// <summary>One SMS and its current status.</summary>
    public class Sms
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("batch_id")]
        public string? BatchId { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>"queued", "sent", "delivered", "failed" or "rejected". New statuses may be added; treat unknown ones as pending.</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("pages")]
        public int Pages { get; set; }

        [JsonPropertyName("cost")]
        public Money? Cost { get; set; }

        /// <summary>Why it failed or was rejected.</summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("test")]
        public bool Test { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("sent_at")]
        public string? SentAt { get; set; }

        [JsonPropertyName("delivered_at")]
        public string? DeliveredAt { get; set; }

        [JsonPropertyName("failed_at")]
        public string? FailedAt { get; set; }
    }

    /// <summary>Filters for listing SMS. All optional.</summary>
    public class SmsListParams
    {
        public string? Status { get; set; }
        public string? Reference { get; set; }
        public string? BatchId { get; set; }
        public string? To { get; set; }
        public System.DateTimeOffset? CreatedAfter { get; set; }
        public System.DateTimeOffset? CreatedBefore { get; set; }
        /// <summary>1–100, default 20.</summary>
        public int? Limit { get; set; }
        /// <summary>NextCursor from the previous page.</summary>
        public string? Cursor { get; set; }
    }

    /// <summary>One page of SMS, newest first.</summary>
    public class SmsPage
    {
        [JsonPropertyName("data")]
        public List<Sms> Data { get; set; } = new List<Sms>();

        [JsonPropertyName("has_more")]
        public bool HasMore { get; set; }

        /// <summary>Pass as Cursor to get the next page; null on the last page.</summary>
        [JsonPropertyName("next_cursor")]
        public string? NextCursor { get; set; }
    }

    /// <summary>SMS balance, prices on your tier, and today's daily limit.</summary>
    public class SmsBalance
    {
        /// <summary>Null for accounts that send free.</summary>
        [JsonPropertyName("balance")]
        public Money? Balance { get; set; }

        [JsonPropertyName("free_sending")]
        public bool FreeSending { get; set; }

        [JsonPropertyName("tier")]
        public SmsTier? Tier { get; set; }

        [JsonPropertyName("daily_limit")]
        public SmsDailyLimit? DailyLimit { get; set; }
    }

    public class SmsTier
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("price_per_page")]
        public SmsPrices? PricePerPage { get; set; }
    }

    public class SmsPrices
    {
        [JsonPropertyName("transactional")]
        public Money? Transactional { get; set; }

        [JsonPropertyName("promotional")]
        public Money? Promotional { get; set; }
    }

    public class SmsDailyLimit
    {
        [JsonPropertyName("pages")]
        public int Pages { get; set; }

        [JsonPropertyName("used_pages")]
        public int UsedPages { get; set; }

        [JsonPropertyName("remaining_pages")]
        public int RemainingPages { get; set; }
    }

    // { "success": true, "data": … }
    internal class Envelope<T> where T : class
    {
        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }
}
