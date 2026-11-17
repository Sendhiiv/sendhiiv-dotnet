using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Sendhiiv
{
    /// <summary>
    /// SMS operations, accessed via <see cref="SendhiivClient.Sms"/>.
    /// <code>
    /// var batch = await sendhiiv.Sms.SendAsync(new SendSmsParams
    /// {
    ///     To = { "08031234567" },
    ///     Message = "Your code is 418302",
    /// });
    /// </code>
    /// </summary>
    public class SmsResource
    {
        private readonly SendhiivClient _client;

        internal SmsResource(SendhiivClient client) => _client = client;

        /// <summary>
        /// Send one SMS to 1–1,000 numbers, charged from your SMS balance.
        /// Numbers that can't be used come back in <see cref="SmsBatch.Rejected"/>, uncharged.
        ///
        /// Every send carries an idempotency key (one is generated when
        /// <paramref name="idempotencyKey"/> is null), so the SDK retries
        /// timeouts and server errors without texting or charging anyone
        /// twice. Pass your own, such as an order id, to make retries across
        /// restarts safe too.
        /// </summary>
        public async Task<SmsBatch> SendAsync(
            SendSmsParams sms, string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            if (sms == null) throw new ArgumentNullException(nameof(sms));
            var key = string.IsNullOrEmpty(idempotencyKey) ? "sdk_" + Guid.NewGuid().ToString("D") : idempotencyKey;
            var result = await _client.RequestAsync(
                HttpMethod.Post, "/sms/messages", _client.SerializeSms(sms), key, true, cancellationToken).ConfigureAwait(false);
            return _client.ParseOrThrow(result, _client.ParseData<SmsBatch>);
        }

        /// <summary>One SMS by the "sms_…" id returned when it was sent.</summary>
        public async Task<Sms> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("SMS id is required.", nameof(id));
            var result = await _client.RequestAsync(
                HttpMethod.Get, "/sms/messages/" + Uri.EscapeDataString(id), null, null, true, cancellationToken).ConfigureAwait(false);
            return _client.ParseOrThrow(result, _client.ParseData<Sms>);
        }

        /// <summary>One page of SMS, newest first. Page with <see cref="SmsPage.NextCursor"/>.</summary>
        public async Task<SmsPage> ListAsync(SmsListParams? filters = null, CancellationToken cancellationToken = default)
        {
            var result = await _client.RequestAsync(
                HttpMethod.Get, "/sms/messages" + QueryString(filters), null, null, true, cancellationToken).ConfigureAwait(false);
            return _client.ParseOrThrow(result, _client.ParsePage);
        }

        /// <summary>Balance, price per page on your tier, and today's daily limit.</summary>
        public async Task<SmsBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
        {
            var result = await _client.RequestAsync(
                HttpMethod.Get, "/sms/balance", null, null, true, cancellationToken).ConfigureAwait(false);
            return _client.ParseOrThrow(result, _client.ParseData<SmsBalance>);
        }

        internal static string QueryString(SmsListParams? f)
        {
            if (f == null) return string.Empty;
            var parts = new List<string>();
            void Add(string name, string? value)
            {
                if (!string.IsNullOrEmpty(value)) parts.Add(name + "=" + Uri.EscapeDataString(value!));
            }
            Add("status", f.Status);
            Add("reference", f.Reference);
            Add("batch_id", f.BatchId);
            Add("to", f.To);
            Add("created_after", f.CreatedAfter?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            Add("created_before", f.CreatedBefore?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            Add("limit", f.Limit?.ToString(CultureInfo.InvariantCulture));
            Add("cursor", f.Cursor);
            return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
        }
    }
}
