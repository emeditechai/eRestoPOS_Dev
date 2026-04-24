using Microsoft.AspNetCore.Mvc.Rendering;

namespace RestaurantManagementSystem.ViewModels
{
    public class CollectionRegisterViewModel
    {
        public CollectionRegisterFilter Filter { get; set; } = new CollectionRegisterFilter();
        public List<SelectListItem> PaymentMethods { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Counters { get; set; } = new List<SelectListItem>();
        public List<CollectionRegisterRow> Rows { get; set; } = new List<CollectionRegisterRow>();
        public CollectionRegisterSummary Summary { get; set; } = new CollectionRegisterSummary();
        public List<SelectListItem> Branches { get; set; } = new List<SelectListItem>();
        public bool IsMainBranchAdmin { get; set; }
    }

    public class CollectionRegisterFilter
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int? PaymentMethodId { get; set; }
        public string PaymentMethodName { get; set; } = "ALL";
        public int? CounterId { get; set; }
        public string CounterName { get; set; } = "ALL";
        public int? UserId { get; set; }
        public string UserDisplayName { get; set; } = string.Empty;
        public List<int> SelectedBranchIds { get; set; } = new List<int>();
    }

    public class CollectionRegisterRow
    {
        public string OrderNo { get; set; }
        public string BillNo { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string TableNo { get; set; }
        public string Username { get; set; }
        public int? CounterId { get; set; }
        public string CounterName { get; set; }
        public decimal ActualBillAmount { get; set; } // Subtotal - Discount (before GST)
        public decimal DiscountAmount { get; set; }
        public decimal GSTAmount { get; set; } // CGST + SGST
        public decimal RoundOffAmount { get; set; }
        public decimal ReceiptAmount { get; set; }
        public string PaymentMethod { get; set; }
        public string Details { get; set; }
        public DateTime PaymentDate { get; set; }
        public int PaymentStatus { get; set; } // 1=Approved, 3=Void/Refund
    }

    public class CollectionRegisterSummary
    {
        public int TotalTransactions { get; set; }
        public decimal TotalActualAmount { get; set; } // Sum of (Subtotal - Discount)
        public decimal TotalDiscount { get; set; }
        public decimal TotalGST { get; set; } // Sum of GST amounts
        public decimal TotalRoundOff { get; set; }
        public decimal TotalReceiptAmount { get; set; }
    }

    public class WaitlistGuestReportViewModel
    {
        public WaitlistGuestReportFilter Filter { get; set; } = new WaitlistGuestReportFilter();
        public List<WaitlistGuestReportRow> Rows { get; set; } = new List<WaitlistGuestReportRow>();
        public WaitlistGuestReportSummary Summary { get; set; } = new WaitlistGuestReportSummary();
        public List<SelectListItem> Branches { get; set; } = new List<SelectListItem>();
        public bool IsMainBranchAdmin { get; set; }
        public string SelectedBranchLabel { get; set; } = "Active Branch";
    }

    public class WaitlistGuestReportFilter
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<int> SelectedBranchIds { get; set; } = new List<int>();
    }

    public class WaitlistGuestReportRow
    {
        public int WaitlistId { get; set; }
        public DateTime AddedAt { get; set; }
        public string GuestName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public int PartySize { get; set; }
        public int QuotedWaitTime { get; set; }
        public string StatusText { get; set; } = string.Empty;
        public DateTime? NotifiedAt { get; set; }
        public DateTime? SeatedAt { get; set; }
        public string TableNumber { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public int? ActualWaitMinutes { get; set; }
        public bool SeatedWithoutTable { get; set; }
    }

    public class WaitlistGuestReportSummary
    {
        public int TotalGuests { get; set; }
        public int WaitingGuests { get; set; }
        public int NotifiedGuests { get; set; }
        public int SeatedGuests { get; set; }
        public int SeatedWithoutTableGuests { get; set; }
        public decimal AverageQuotedWaitTime { get; set; }
        public decimal AverageActualWaitTime { get; set; }
    }
}
