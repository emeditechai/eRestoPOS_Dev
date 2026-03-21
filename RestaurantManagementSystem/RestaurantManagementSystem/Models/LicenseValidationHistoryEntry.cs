namespace RestaurantManagementSystem.Models
{
    public class LicenseValidationHistoryEntry
    {
        public long Id { get; set; }

        public string ClientCode { get; set; } = string.Empty;

        public string LicenseKey { get; set; } = string.Empty;

        public bool IsValid { get; set; }

        public string? FailureReason { get; set; }

        public string? PublicIPAddress { get; set; }

        public string? DeviceInfo { get; set; }

        public string? AppUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}