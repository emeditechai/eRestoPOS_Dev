namespace RestaurantManagementSystem.ViewModels
{
    public class LicenseRegistrationViewModel
    {
        [Required(ErrorMessage = "Client name is required")]
        [Display(Name = "Client Name")]
        [StringLength(200, ErrorMessage = "Client name cannot exceed {1} characters")]
        public string ClientName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Contact number is required")]
        [Display(Name = "Contact Number")]
        [Phone(ErrorMessage = "Enter a valid contact number")]
        [StringLength(30, ErrorMessage = "Contact number cannot exceed {1} characters")]
        public string ContactNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Client email ID is required")]
        [Display(Name = "Client Email ID")]
        [EmailAddress(ErrorMessage = "Enter a valid email address")]
        [StringLength(200, ErrorMessage = "Email ID cannot exceed {1} characters")]
        public string? EmailID { get; set; }

        [Display(Name = "Client Code")]
        public string ClientCodePreview { get; set; } = "Generated on registration";

        [Display(Name = "License Key")]
        public string LicenseKeyPreview { get; set; } = "Generated on registration";

        [Display(Name = "Server MAC ID")]
        public string ServerMacID { get; set; } = string.Empty;

        [Display(Name = "Hard Disk Number")]
        public string HardDiskNumber { get; set; } = string.Empty;

        [Display(Name = "Motherboard Number")]
        public string MotherboardNumber { get; set; } = string.Empty;

        [Display(Name = "Start Date")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; } = DateTime.Now;

        [Required(ErrorMessage = "Expiry date is required")]
        [Display(Name = "End Date")]
        [DataType(DataType.Date)]
        public DateTime ExpiryDate { get; set; } = DateTime.Today.AddYears(1);

        [Display(Name = "AMC Expiry Date")]
        [DataType(DataType.Date)]
        public DateTime? AmcExpiryDate { get; set; }
    }

    public class LicenseBlockedViewModel
    {
        public LicenseGateStatus Status { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string? FailureReason { get; set; }

        public string? ClientCode { get; set; }

        public string? LicenseKey { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public DateTime? LastValidatedAt { get; set; }

        public bool ShowRetryAction { get; set; }
    }
}