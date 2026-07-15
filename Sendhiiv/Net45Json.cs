#if NET45
using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Sendhiiv
{
    /// <summary>
    /// JSON layer for the net45 build. Uses the framework's built-in
    /// JavaScriptSerializer plus hand-written field mapping so the package has
    /// no dependencies on .NET Framework — installing it can never add,
    /// upgrade, or conflict with Newtonsoft.Json (or anything else) in the
    /// host application. The wire shapes are small and fixed; the test suite
    /// runs these mappings on the classic CLR on every change.
    /// </summary>
    internal static class Net45Json
    {
        internal static string Serialize(SendMessageParams m)
        {
            var payload = new Dictionary<string, object>();
            payload["to"] = m.To;
            if (m.Subject != null) payload["subject"] = m.Subject;
            if (m.Html != null) payload["html"] = m.Html;
            if (m.Text != null) payload["text"] = m.Text;
            if (m.From != null) payload["from"] = m.From;
            if (m.ReplyTo != null) payload["reply_to"] = m.ReplyTo;
            if (m.TemplateKey != null) payload["template_key"] = m.TemplateKey;
            if (m.Variables != null) payload["variables"] = m.Variables;
            if (m.Attachments != null)
            {
                var attachments = new List<Dictionary<string, object>>();
                foreach (var attachment in m.Attachments)
                {
                    var entry = new Dictionary<string, object>
                    {
                        { "filename", attachment.Filename },
                        { "content", attachment.Content },
                    };
                    if (attachment.ContentType != null) entry["content_type"] = attachment.ContentType;
                    attachments.Add(entry);
                }
                payload["attachments"] = attachments;
            }
            if (m.SendMode != null) payload["send_mode"] = m.SendMode;
            if (m.BatchSize.HasValue) payload["batch_size"] = m.BatchSize.Value;
            if (m.BatchIntervalMinutes.HasValue) payload["batch_interval_minutes"] = m.BatchIntervalMinutes.Value;

            return NewSerializer().Serialize(payload);
        }

        /// <summary>Returns null (never throws) when the body isn't usable JSON.</summary>
        internal static T? Deserialize<T>(string body) where T : class
        {
            Dictionary<string, object>? parsed;
            try
            {
                parsed = NewSerializer().Deserialize<Dictionary<string, object>>(body);
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            if (parsed == null) return null;

            if (typeof(T) == typeof(SendMessageResponse)) return (T)(object)MapResponse(parsed);
            if (typeof(T) == typeof(ErrorBody)) return (T)(object)MapError(parsed);
            return null;
        }

        private static JavaScriptSerializer NewSerializer()
            // Default MaxJsonLength is 2 MB, which a large attachment payload exceeds.
            => new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private static SendMessageResponse MapResponse(Dictionary<string, object> d)
        {
            var response = new SendMessageResponse
            {
                Success = GetBool(d, "success"),
                Status = GetString(d, "status"),
                Code = GetString(d, "code"),
                Message = GetString(d, "message"),
                Total = GetInt(d, "total"),
            };
            if (Get(d, "retry") is Dictionary<string, object> retry)
            {
                response.Retry = new RetryInfo
                {
                    Automatic = GetBool(retry, "automatic"),
                    RetryableTemporaryFailures = GetBool(retry, "retryable_temporary_failures"),
                };
            }
            return response;
        }

        private static ErrorBody MapError(Dictionary<string, object> d)
        {
            var error = new ErrorBody
            {
                Error = GetString(d, "error"),
                Msg = GetString(d, "msg"),
                Code = GetString(d, "code"),
            };
            if (Get(d, "compliance") is Dictionary<string, object> compliance)
            {
                error.Compliance = new ComplianceInfo
                {
                    Score = GetDouble(compliance, "score"),
                    Severity = GetString(compliance, "severity"),
                };
                if (Get(compliance, "reasons") is IEnumerable reasons && !(reasons is string))
                {
                    var list = new List<string>();
                    foreach (var reason in reasons)
                    {
                        if (reason != null) list.Add(reason.ToString());
                    }
                    error.Compliance.Reasons = list;
                }
            }
            return error;
        }

        private static object? Get(Dictionary<string, object> d, string key)
        {
            object value;
            return d.TryGetValue(key, out value) ? value : null;
        }

        private static string? GetString(Dictionary<string, object> d, string key)
        {
            var value = Get(d, key);
            return value == null ? null : value.ToString();
        }

        private static bool GetBool(Dictionary<string, object> d, string key)
            => Get(d, key) is bool value && value;

        private static int GetInt(Dictionary<string, object> d, string key)
        {
            var value = Get(d, key);
            if (!(value is IConvertible)) return 0;
            try { return Convert.ToInt32(value); }
            catch (FormatException) { return 0; }
            catch (OverflowException) { return 0; }
            catch (InvalidCastException) { return 0; }
        }

        private static double GetDouble(Dictionary<string, object> d, string key)
        {
            var value = Get(d, key);
            if (!(value is IConvertible)) return 0;
            try { return Convert.ToDouble(value); }
            catch (FormatException) { return 0; }
            catch (OverflowException) { return 0; }
            catch (InvalidCastException) { return 0; }
        }
    }
}
#endif
