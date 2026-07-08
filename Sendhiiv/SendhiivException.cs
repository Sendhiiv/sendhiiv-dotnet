using System;

namespace Sendhiiv
{
    /// <summary>Thrown for any non-2xx API response, network failure, or timeout.</summary>
    public class SendhiivException : Exception
    {
        /// <summary>HTTP status code, e.g. 401, 413, 429. 0 for network/timeout errors.</summary>
        public int Status { get; }

        /// <summary>Machine-readable code, e.g. "CONTENT_COMPLIANCE_BLOCKED", "QUOTA_EXCEEDED". Null when none applies.</summary>
        public string? Code { get; }

        /// <summary>Compliance details when Code is CONTENT_COMPLIANCE_BLOCKED.</summary>
        public ComplianceInfo? Compliance { get; }

        /// <summary>Raw response body, when one was received.</summary>
        public string? ResponseBody { get; }

        public SendhiivException(
            string message,
            int status = 0,
            string? code = null,
            ComplianceInfo? compliance = null,
            string? responseBody = null,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Status = status;
            Code = code;
            Compliance = compliance;
            ResponseBody = responseBody;
        }
    }
}
