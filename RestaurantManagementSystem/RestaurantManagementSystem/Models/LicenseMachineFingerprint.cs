namespace RestaurantManagementSystem.Models
{
    public class LicenseMachineFingerprint
    {
        public string ServerMacID { get; set; } = string.Empty;

        public string HardDiskNumber { get; set; } = string.Empty;

        public string MotherboardNumber { get; set; } = string.Empty;

        public DateTime CapturedAt { get; set; } = DateTime.Now;
    }
}