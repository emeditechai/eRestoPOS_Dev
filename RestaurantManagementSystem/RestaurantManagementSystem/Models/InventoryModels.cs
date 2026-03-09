using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.Models
{
        // ─────────────────────────────────────────────────────────────────────────
    // INVENTORY PARAMETERS
    // ─────────────────────────────────────────────────────────────────────────

    public class InventoryParameters
    {
        public int ParamId { get; set; }
        public int BranchId { get; set; }

        [Display(Name = "Purchase Only From Main Godown")]
        public bool PurchaseOnlyFromMainGodown { get; set; }

        [Display(Name = "GRN Mandatory")]
        public bool GRNMandatory { get; set; } = true;

        [Display(Name = "Allow Direct Purchase")]
        public bool AllowDirectPurchase { get; set; } = true;

        [Display(Name = "Transfer Price Mode")]
        public string TransferPriceMode { get; set; } = "AverageCost";

        [Display(Name = "Allow Negative Stock")]
        public bool NegativeStockAllowed { get; set; }

        [Display(Name = "Auto Consumption On Sale (BOM)")]
        public bool AutoConsumptionOnSale { get; set; } = true;

        public DateTime? UpdatedAt { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OPENING STOCK
    // ─────────────────────────────────────────────────────────────────────────

    public class OpeningStockItem
    {
        public int OpeningStockId { get; set; }
        public int BranchId { get; set; }
        public int GodownId { get; set; }
        public int ItemId { get; set; }

        [Display(Name = "Stock Date")]
        public DateTime StockDate { get; set; } = DateTime.Today;

        [Range(0.001, double.MaxValue, ErrorMessage = "Quantity must be > 0")]
        public decimal Quantity { get; set; }

        public int UOMId { get; set; }

        [Display(Name = "Cost Price")]
        [Range(0, double.MaxValue)]
        public decimal CostPrice { get; set; }

        public decimal? TotalValue { get; set; }

        [StringLength(300)]
        public string? Remarks { get; set; }

        public bool IsPosted { get; set; }
        public DateTime? CreatedAt { get; set; }

        // Display helpers
        public string? ItemName { get; set; }
        public string? ItemCode { get; set; }
        public string? UOMCode { get; set; }
        public string? UOMName { get; set; }
        public string? GodownName { get; set; }
        public string? GodownCode { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PURCHASE ORDER
    // ─────────────────────────────────────────────────────────────────────────

    public class PurchaseOrderHeader
    {
        public int POId { get; set; }
        public string? PONumber { get; set; }
        public int BranchId { get; set; }
        public int GodownId { get; set; }
        public int SupplierId { get; set; }

        [Required, Display(Name = "PO Date")]
        public DateTime PODate { get; set; } = DateTime.Today;

        [Display(Name = "Expected Delivery Date")]
        public DateTime? ExpectedDate { get; set; }

        [Display(Name = "GST Type")]
        public string GSTType { get; set; } = "Exclusive";

        [StringLength(100)]
        [Display(Name = "Payment Terms")]
        public string? PaymentTerms { get; set; }

        [StringLength(500)]
        public string? Remarks { get; set; }

        public string Status { get; set; } = "Draft";
        public decimal SubTotal { get; set; }
        public decimal TotalGSTAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime? CreatedAt { get; set; }

        // Display helpers
        public string? GodownName { get; set; }
        public string? GodownCode { get; set; }
        public string? SupplierName { get; set; }
        public string? SupplierGST { get; set; }
        public string? SupplierPhone { get; set; }
        public int LineCount { get; set; }

        public List<PurchaseOrderLine> Lines { get; set; } = new();
    }

    public class PurchaseOrderLine
    {
        public int PODetailId { get; set; }
        public int POId { get; set; }
        public int ItemId { get; set; }
        public int UOMId { get; set; }

        [Range(0.001, double.MaxValue)]
        public decimal OrderedQty { get; set; }

        public decimal ReceivedQty { get; set; }
        public decimal? PendingQty { get; set; }

        [Range(0, double.MaxValue)]
        [Display(Name = "Unit Rate")]
        public decimal UnitRate { get; set; }

        [Range(0, 100)]
        [Display(Name = "GST %")]
        public decimal GSTPercent { get; set; }

        // GST breakdown (stored in DB, auto-computed from GSTPercent by SP)
        public decimal CGSTPercent   { get; set; }
        public decimal SGSTPercent   { get; set; }
        public decimal IGSTPercent   { get; set; }
        public decimal CGSTAmount    { get; set; }
        public decimal SGSTAmount    { get; set; }
        public decimal IGSTAmount    { get; set; }
        public decimal TaxableAmount { get; set; }

        [StringLength(200)]
        public string? Remarks { get; set; }

        // Display helpers
        public string? ItemName { get; set; }
        public string? ItemCode { get; set; }
        public string? UOMCode  { get; set; }
        public string? UOMName  { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GRN
    // ─────────────────────────────────────────────────────────────────────────

    public class GRNHeader
    {
        public int GRNId { get; set; }
        public string? GRNNumber { get; set; }
        public int BranchId { get; set; }
        public int POId { get; set; }
        public int GodownId { get; set; }
        public int SupplierId { get; set; }

        [Display(Name = "GRN Date")]
        public DateTime GRNDate { get; set; } = DateTime.Today;

        [StringLength(50)]
        [Display(Name = "Invoice No")]
        public string? InvoiceNo { get; set; }

        [Display(Name = "Invoice Date")]
        public DateTime? InvoiceDate { get; set; }

        [Display(Name = "GST Type")]
        public string GSTType { get; set; } = "Exclusive";

        [StringLength(500)]
        public string? Remarks { get; set; }

        public decimal SubTotal { get; set; }
        public decimal TotalGSTAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "Draft";
        public DateTime? CreatedAt { get; set; }

        // Display helpers
        public string? GodownName { get; set; }
        public string? SupplierName { get; set; }
        public string? PONumber { get; set; }
        public int LineCount { get; set; }

        public List<GRNLine> Lines { get; set; } = new();
    }

    public class GRNLine
    {
        public int GRNDetailId { get; set; }
        public int GRNId { get; set; }
        public int? PODetailId { get; set; }
        public int ItemId { get; set; }
        public int UOMId { get; set; }
        public decimal OrderedQty { get; set; }
        public decimal ReceivedQty { get; set; }
        public decimal RejectedQty { get; set; }
        public decimal? AcceptedQty { get; set; }

        [Range(0, double.MaxValue)]
        [Display(Name = "Unit Rate")]
        public decimal UnitRate { get; set; }

        [Range(0, 100)]
        [Display(Name = "GST %")]
        public decimal GSTPercent { get; set; }

        [StringLength(200)]
        public string? Remarks { get; set; }

        // Display helpers
        public string? ItemName { get; set; }
        public string? ItemCode { get; set; }
        public string? UOMCode { get; set; }
        public string? UOMName { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STOCK TRANSFER
    // ─────────────────────────────────────────────────────────────────────────

    public class StockTransferHeader
    {
        public int TransferId { get; set; }
        public string? TransferNumber { get; set; }
        public int BranchId { get; set; }
        public int FromGodownId { get; set; }
        public int ToGodownId { get; set; }

        [Display(Name = "Transfer Date")]
        public DateTime TransferDate { get; set; } = DateTime.Today;

        [Display(Name = "Transfer Type")]
        public string TransferType { get; set; } = "Internal";

        [Display(Name = "Price Mode")]
        public string PriceMode { get; set; } = "AverageCost";

        [StringLength(500)]
        public string? Remarks { get; set; }

        public string Status { get; set; } = "Draft";
        public decimal TotalQty { get; set; }
        public decimal TotalValue { get; set; }
        public DateTime? CreatedAt { get; set; }

        // Display helpers
        public string? FromGodownName { get; set; }
        public string? ToGodownName { get; set; }
        public int LineCount { get; set; }

        public List<StockTransferLine> Lines { get; set; } = new();
    }

    public class StockTransferLine
    {
        public int TransferDetailId { get; set; }
        public int TransferId { get; set; }
        public int ItemId { get; set; }
        public int UOMId { get; set; }

        [Range(0.001, double.MaxValue)]
        public decimal Quantity { get; set; }

        [Display(Name = "Unit Cost")]
        public decimal UnitCost { get; set; }

        public decimal? TotalCost { get; set; }

        [StringLength(200)]
        public string? Remarks { get; set; }

        // Display helpers
        public string? ItemName { get; set; }
        public string? ItemCode { get; set; }
        public string? UOMCode { get; set; }
        public string? UOMName { get; set; }
    }

    /// <summary>Used to populate From/To Godown dropdowns on the Stock Transfer form.</summary>
    public class GodownDropdownItem
    {
        public int    GodownId   { get; set; }
        public string GodownName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public int    BranchId   { get; set; }
        public bool   IsDisabled { get; set; }
        /// <summary>Display text: "GodownName (BranchName)"</summary>
        public string Label => $"{GodownName} ({BranchName})";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DAMAGE ENTRY
    // ─────────────────────────────────────────────────────────────────────────

    public class DamageEntryHeader
    {
        public int DamageId { get; set; }
        public string? DamageNumber { get; set; }
        public int BranchId { get; set; }
        public int GodownId { get; set; }

        [Display(Name = "Damage Date")]
        public DateTime DamageDate { get; set; } = DateTime.Today;

        [Display(Name = "Damage Type")]
        public string DamageType { get; set; } = "Damage";

        [StringLength(500)]
        public string? Remarks { get; set; }

        public string Status { get; set; } = "Draft";
        public decimal TotalQty { get; set; }
        public decimal TotalValue { get; set; }
        public DateTime? CreatedAt { get; set; }

        // Display helpers
        public string? GodownName { get; set; }

        public List<DamageEntryLine> Lines { get; set; } = new();
    }

    public class DamageEntryLine
    {
        public int DamageDetailId { get; set; }
        public int DamageId { get; set; }
        public int ItemId { get; set; }
        public int UOMId { get; set; }

        [Range(0.001, double.MaxValue)]
        public decimal Quantity { get; set; }

        [Display(Name = "Unit Cost")]
        public decimal UnitCost { get; set; }

        public decimal? TotalCost { get; set; }

        [StringLength(200)]
        public string? Reason { get; set; }

        // Display helpers
        public string? ItemName { get; set; }
        public string? ItemCode { get; set; }
        public string? UOMCode { get; set; }
        public string? UOMName { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STOCK LEDGER
    // ─────────────────────────────────────────────────────────────────────────

    public class StockLedgerEntry
    {
        public int LedgerId { get; set; }
        public int BranchId { get; set; }
        public int GodownId { get; set; }
        public int ItemId { get; set; }
        public DateTime TransactionDate { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public string? ReferenceType { get; set; }
        public int? ReferenceId { get; set; }
        public string? ReferenceNumber { get; set; }
        public decimal InQuantity { get; set; }
        public decimal OutQuantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal? TotalValue { get; set; }
        public decimal BalanceQty { get; set; }
        public decimal BalanceValue { get; set; }
        public decimal AverageCost { get; set; }
        public string? Remarks { get; set; }
        public DateTime? CreatedAt { get; set; }

        // Display helpers
        public string? ItemName { get; set; }
        public string? ItemCode { get; set; }
        public string? UOMCode { get; set; }
        public string? GodownName { get; set; }
        public string? GodownCode { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CURRENT STOCK
    // ─────────────────────────────────────────────────────────────────────────

    public class CurrentStockItem
    {
        public int StockId { get; set; }
        public int BranchId { get; set; }
        public int GodownId { get; set; }
        public int ItemId { get; set; }
        public decimal BalanceQty { get; set; }
        public decimal AverageCost { get; set; }
        public decimal? StockValue { get; set; }
        public DateTime LastUpdated { get; set; }

        // Display helpers
        public string? ItemName { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemCategory { get; set; }
        public decimal? ReorderLevel { get; set; }
        public string? BaseUOMCode { get; set; }
        public string? BaseUOMName { get; set; }
        public string? GodownName { get; set; }
        public string? GodownCode { get; set; }
        public string? GodownType { get; set; }
        public bool IsLowStock { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REPORT VIEW MODELS
    // ─────────────────────────────────────────────────────────────────────────

    public class ClosingStockReportItem
    {
        public string? ItemName { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemCategory { get; set; }
        public string? GodownName { get; set; }
        public decimal OpeningQty { get; set; }
        public decimal PurchaseQty { get; set; }
        public decimal TransferInQty { get; set; }
        public decimal TransferOutQty { get; set; }
        public decimal DamageQty { get; set; }
        public decimal SaleQty { get; set; }
        public decimal ClosingQty { get; set; }
        public decimal AverageCost { get; set; }
        public decimal ClosingValue { get; set; }
    }

    public class StockValuationItem
    {
        public int GodownId { get; set; }
        public string? GodownName { get; set; }
        public int ItemId { get; set; }
        public string? ItemName { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemCategory { get; set; }
        public string? UOMCode { get; set; }
        public decimal BalanceQty { get; set; }
        public decimal AverageCost { get; set; }
        public decimal? StockValue { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class PurchaseRegisterItem
    {
        public int GRNId { get; set; }
        public string? GRNNumber { get; set; }
        public DateTime GRNDate { get; set; }
        public string? InvoiceNo { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public string? SupplierName { get; set; }
        public string? GSTNumber { get; set; }
        public string? GodownName { get; set; }
        public decimal SubTotal { get; set; }
        public decimal TotalGSTAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string? PONumber { get; set; }
    }

    public class TransferRegisterItem
    {
        public int TransferId { get; set; }
        public string? TransferNumber { get; set; }
        public DateTime TransferDate { get; set; }
        public string? TransferType { get; set; }
        public string? FromGodownName { get; set; }
        public string? ToGodownName { get; set; }
        public string? FromBranchName { get; set; }
        public string? ToBranchName { get; set; }
        public decimal TotalQty { get; set; }
        public decimal TotalValue { get; set; }
        public string? Status { get; set; }
        public string? Remarks { get; set; }
        public string? Direction { get; set; }   // SENT or RECEIVED
    }

    public class DamageRegisterItem
    {
        public int DamageId { get; set; }
        public string? DamageNumber { get; set; }
        public DateTime DamageDate { get; set; }
        public string? DamageType { get; set; }
        public string? GodownName { get; set; }
        public decimal TotalQty { get; set; }
        public decimal TotalValue { get; set; }
        public string? Remarks { get; set; }
        public string? Status { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INVENTORY DASHBOARD
    // ─────────────────────────────────────────────────────────────────────────

    public class InventoryDashboardViewModel
    {
        public decimal TotalStockValue { get; set; }
        public int LowStockItems { get; set; }
        public int PendingGRN { get; set; }
        public decimal TodayPurchase { get; set; }
        public decimal TodayConsumption { get; set; }
        public int ActiveGodowns { get; set; }
        public int TodayDamageCount { get; set; }

        public List<TopConsumedItem> TopConsumedItems { get; set; } = new();
        public List<LowStockAlert> LowStockAlerts { get; set; } = new();
    }

    public class TopConsumedItem
    {
        public string? ItemName { get; set; }
        public string? ItemCode { get; set; }
        public decimal TotalConsumed { get; set; }
        public string? UOMCode { get; set; }
    }

    public class LowStockAlert
    {
        public string? ItemName { get; set; }
        public string? ItemCode { get; set; }
        public decimal BalanceQty { get; set; }
        public decimal? ReorderLevel { get; set; }
        public string? UOMCode { get; set; }
        public string? GodownName { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FILTER VIEW MODELS
    // ─────────────────────────────────────────────────────────────────────────

    public class StockLedgerFilter
    {
        public int? GodownId { get; set; }
        public int? ItemId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? TxnType { get; set; }
    }

    public class InventoryReportFilter
    {
        public int? GodownId { get; set; }
        public int? SupplierId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public DateTime? AsOfDate { get; set; }
    }
}
