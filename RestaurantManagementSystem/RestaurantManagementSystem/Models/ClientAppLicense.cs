namespace RestaurantManagementSystem.Models
{
    public class ClientAppLicense
    {
        public long Id { get; set; }

        [Required]
        [StringLength(32)]
        public string ClientCode { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string ClientName { get; set; } = string.Empty;

        [StringLength(30)]
        public string? ContactNumber { get; set; }

        [StringLength(200)]
        public string? EmailID { get; set; }

        [Required]
        [StringLength(100)]
        public string LicenseKey { get; set; } = string.Empty;

        [Required]
        [StringLength(120)]
        public string HardDiskNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(120)]
        public string ServerMacID { get; set; } = string.Empty;

        [Required]
        [StringLength(120)]
        public string MotherboardNumber { get; set; } = string.Empty;

        [StringLength(60)]
        public string? PublicIPAddress { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime ExpiryDate { get; set; }

        public DateTime? LastLoginDate { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        [Column("OTP_Verified")]
        public bool OTP_Verified { get; set; }

        [Column("AMC_Expireddate")]
        public DateTime? AMC_Expireddate { get; set; }

        [Column("AppUrl")]
        public string? AppUrl { get; set; }

        [Column("ProductType")]
        [StringLength(100)]
        public string? ProductType { get; set; }

        [Column("IsDisplayAlerts")]
        public bool IsDisplayAlerts { get; set; }

        [Column("AlertStartdate")]
        public DateTime? AlertStartDate { get; set; }

        [Column("AlertStartTime")]
        public TimeSpan? AlertStartTime { get; set; }

        [Column("AlertEnddate")]
        public DateTime? AlertEndDate { get; set; }

        [Column("AlertEndTime")]
        public TimeSpan? AlertEndTime { get; set; }

        [Column("AlertMessage")]
        public string? AlertMessage { get; set; }
    }
}