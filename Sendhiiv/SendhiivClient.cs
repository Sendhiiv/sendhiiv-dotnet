using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if NET45
using System.Net;
#else
using System.Text.Json;
#endif

namespace Sendhiiv
{
    /// <summary>Options for <see cref="SendhiivClient"/>.</summary>
    public class SendhiivClientOptions
    {
        /// <summary>API base URL. Defaults to https://api.sendhiiv.com/api/v1</summary>
        public string BaseUrl { get; set; } = "https://api.sendhiiv.com/api/v1";

        /// <summary>Retries for HTTP 429 rate-limit responses only. Default 2.</summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>Per-request timeout. Default 30 seconds.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Bring your own HttpClient (e.g. from IHttpClientFactory). The SDK never disposes it.</summary>
        public HttpClient? HttpClient { get; set; }
    }

    /// <summary>
    /// Client for the Sendhiiv email API.
    /// <code>
    /// var sendhiiv = new SendhiivClient("sh_live_...");
    /// var result = await sendhiiv.Messages.SendAsync(new SendMessageParams
    /// {
    ///     From = "Acme &lt;hello@yourdomain.com&gt;",
    ///     To = { "customer@example.com" },
    ///     Subject = "Welcome aboard",
    ///     Html = "&lt;p&gt;Hi there, your account is ready.&lt;/p&gt;",
    /// });
    /// </code>
    /// </summary>
    public class SendhiivClient
    {
#if NET45
        static SendhiivClient()
        {
            // .NET Framework 4.5 negotiates TLS 1.0 by default and the API only
            // accepts TLS 1.2+, so opt in here instead of making every caller do it.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
#else
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
#endif

        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly int _maxRetries;
        private readonly TimeSpan _timeout;

        /// <summary>Message sending operations.</summary>
        public MessagesResource Messages { get; }

        public SendhiivClient(string apiKey, SendhiivClientOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Sendhiiv: missing API key.", nameof(apiKey));

            options = options ?? new SendhiivClientOptions();
            _apiKey = apiKey;
            _baseUrl = options.BaseUrl.TrimEnd('/');
            _maxRetries = options.MaxRetries;
            _timeout = options.Timeout;
            _http = options.HttpClient ?? new HttpClient();
            Messages = new MessagesResource(this);
        }

        internal async Task<SendMessageResponse> PostMessageAsync(
            SendMessageParams message, CancellationToken cancellationToken)
        {
#if NET45
            var json = Net45Json.Serialize(message);
#else
            var json = JsonSerializer.Serialize(message, JsonOptions);
#endif

            // Only 429 (rate limit) is retried: the limiter runs before anything
            // is queued, so a retry can never double-send. 5xx and network errors
            // are NOT retried — the message may already have been accepted.
            for (var attempt = 0; ; attempt++)
            {
                HttpResponseMessage response;
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(_timeout);
                    var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/messages")
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json"),
                    };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                    request.Headers.UserAgent.ParseAdd("sendhiiv-dotnet/0.3.1");

                    try
                    {
                        response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new SendhiivException(
                            $"Request timed out after {_timeout.TotalMilliseconds:0}ms", innerException: ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new SendhiivException($"Network error: {ex.Message}", innerException: ex);
                    }
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var statusCode = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    var parsed = SafeDeserialize<SendMessageResponse>(body);
                    if (parsed == null)
                        throw new SendhiivException(
                            "Sendhiiv API returned an unreadable success response.",
                            statusCode, responseBody: body);
                    return parsed;
                }

                if (statusCode == 429 && attempt < _maxRetries)
                {
                    await Task.Delay(RetryDelay(response, attempt), cancellationToken).ConfigureAwait(false);
                    response.Dispose();
                    continue;
                }

                var error = SafeDeserialize<ErrorBody>(body);
                throw new SendhiivException(
                    error?.Error ?? error?.Msg ?? $"Sendhiiv API error (HTTP {statusCode})",
                    statusCode,
                    error?.Code,
                    error?.Compliance,
                    body);
            }
        }

        private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
                return delta;
            if (retryAfter?.Date is DateTimeOffset date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) return wait;
            }
            var backoffMs = Math.Min(1000 * Math.Pow(2, attempt), 10_000);
            return TimeSpan.FromMilliseconds(backoffMs);
        }

        private static T? SafeDeserialize<T>(string body) where T : class
        {
#if NET45
            return Net45Json.Deserialize<T>(body);
#else
            try
            {
                return JsonSerializer.Deserialize<T>(body, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
#endif
        }
    }

    /// <summary>Message sending operations, accessed via <see cref="SendhiivClient.Messages"/>.</summary>
    public class MessagesResource
    {
        private readonly SendhiivClient _client;

        internal MessagesResource(SendhiivClient client) => _client = client;

        /// <summary>
        /// Send a transactional email. Returns the queue confirmation (HTTP 202)
        /// or throws <see cref="SendhiivException"/>.
        /// </summary>
        public Task<SendMessageResponse> SendAsync(
            SendMessageParams message, CancellationToken cancellationToken = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            return _client.PostMessageAsync(message, cancellationToken);
        }
    }
}
