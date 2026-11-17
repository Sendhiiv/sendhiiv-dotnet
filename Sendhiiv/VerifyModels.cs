using System.Collections.Generic;
#if !NET45
using System.Text.Json.Serialization;
#endif

namespace Sendhiiv
{
    /// <summary>Parameters for POST /api/v1/verify.</summary>
    public class StartVerificationParams
    {
        /// <summary>The phone number to text the code to.</summary>
        [JsonPropertyName("to")]
        public string To { get; set; } = string.Empty;

        /// <summary>Shown in the SMS: "418302 is your Lagos Bloom verification code…". Up to 30 characters.</summary>
        [JsonPropertyName("app_name")]
        public string? AppName { get; set; }

        /// <summary>4–8 digits. Default 6.</summary>
        [JsonPropertyName("code_length")]
        public int? CodeLength { get; set; }

        /// <summary>1–30. Default 10.</summary>
        [JsonPropertyName("expires_in_minutes")]
        public int? ExpiresInMinutes { get; set; }

        /// <summary>Your own text. Must include {code}; may use {minutes} and {app_name}.</summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>Parameters for POST /api/v1/verify/check. Set Id, or To.</summary>
    public class CheckVerificationParams
    {
        /// <summary>The vrf_… id from SendAsync.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>Check the latest code sent to this number instead of by id.</summary>
        [JsonPropertyName("to")]
        public string? To { get; set; }

        /// <summary>What the user typed.</summary>
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
    }

    /// <summary>One verification code and where it stands.</summary>
    public class Verification
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        /// <summary>"pending", "approved", "expired", "max_attempts" or "canceled".</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("attempts")]
        public int Attempts { get; set; }

        [JsonPropertyName("attempts_remaining")]
        public int AttemptsRemaining { get; set; }

        [JsonPropertyName("expires_at")]
        public string? ExpiresAt { get; set; }

        [JsonPropertyName("approved_at")]
        public string? ApprovedAt { get; set; }

        /// <summary>The SMS that carried the code; null in test mode.</summary>
        [JsonPropertyName("sms")]
        public VerificationSms? Sms { get; set; }

        [JsonPropertyName("cost")]
        public Money? Cost { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("test")]
        public bool Test { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }
    }

    public class VerificationSms
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    /// <summary>The outcome of checking a code.</summary>
    public class VerificationCheck : Verification
    {
        /// <summary>True only the first time the right code is given.</summary>
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        /// <summary>"approved", "incorrect", "expired", "max_attempts", "canceled" or "already_approved".</summary>
        [JsonPropertyName("result")]
        public string? Result { get; set; }
    }
}
