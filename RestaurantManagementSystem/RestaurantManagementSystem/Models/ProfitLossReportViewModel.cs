using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RestaurantManagementSystem.Models
{
    // ─────────────────────────────────────────────────────────────────────────
    // MAIN VIEWMODEL
    // ─────────────────────────────────────────────────────────────────────────
    public class ProfitLossReportViewModel
    {
        public ProfitLossFilter Filter { get; set; } = new ProfitLossFilter();
        public ProfitLossSummary Summary { get; set; } = new ProfitLossSummary();
        public List<ProfitLossMenuItemRow> MenuItems { get; set; } = new();
        public List<ProfitLossBranchRow> BranchData { get; set; } = new();
        public List<ProfitLossCategoryRow> CategoryData { get; set; } = new();
        public List<ProfitLossPeriodRow> PeriodData { get; set; } = new();
        public List<ProfitLossMenuItemRow> TopProfitable { get; set; } = new();
        public List<ProfitLossMenuItemRow> LeastProfitable { get; set; } = new();

        // Supporting data for dropdowns
        public List<SelectListItem> Branches { get; set; } = new();
        public List<SelectListItem> Categories { get; set; } = new();
        public bool IsMainBranchAdmin { get; set; }

        // Active login branch context
        public int ActiveBranchId { get; set; }
        public string ActiveBranchName { get; set; } = "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FILTER
    // ─────────────────────────────────────────────────────────────────────────
    public class ProfitLossFilter
    {
        [Display(Name = "From Date")]
        [DataType(DataType.Date)]
        public DateTime? StartDate { get; set; } = DateTime.Today.AddDays(-30);

        [Display(Name = "To Date")]
        [DataType(DataType.Date)]
        public DateTime? EndDate { get; set; } = DateTime.Today;

        /// <summary>daily | weekly | monthly | quarterly | yearly</summary>
        [Display(Name = "Period Grouping")]
        public string GroupBy { get; set; } = "monthly";

        /// <summary>For main branch admin — filter to specific branches.</summary>
        public List<int> SelectedBranchIds { get; set; } = new();

        [Display(Name = "Category")]
        public int? CategoryId { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SUMMARY
    // ─────────────────────────────────────────────────────────────────────────
    public class ProfitLossSummary
    {
        public decimal TotalSales { get; set; }
        public decimal TotalCost { get; set; }
        public decimal GrossProfit => TotalSales - TotalCost;
        public decimal FoodCostPct => TotalSales == 0 ? 0 : Math.Round(TotalCost * 100m / TotalSales, 2);
        public decimal GrossProfitPct => TotalSales == 0 ? 0 : Math.Round(GrossProfit * 100m / TotalSales, 2);
        public long TotalQtySold { get; set; }
        public int TotalOrders { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MENU ITEM ROW  (used also for Top/Least profitable)
    // ─────────────────────────────────────────────────────────────────────────
    public class ProfitLossMenuItemRow
    {
        public int MenuItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public int QtySold { get; set; }
        public decimal SalesValue { get; set; }
        public decimal CostValue { get; set; }
        public decimal GrossProfit => SalesValue - CostValue;
        public decimal ProfitPct => SalesValue == 0 ? 0 : Math.Round(GrossProfit * 100m / SalesValue, 2);
        public decimal FoodCostPct => SalesValue == 0 ? 0 : Math.Round(CostValue * 100m / SalesValue, 2);
        public bool HasBOM { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRANCH ROW
    // ─────────────────────────────────────────────────────────────────────────
    public class ProfitLossBranchRow
    {
        public int BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public decimal SalesValue { get; set; }
        public decimal CostValue { get; set; }
        public decimal GrossProfit => SalesValue - CostValue;
        public decimal ProfitPct => SalesValue == 0 ? 0 : Math.Round(GrossProfit * 100m / SalesValue, 2);
        public decimal FoodCostPct => SalesValue == 0 ? 0 : Math.Round(CostValue * 100m / SalesValue, 2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CATEGORY ROW
    // ─────────────────────────────────────────────────────────────────────────
    public class ProfitLossCategoryRow
    {
        public string CategoryName { get; set; } = string.Empty;
        public int QtySold { get; set; }
        public decimal SalesValue { get; set; }
        public decimal CostValue { get; set; }
        public decimal GrossProfit => SalesValue - CostValue;
        public decimal ProfitPct => SalesValue == 0 ? 0 : Math.Round(GrossProfit * 100m / SalesValue, 2);
        public decimal FoodCostPct => SalesValue == 0 ? 0 : Math.Round(CostValue * 100m / SalesValue, 2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PERIOD ROW  (daily / weekly / monthly / quarterly / yearly)
    // ─────────────────────────────────────────────────────────────────────────
    public class ProfitLossPeriodRow
    {
        public string PeriodLabel { get; set; } = string.Empty;
        public int SortKey { get; set; }
        public int QtySold { get; set; }
        public decimal SalesValue { get; set; }
        public decimal CostValue { get; set; }
        public decimal GrossProfit => SalesValue - CostValue;
        public decimal ProfitPct => SalesValue == 0 ? 0 : Math.Round(GrossProfit * 100m / SalesValue, 2);
        public decimal FoodCostPct => SalesValue == 0 ? 0 : Math.Round(CostValue * 100m / SalesValue, 2);
    }
}
