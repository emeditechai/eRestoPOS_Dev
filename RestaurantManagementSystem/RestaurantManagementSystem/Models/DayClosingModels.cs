using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantManagementSystem.Models
{
    /// <summary>
    /// Represents the opening float assigned to a cashier for a business day
    /// </summary>
    [Table("CashierDayOpening", Schema = "dbo")]
    public class CashierDayOpening
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Column(TypeName = "date")]
        public DateTime BusinessDate { get; set; }

        [Required]
        public int CashierId { get; set; }

        [Required]
        [MaxLength(100)]
        public string CashierName { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "decimal(10,2)")]
        public decimal OpeningFloat { get; set; }

        [Required]
        [MaxLength(50)]
        public string CreatedBy { get; set; } = string.Empty;

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [MaxLength(50)]
        public string? UpdatedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        // Navigation property
        [ForeignKey("CashierId")]
        public virtual User? Cashier { get; set; }
    }

    /// <summary>
    /// Represents the end-of-day cash declaration and variance for a cashier
    /// </summary>
    [Table("CashierDayClose", Schema = "dbo")]
    public class CashierDayClose
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Column(TypeName = "date")]
        public DateTime BusinessDate { get; set; }

        [Required]
        public int CashierId { get; set; }

        [Required]
        [MaxLength(100)]
        public string CashierName { get; set; } = string.Empty;

        /// <summary>
        /// Total cash amount calculated from POS system
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(10,2)")]
        public decimal SystemAmount { get; set; }

        /// <summary>
        /// Actual cash declared/counted by cashier
        /// </summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal? DeclaredAmount { get; set; }

        /// <summary>
        /// Opening float from CashierDayOpening
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(10,2)")]
        public decimal OpeningFloat { get; set; }

        /// <summary>
        /// Calculated variance: (DeclaredAmount + OpeningFloat) - SystemAmount
        /// </summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal? Variance { get; set; }

        /// <summary>
        /// Status: PENDING, OK, CHECK, LOCKED
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "PENDING";

        [MaxLength(50)]
        public string? ApprovedBy { get; set; }

        [MaxLength(500)]
        public string? ApprovalComment { get; set; }

        [Required]
        public bool LockedFlag { get; set; } = false;

        public DateTime? LockedAt { get; set; }

        [MaxLength(50)]
        public string? LockedBy { get; set; }

        [Required]
        [MaxLength(50)]
        public string CreatedBy { get; set; } = string.Empty;

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [MaxLength(50)]
        public string? UpdatedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }

        // Navigation property
        [ForeignKey("CashierId")]
        public virtual User? Cashier { get; set; }

        // Computed properties for display
        [NotMapped]
        public decimal ExpectedCash => SystemAmount + OpeningFloat;

        [NotMapped]
        public string StatusBadgeClass => Status switch
        {
            "PENDING" => "bg-warning text-dark",
            "OK" => "bg-success",
            "CHECK" => "bg-danger",
            "LOCKED" => "bg-secondary",
            _ => "bg-secondary"
        };

        [NotMapped]
        public string StatusIcon => Status switch
        {
            "PENDING" => "fa-clock",
            "OK" => "fa-check-circle",
            "CHECK" => "fa-exclamation-triangle",
            "LOCKED" => "fa-lock",
            _ => "fa-question-circle"
        };

        [NotMapped]
        public bool RequiresApproval => Status == "CHECK" && !LockedFlag;
    }

    /// <summary>
    /// Audit trail for day lock/unlock operations
    /// </summary>
    [Table("DayLockAudit", Schema = "dbo")]
    public class DayLockAudit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int LockId { get; set; }

        [Required]
        [Column(TypeName = "date")]
        public DateTime BusinessDate { get; set; }

        [Required]
        [MaxLength(50)]
        public string LockedBy { get; set; } = string.Empty;

        [Required]
        public DateTime LockTime { get; set; } = DateTime.Now;

        [MaxLength(1000)]
        public string? Remarks { get; set; }

        /// <summary>
        /// Status: LOCKED, REOPENED
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "LOCKED";

        [MaxLength(50)]
        public string? ReopenedBy { get; set; }

        public DateTime? ReopenedAt { get; set; }

        [MaxLength(500)]
        public string? ReopenReason { get; set; }

        // Computed properties
        [NotMapped]
        public bool IsLocked => Status == "LOCKED";

        [NotMapped]
        public string StatusBadgeClass => Status == "LOCKED" ? "bg-danger" : "bg-warning text-dark";
    }
}
