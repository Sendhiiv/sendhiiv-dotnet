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

        internal static string Serialize(SendSmsParams m)
        {
            var payload = new Dictionary<string, object>();
            payload["to"] = m.To;
            payload["message"] = m.Message;
            if (m.Type != null) payload["type"] = m.Type;
            if (m.From != null) payload["from"] = m.From;
            if (m.Reference != null) payload["reference"] = m.Reference;
            if (m.Metadata != null) payload["metadata"] = m.Metadata;
            return NewSerializer().Serialize(payload);
        }

        private static Dictionary<string, object>? Parse(string body)
        {
            try { return NewSerializer().Deserialize<Dictionary<string, object>>(body); }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        /// <summary>The "data" object of { success, data }, mapped to T. Null when unusable.</summary>
        internal static T? DeserializeData<T>(string body) where T : class
        {
            var parsed = Parse(body);
            if (parsed == null || !(Get(parsed, "data") is Dictionary<string, object> data)) return null;
            if (typeof(T) == typeof(SmsBatch)) return (T)(object)MapSmsBatch(data);
            if (typeof(T) == typeof(Sms)) return (T)(object)MapSms(data);
            if (typeof(T) == typeof(SmsBalance)) return (T)(object)MapSmsBalance(data);
            return null;
        }

        internal static SmsPage? DeserializeSmsPage(string body)
        {
            var parsed = Parse(body);
            if (parsed == null) return null;
            var page = new SmsPage { HasMore = GetBool(parsed, "has_more"), NextCursor = GetString(parsed, "next_cursor") };
            foreach (var item in Items(parsed, "data")) page.Data.Add(MapSms(item));
            return page;
        }

        private static IEnumerable<Dictionary<string, object>> Items(Dictionary<string, object> d, string key)
        {
            if (Get(d, key) is IEnumerable list && !(list is string))
                foreach (var item in list)
                    if (item is Dictionary<string, object> entry) yield return entry;
        }

        private static Money? MapMoney(object? value)
        {
            if (!(value is Dictionary<string, object> d)) return null;
            return new Money { Amount = GetLong(d, "amount"), Currency = GetString(d, "currency") };
        }

        private static SmsBatch MapSmsBatch(Dictionary<string, object> d)
        {
            var batch = new SmsBatch
            {
                BatchId = GetString(d, "batch_id"),
                Type = GetString(d, "type"),
                From = GetString(d, "from"),
                Reference = GetString(d, "reference"),
                Recipients = GetInt(d, "recipients"),
                PagesPerMessage = GetInt(d, "pages_per_message"),
                Encoding = GetString(d, "encoding"),
                Cost = MapMoney(Get(d, "cost")),
                Balance = MapMoney(Get(d, "balance")),
                Test = GetBool(d, "test"),
            };
            foreach (var m in Items(d, "messages"))
                batch.Messages.Add(new SmsAccepted { Id = GetString(m, "id"), To = GetString(m, "to"), Status = GetString(m, "status"), Pages = GetInt(m, "pages") });
            foreach (var r in Items(d, "rejected"))
                batch.Rejected.Add(new SmsRejected { To = GetString(r, "to"), Reason = GetString(r, "reason") });
            return batch;
        }

        private static Sms MapSms(Dictionary<string, object> d) => new Sms
        {
            Id = GetString(d, "id"),
            BatchId = GetString(d, "batch_id"),
            To = GetString(d, "to"),
            From = GetString(d, "from"),
            Message = GetString(d, "message"),
            Type = GetString(d, "type"),
            Status = GetString(d, "status"),
            Pages = GetInt(d, "pages"),
            Cost = MapMoney(Get(d, "cost")),
            Error = GetString(d, "error"),
            Reference = GetString(d, "reference"),
            Test = GetBool(d, "test"),
            CreatedAt = GetString(d, "created_at"),
            SentAt = GetString(d, "sent_at"),
            DeliveredAt = GetString(d, "delivered_at"),
            FailedAt = GetString(d, "failed_at"),
        };

        private static SmsBalance MapSmsBalance(Dictionary<string, object> d)
        {
            var balance = new SmsBalance { Balance = MapMoney(Get(d, "balance")), FreeSending = GetBool(d, "free_sending") };
            if (Get(d, "tier") is Dictionary<string, object> tier)
            {
                balance.Tier = new SmsTier { Name = GetString(tier, "name") };
                if (Get(tier, "price_per_page") is Dictionary<string, object> prices)
                    balance.Tier.PricePerPage = new SmsPrices
                    {
                        Transactional = MapMoney(Get(prices, "transactional")),
                        Promotional = MapMoney(Get(prices, "promotional")),
                    };
            }
            if (Get(d, "daily_limit") is Dictionary<string, object> limit)
                balance.DailyLimit = new SmsDailyLimit
                {
                    Pages = GetInt(limit, "pages"),
                    UsedPages = GetInt(limit, "used_pages"),
                    RemainingPages = GetInt(limit, "remaining_pages"),
                };
            return balance;
        }

        private static long GetLong(Dictionary<string, object> d, string key)
        {
            var value = Get(d, key);
            if (!(value is IConvertible)) return 0;
            try { return Convert.ToInt64(value); }
            catch (FormatException) { return 0; }
            catch (OverflowException) { return 0; }
            catch (InvalidCastException) { return 0; }
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
                Mode = GetString(d, "mode"),
            };
            if (Get(d, "retry") is Dictionary<string, object> retry)
            {
                response.Retry = new RetryInfo
                {
                    Automatic = GetBool(retry, "automatic"),
                    RetryableTemporaryFailures = GetBool(retry, "retryable_temporary_failures"),
                };
            }
            if (Get(d, "data") is IEnumerable accepted && !(accepted is string))
            {
                var messages = new List<AcceptedMessage>();
                foreach (var item in accepted)
                {
                    if (item is Dictionary<string, object> entry)
                    {
                        messages.Add(new AcceptedMessage
                        {
                            Id = GetString(entry, "id"),
                            To = GetString(entry, "to"),
                            Status = GetString(entry, "status"),
                        });
                    }
                }
                response.Data = messages;
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
