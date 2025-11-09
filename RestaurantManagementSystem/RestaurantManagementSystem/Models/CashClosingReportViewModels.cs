using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.Models
{
    // =====================================================
    // Cash Closing Report View Models
    // =====================================================

    public class CashClosingReportViewModel
    {
        public CashClosingReportFilters Filters { get; set; } = new();
        public CashClosingReportSummary Summary { get; set; } = new();
        public List<CashClosingDailySummary> DailySummaries { get; set; } = new();
        public List<CashClosingDetailRecord> DetailRecords { get; set; } = new();
        public List<CashClosingCashierPerformance> CashierPerformance { get; set; } = new();
        public List<CashClosingDayLockAudit> DayLockAudits { get; set; } = new();
    }

    public class CashClosingReportFilters
    {
        [Required(ErrorMessage = "Start date is required")]
        [Display(Name = "Start Date")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; } = DateTime.Today.AddDays(-7);

        [Required(ErrorMessage = "End date is required")]
        [Display(Name = "End Date")]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; } = DateTime.Today;

        [Display(Name = "Cashier (Optional)")]
        public int? CashierId { get; set; }

        [Display(Name = "Cashier Name")]
        public string? CashierName { get; set; }
    }

    public class CashClosingReportSummary
    {
        public int TotalDays { get; set; }
        public int TotalCashiers { get; set; }
        public decimal TotalOpeningFloat { get; set; }
        public decimal TotalSystemAmount { get; set; }
        public decimal TotalDeclaredAmount { get; set; }
        public decimal TotalVariance { get; set; }
        public decimal TotalCashOver { get; set; }
        public decimal TotalCashShort { get; set; }
        public int ApprovedCount { get; set; }
        public int PendingApprovalCount { get; set; }
        public int LockedCount { get; set; }

        // Calculated properties
        public decimal TotalExpectedCash => TotalOpeningFloat + TotalSystemAmount;
        public decimal VariancePercentage => TotalSystemAmount > 0 
            ? Math.Round((TotalVariance / TotalSystemAmount) * 100, 2) 
            : 0;
        public string OverallStatus => TotalVariance >= 0 ? "Over" : "Short";
    }

    public class CashClosingDailySummary
    {
        public DateTime BusinessDate { get; set; }
        public int CashierCount { get; set; }
        public decimal DayOpeningFloat { get; set; }
        public decimal DaySystemAmount { get; set; }
        public decimal DayDeclaredAmount { get; set; }
        public decimal DayVariance { get; set; }
        public decimal DayCashOver { get; set; }
        public decimal DayCashShort { get; set; }
        public string IsDayLocked { get; set; } = "No";

        // Calculated properties
        public decimal DayExpectedCash => DayOpeningFloat + DaySystemAmount;
        public string DayStatus => DayVariance >= 0 ? "Over" : "Short";
        public bool IsLocked => IsDayLocked == "Yes";
    }

    public class CashClosingDetailRecord
    {
        public DateTime BusinessDate { get; set; }
        public int CashierId { get; set; }
        public string CashierName { get; set; } = string.Empty;
        public decimal OpeningFloat { get; set; }
        public decimal SystemAmount { get; set; }
        public decimal ExpectedCash { get; set; }
        public decimal DeclaredAmount { get; set; }
        public decimal Variance { get; set; }
        public string VarianceType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ApprovedBy { get; set; }
        public string? ApprovalComment { get; set; }
        public bool LockedFlag { get; set; }
        public DateTime? LockedAt { get; set; }
        public string? LockedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Display properties
        public string StatusBadgeClass => Status switch
        {
            "OK" => "badge bg-success",
            "CHECK" => "badge bg-warning",
            "LOCKED" => "badge bg-secondary",
            "PENDING" => "badge bg-info",
            _ => "badge bg-light text-dark"
        };

        public string VarianceTypeClass => VarianceType switch
        {
            "Over" => "text-success",
            "Short" => "text-danger",
            _ => "text-muted"
        };
    }

    public class CashClosingCashierPerformance
    {
        public int CashierId { get; set; }
        public string CashierName { get; set; } = string.Empty;
        public int TotalDaysWorked { get; set; }
        public decimal TotalCashCollected { get; set; }
        public decimal AverageVariance { get; set; }
        public decimal BestVariance { get; set; }
        public decimal WorstVariance { get; set; }
        public int DaysWithinTolerance { get; set; }
        public int DaysAboveTolerance { get; set; }
        public int ApprovedDays { get; set; }
        public int PendingDays { get; set; }

        // Calculated properties
        public decimal TolerancePercentage => TotalDaysWorked > 0 
            ? Math.Round((decimal)DaysWithinTolerance / TotalDaysWorked * 100, 2) 
            : 0;
        public string PerformanceRating
        {
            get
            {
                if (TolerancePercentage >= 95) return "Excellent";
                if (TolerancePercentage >= 85) return "Good";
                if (TolerancePercentage >= 70) return "Average";
                return "Needs Improvement";
            }
        }
        public string PerformanceClass
        {
            get
            {
                if (TolerancePercentage >= 95) return "text-success fw-bold";
                if (TolerancePercentage >= 85) return "text-primary";
                if (TolerancePercentage >= 70) return "text-warning";
                return "text-danger";
            }
        }
    }

    public class CashClosingDayLockAudit
    {
        public DateTime BusinessDate { get; set; }
        public string LockedBy { get; set; } = string.Empty;
        public DateTime LockTime { get; set; }
        public string? Remarks { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ReopenedBy { get; set; }
        public DateTime? ReopenedAt { get; set; }
        public string? ReopenReason { get; set; }

        // Display properties
        public bool IsReopened => Status == "REOPENED";
        public string StatusBadgeClass => Status == "LOCKED" ? "badge bg-secondary" : "badge bg-warning";
    }
}
