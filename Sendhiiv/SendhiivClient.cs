using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Retries per request. Default 2. HTTP 429 is always retried; timeouts,
        /// network errors and 5xx are retried for SMS sends and reads only.
        /// </summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>Per-request timeout. Default 30 seconds.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Bring your own HttpClient (e.g. from IHttpClientFactory). The SDK never disposes it.</summary>
        public HttpClient? HttpClient { get; set; }
    }

    /// <summary>
    /// Client for the Sendhiiv API: transactional email and SMS.
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

        /// <summary>SDK version, sent in the User-Agent header.</summary>
        public const string Version = "0.5.0";

        /// <summary>Email sending operations.</summary>
        public MessagesResource Messages { get; }

        /// <summary>SMS: send, look up, list, balance.</summary>
        public SmsResource Sms { get; }

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
            Sms = new SmsResource(this);
        }

        internal async Task<SendMessageResponse> PostMessageAsync(
            SendMessageParams message, string? idempotencyKey,
            CancellationToken cancellationToken)
        {
#if NET45
            var json = Net45Json.Serialize(message);
#else
            var json = JsonSerializer.Serialize(message, JsonOptions);
#endif
            // Email sends aren't retrySafe: a 5xx or network error may come
            // after the message was accepted. Only 429 is retried.
            var result = await RequestAsync(
                HttpMethod.Post, "/messages", json, idempotencyKey, false, cancellationToken).ConfigureAwait(false);
            return ParseOrThrow(result, SafeDeserialize<SendMessageResponse>);
        }

        internal string SerializeSms(SendSmsParams sms)
        {
#if NET45
            return Net45Json.Serialize(sms);
#else
            return JsonSerializer.Serialize(sms, JsonOptions);
#endif
        }

        internal T ParseOrThrow<T>(ApiResponse result, Func<string, T?> parse) where T : class
        {
            var parsed = parse(result.Body);
            if (parsed == null)
                throw new SendhiivException(
                    "Sendhiiv API returned an unreadable success response.",
                    result.StatusCode, responseBody: result.Body);
            return parsed;
        }

        internal T? ParseData<T>(string body) where T : class
        {
#if NET45
            return Net45Json.DeserializeData<T>(body);
#else
            return SafeDeserialize<Envelope<T>>(body)?.Data;
#endif
        }

        internal SmsPage? ParsePage(string body)
        {
#if NET45
            return Net45Json.DeserializeSmsPage(body);
#else
            return SafeDeserialize<SmsPage>(body);
#endif
        }

        /// <summary>
        /// One API call. Returns the body of a 2xx response; throws
        /// SendhiivException otherwise.
        ///
        /// 429 is always retried: the limiter runs before anything is queued,
        /// so a retry can never double-send. Timeouts, network errors and 5xx
        /// are retried only when <paramref name="retrySafe"/>: reads, and SMS
        /// sends, which always carry an idempotency key.
        /// </summary>
        internal async Task<ApiResponse> RequestAsync(
            HttpMethod method, string path, string? json, string? idempotencyKey,
            bool retrySafe, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                HttpResponseMessage response;
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(_timeout);
                    var request = new HttpRequestMessage(method, _baseUrl + path);
                    if (json != null)
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                    request.Headers.UserAgent.ParseAdd("sendhiiv-dotnet/" + Version);
                    if (!string.IsNullOrEmpty(idempotencyKey))
                        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

                    try
                    {
                        response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        if (retrySafe && attempt < _maxRetries)
                        {
                            await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        throw new SendhiivException(
                            $"Request timed out after {_timeout.TotalMilliseconds:0}ms", innerException: ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        if (retrySafe && attempt < _maxRetries)
                        {
                            await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        throw new SendhiivException($"Network error: {ex.Message}", innerException: ex);
                    }
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var statusCode = (int)response.StatusCode;

                if (response.IsSuccessStatusCode) return new ApiResponse(body, statusCode);

                var retryable = statusCode == 429 || (retrySafe && statusCode >= 500);
                if (retryable && attempt < _maxRetries)
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

        private static TimeSpan Backoff(int attempt)
            => TimeSpan.FromMilliseconds(Math.Min(1000 * Math.Pow(2, attempt), 10_000));

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
            return Backoff(attempt);
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

    internal sealed class ApiResponse
    {
        internal ApiResponse(string body, int statusCode)
        {
            Body = body;
            StatusCode = statusCode;
        }

        internal string Body { get; }
        internal int StatusCode { get; }
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
            return _client.PostMessageAsync(message, null, cancellationToken);
        }

        /// <summary>
        /// Send a transactional email, safely retryable. If the request was
        /// already accepted under this <paramref name="idempotencyKey"/>, the
        /// original response is returned and nothing is sent again. Keys are
        /// remembered for 24 hours; derive one from something stable in your own
        /// system, such as an order id.
        /// </summary>
        public Task<SendMessageResponse> SendAsync(
            SendMessageParams message, string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            return _client.PostMessageAsync(message, idempotencyKey, cancellationToken);
        }
    }
}
