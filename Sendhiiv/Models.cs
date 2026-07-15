using System.Collections.Generic;
#if !NET45
using System.Text.Json.Serialization;
#endif

namespace Sendhiiv
{
#if NET45
    // The net45 build serializes via hand-written mapping in Net45Json.cs
    // (zero package dependencies), so this attribute is a compile-time no-op
    // that lets the annotations below stay identical across all targets.
    [System.AttributeUsage(System.AttributeTargets.Property)]
    internal sealed class JsonPropertyNameAttribute : System.Attribute
    {
        public JsonPropertyNameAttribute(string name) { Name = name; }
        public string Name { get; }
    }
#endif

    /// <summary>Parameters for sending a message via POST /api/v1/messages.</summary>
    public class SendMessageParams
    {
        /// <summary>Recipient email address(es). One or many.</summary>
        [JsonPropertyName("to")]
        public List<string> To { get; set; } = new List<string>();

        /// <summary>Subject line. A message template's subject is only used when this is omitted.</summary>
        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        /// <summary>HTML body. With a layout template it is placed inside the layout.</summary>
        [JsonPropertyName("html")]
        public string? Html { get; set; }

        /// <summary>Plain-text body. Required if Html and TemplateKey are both omitted.</summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        /// <summary>Display sender, e.g. "Acme &lt;hello@yourdomain.com&gt;". Requires a verified domain; omit to use the shared sender.</summary>
        [JsonPropertyName("from")]
        public string? From { get; set; }

        /// <summary>Reply-To address.</summary>
        [JsonPropertyName("reply_to")]
        public string? ReplyTo { get; set; }

        /// <summary>Key of a saved layout or message template, e.g. "brand-layout".</summary>
        [JsonPropertyName("template_key")]
        public string? TemplateKey { get; set; }

        /// <summary>Values for merge tags such as {{firstName}}.</summary>
        [JsonPropertyName("variables")]
        public Dictionary<string, object>? Variables { get; set; }

        /// <summary>Attachments, 10 MB total per message.</summary>
        [JsonPropertyName("attachments")]
        public List<Attachment>? Attachments { get; set; }

        /// <summary>"drip" schedules recipients in batches instead of sending all immediately.</summary>
        [JsonPropertyName("send_mode")]
        public string? SendMode { get; set; }

        /// <summary>Drip only: recipients per batch (default 50, max 500).</summary>
        [JsonPropertyName("batch_size")]
        public int? BatchSize { get; set; }

        /// <summary>Drip only: minutes between batches (default 15, max 1440).</summary>
        [JsonPropertyName("batch_interval_minutes")]
        public int? BatchIntervalMinutes { get; set; }
    }

    /// <summary>A file attachment.</summary>
    public class Attachment
    {
        [JsonPropertyName("filename")]
        public string Filename { get; set; } = string.Empty;

        /// <summary>Base64-encoded file content.</summary>
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        /// <summary>MIME type, e.g. "application/pdf".</summary>
        [JsonPropertyName("content_type")]
        public string? ContentType { get; set; }
    }

    /// <summary>Successful queue confirmation (HTTP 202).</summary>
    public class SendMessageResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        /// <summary>"queued" on success.</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        /// <summary>"QUEUED_FOR_DELIVERY" on success.</summary>
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>Number of recipients queued.</summary>
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("retry")]
        public RetryInfo? Retry { get; set; }
    }

    /// <summary>Server-side delivery retry behavior.</summary>
    public class RetryInfo
    {
        [JsonPropertyName("automatic")]
        public bool Automatic { get; set; }

        [JsonPropertyName("retryable_temporary_failures")]
        public bool RetryableTemporaryFailures { get; set; }
    }

    /// <summary>Details returned when content is blocked (code CONTENT_COMPLIANCE_BLOCKED).</summary>
    public class ComplianceInfo
    {
        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("severity")]
        public string? Severity { get; set; }

        [JsonPropertyName("reasons")]
        public List<string>? Reasons { get; set; }
    }

    internal class ErrorBody
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        // Plan/quota errors (402, 403, 429 quota) use "msg" instead of "error".
        [JsonPropertyName("msg")]
        public string? Msg { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("compliance")]
        public ComplianceInfo? Compliance { get; set; }
    }
}
