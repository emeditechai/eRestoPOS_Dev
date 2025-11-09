using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.Models
{
    /// <summary>
    /// ViewModel for Day Closing Dashboard
    /// </summary>
    public class DayClosingDashboardViewModel
    {
        public DateTime BusinessDate { get; set; } = DateTime.Today;
        public List<CashierDayCloseViewModel> CashierClosings { get; set; } = new List<CashierDayCloseViewModel>();
        public DayLockStatus? LockStatus { get; set; }
        public DaySummary Summary { get; set; } = new DaySummary();
        public bool CanLockDay { get; set; }
        public string? LockMessage { get; set; }
    }

    /// <summary>
    /// ViewModel for individual cashier closing
    /// </summary>
    public class CashierDayCloseViewModel
    {
        public int Id { get; set; }
        public int CashierId { get; set; }
        public string CashierName { get; set; } = string.Empty;
        public decimal OpeningFloat { get; set; }
        public decimal SystemAmount { get; set; }
        public decimal? DeclaredAmount { get; set; }
        public decimal ExpectedCash { get; set; }
        public decimal? Variance { get; set; }
        public string Status { get; set; } = "PENDING";
        public string? ApprovedBy { get; set; }
        public string? ApprovalComment { get; set; }
        public bool LockedFlag { get; set; }
        public DateTime? LockedAt { get; set; }
        public string? LockedBy { get; set; }
        
        // Display properties
        public string StatusBadgeClass { get; set; } = "bg-secondary";
        public string StatusIcon { get; set; } = "fa-question";
        public bool RequiresApproval { get; set; }
        public bool CanEdit => !LockedFlag && Status != "LOCKED";
        public string VarianceDisplay => Variance.HasValue ? $"₹{Math.Abs(Variance.Value):N2} {(Variance.Value >= 0 ? "Over" : "Short")}" : "-";
    }

    /// <summary>
    /// Day lock status information
    /// </summary>
    public class DayLockStatus
    {
        public int LockId { get; set; }
        public DateTime BusinessDate { get; set; }
        public string LockedBy { get; set; } = string.Empty;
        public DateTime LockTime { get; set; }
        public string? Remarks { get; set; }
        public string Status { get; set; } = "LOCKED";
        public bool IsLocked => Status == "LOCKED";
    }

    /// <summary>
    /// Day closing summary statistics
    /// </summary>
    public class DaySummary
    {
        public int TotalCashiers { get; set; }
        public int PendingCount { get; set; }
        public int OkCount { get; set; }
        public int CheckCount { get; set; }
        public int LockedCount { get; set; }
        public decimal TotalOpeningFloat { get; set; }
        public decimal TotalSystemAmount { get; set; }
        public decimal TotalDeclaredAmount { get; set; }
        public decimal TotalVariance { get; set; }
        public decimal TotalExpectedCash { get; set; }
    }

    /// <summary>
    /// ViewModel for opening float entry
    /// </summary>
    public class OpenFloatViewModel
    {
        public DateTime BusinessDate { get; set; } = DateTime.Today;
        
        [Required(ErrorMessage = "Please select a cashier")]
        [Display(Name = "Cashier")]
        public int CashierId { get; set; }
        
        public string? CashierName { get; set; }
        
        [Required(ErrorMessage = "Opening float is required")]
        [Range(0, 1000000, ErrorMessage = "Opening float must be between 0 and 1,000,000")]
        [Display(Name = "Opening Float Amount")]
        public decimal OpeningFloat { get; set; }
        
        public List<CashierOption> AvailableCashiers { get; set; } = new List<CashierOption>();
    }

    /// <summary>
    /// Cashier selection option
    /// </summary>
    public class CashierOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public bool AlreadyInitialized { get; set; }
    }

    /// <summary>
    /// ViewModel for declaring cash
    /// </summary>
    public class DeclaredCashViewModel
    {
        public int CloseId { get; set; }
        public DateTime BusinessDate { get; set; }
        public int CashierId { get; set; }
        public string CashierName { get; set; } = string.Empty;
        public decimal OpeningFloat { get; set; }
        public decimal SystemAmount { get; set; }
        public decimal ExpectedCash { get; set; }
        
        [Required(ErrorMessage = "Please enter the declared cash amount")]
        [Range(0, 10000000, ErrorMessage = "Declared amount must be between 0 and 10,000,000")]
        [Display(Name = "Declared Cash Amount")]
        public decimal DeclaredAmount { get; set; }
        
        // Denomination breakdown (optional)
        [Display(Name = "₹2000 Notes")]
        public int? Notes2000 { get; set; }
        
        [Display(Name = "₹500 Notes")]
        public int? Notes500 { get; set; }
        
        [Display(Name = "₹200 Notes")]
        public int? Notes200 { get; set; }
        
        [Display(Name = "₹100 Notes")]
        public int? Notes100 { get; set; }
        
        [Display(Name = "₹50 Notes")]
        public int? Notes50 { get; set; }
        
        [Display(Name = "₹20 Notes")]
        public int? Notes20 { get; set; }
        
        [Display(Name = "₹10 Notes")]
        public int? Notes10 { get; set; }
        
        [Display(Name = "Coins")]
        public decimal? Coins { get; set; }
        
        public decimal CalculatedTotal => 
            (Notes2000 ?? 0) * 2000 +
            (Notes500 ?? 0) * 500 +
            (Notes200 ?? 0) * 200 +
            (Notes100 ?? 0) * 100 +
            (Notes50 ?? 0) * 50 +
            (Notes20 ?? 0) * 20 +
            (Notes10 ?? 0) * 10 +
            (Coins ?? 0);
    }

    /// <summary>
    /// ViewModel for variance approval
    /// </summary>
    public class VarianceApprovalViewModel
    {
        public int CloseId { get; set; }
        public DateTime BusinessDate { get; set; }
        public string CashierName { get; set; } = string.Empty;
        public decimal OpeningFloat { get; set; }
        public decimal SystemAmount { get; set; }
        public decimal DeclaredAmount { get; set; }
        public decimal Variance { get; set; }
        public decimal ExpectedCash { get; set; }
        
        [Required(ErrorMessage = "Approval comment is required")]
        [MaxLength(500, ErrorMessage = "Comment cannot exceed 500 characters")]
        [Display(Name = "Approval Comment")]
        public string ApprovalComment { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Please select an approval action")]
        public string ApprovalAction { get; set; } = "APPROVE"; // APPROVE or REJECT
    }

    /// <summary>
    /// ViewModel for day lock operation
    /// </summary>
    public class LockDayViewModel
    {
        public DateTime BusinessDate { get; set; } = DateTime.Today;
        
        [MaxLength(1000, ErrorMessage = "Remarks cannot exceed 1000 characters")]
        [Display(Name = "Lock Remarks (Optional)")]
        public string? Remarks { get; set; }
        
        public int PendingCount { get; set; }
        public int CheckCount { get; set; }
        public bool CanLock => PendingCount == 0 && CheckCount == 0;
        public string? ValidationMessage { get; set; }
    }

    /// <summary>
    /// ViewModel for EOD Report
    /// </summary>
    public class EODReportViewModel
    {
        public DateTime BusinessDate { get; set; }
        public string RestaurantName { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public string GeneratedBy { get; set; } = string.Empty;
        
        public List<CashierDayCloseViewModel> CashierDetails { get; set; } = new List<CashierDayCloseViewModel>();
        public DaySummary Summary { get; set; } = new DaySummary();
        public DayLockStatus? LockStatus { get; set; }
        
        // Additional sales summary
        public decimal TotalSales { get; set; }
        public decimal CashSales { get; set; }
        public decimal CardSales { get; set; }
        public decimal UPISales { get; set; }
        public decimal OtherSales { get; set; }
        public int TotalOrders { get; set; }
        public int TotalCustomers { get; set; }
    }
}
