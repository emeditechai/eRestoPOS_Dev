namespace RestaurantManagementSystem.Models
{
    public enum LicenseGateStatus
    {
        Valid = 0,
        Unregistered = 1,
        PendingActivation = 2,
        Inactive = 3,
        Expired = 4,
        HardwareMismatch = 5,
        RemoteUnavailable = 6,
        RemoteNotFound = 7,
        ConfigurationMissing = 8,
        UnknownError = 9,
        DataMismatch = 10
    }

    public class LicenseGateResult
    {
        public LicenseGateStatus Status { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string? FailureReason { get; set; }

        public string? ClientCode { get; set; }

        public string? LicenseKey { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public DateTime EvaluatedAt { get; set; } = DateTime.Now;

        public bool IsAllowed => Status == LicenseGateStatus.Valid;
    }
}