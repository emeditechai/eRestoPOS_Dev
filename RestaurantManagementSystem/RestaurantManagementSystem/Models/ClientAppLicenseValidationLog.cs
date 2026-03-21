namespace RestaurantManagementSystem.Models
{
    public class ClientAppLicenseValidationLog
    {
        public long Id { get; set; }

        public string? ClientCode { get; set; }

        public string? LicenseKey { get; set; }

        public DateTime ValidatedAt { get; set; }

        public string? ServerMacID { get; set; }

        public string? HardDiskNumber { get; set; }

        public string? MotherboardNumber { get; set; }

        public bool IsMatch { get; set; }

        public bool IsExpired { get; set; }

        public bool IsRemoteReachable { get; set; }

        public string Result { get; set; } = string.Empty;

        public string? FailureReason { get; set; }

        public string? RequestIp { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}