using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Sendhiiv
{
    /// <summary>
    /// One-time codes by SMS, accessed via <see cref="SendhiivClient.Verify"/>.
    /// Sendhiiv creates, texts, expires and checks the code.
    /// <code>
    /// var v = await sendhiiv.Verify.SendAsync(new StartVerificationParams { To = "08031234567", AppName = "Lagos Bloom" });
    /// var check = await sendhiiv.Verify.CheckAsync(new CheckVerificationParams { Id = v.Id, Code = typedCode });
    /// if (check.Valid) { /* signed in */ }
    /// </code>
    /// </summary>
    public class VerifyResource
    {
        private readonly SendhiivClient _client;

        internal VerifyResource(SendhiivClient client) => _client = client;

        /// <summary>
        /// Text a one-time code. Keep the returned Id, or check by number
        /// later. Carries an idempotency key (generated when null), so a
        /// retry never sends a second code.
        /// </summary>
        public async Task<Verification> SendAsync(
            StartVerificationParams verification, string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            if (verification == null) throw new ArgumentNullException(nameof(verification));
            var key = string.IsNullOrEmpty(idempotencyKey) ? "sdk_" + Guid.NewGuid().ToString("D") : idempotencyKey;
            var result = await _client.RequestAsync(
                HttpMethod.Post, "/verify", _client.SerializeBody(verification), key, true, cancellationToken).ConfigureAwait(false);
            return _client.ParseOrThrow(result, _client.ParseData<Verification>);
        }

        /// <summary>
        /// Is this the code the user typed? Never retried automatically: a
        /// retried check would use up one of the user's attempts.
        /// </summary>
        public async Task<VerificationCheck> CheckAsync(
            CheckVerificationParams check, CancellationToken cancellationToken = default)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));
            var result = await _client.RequestAsync(
                HttpMethod.Post, "/verify/check", _client.SerializeBody(check), null, false, cancellationToken).ConfigureAwait(false);
            return _client.ParseOrThrow(result, _client.ParseData<VerificationCheck>);
        }

        /// <summary>Where a verification stands, by its vrf_… id.</summary>
        public async Task<Verification> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Verification id is required.", nameof(id));
            var result = await _client.RequestAsync(
                HttpMethod.Get, "/verify/" + Uri.EscapeDataString(id), null, null, true, cancellationToken).ConfigureAwait(false);
            return _client.ParseOrThrow(result, _client.ParseData<Verification>);
        }

        /// <summary>Stop a pending code being used.</summary>
        public async Task<Verification> CancelAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Verification id is required.", nameof(id));
            var result = await _client.RequestAsync(
                HttpMethod.Post, "/verify/" + Uri.EscapeDataString(id) + "/cancel", null, null, true, cancellationToken).ConfigureAwait(false);
            return _client.ParseOrThrow(result, _client.ParseData<Verification>);
        }
    }
}
