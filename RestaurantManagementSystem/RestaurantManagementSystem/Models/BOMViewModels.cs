using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.Models
{
    // ─────────────────────────────────────────────────────────
    //  BOM List  –  one row per Menu Item
    // ─────────────────────────────────────────────────────────
    public class BOMListItemViewModel
    {
        public int    MenuItemId       { get; set; }
        public string MenuItemName     { get; set; } = "";
        public string? CategoryName    { get; set; }
        /// <summary>Base (dine-in) selling price.</summary>
        public decimal SellingPrice    { get; set; }
        /// <summary>Takeout selling price (nullable).</summary>
        public decimal? TakeoutPrice   { get; set; }
        /// <summary>Delivery selling price (nullable).</summary>
        public decimal? DeliveryPrice  { get; set; }
        /// <summary>Room Service selling price (nullable).</summary>
        public decimal? RoomServicePrice { get; set; }
        /// <summary>Number of ingredient lines configured.</summary>
        public int    LineCount        { get; set; }
        /// <summary>Computed BOM cost (after yield adjustment). NULL = not yet calculated.</summary>
        public decimal? BOMCost        { get; set; }
        /// <summary>Gross margin % using Base price.</summary>
        public decimal? GrossMarginPct { get; set; }
        public DateTime? LastCalculated { get; set; }
        /// <summary>BOM is Configured when at least one line exists.</summary>
        public bool   IsConfigured     => LineCount > 0;

        // Margin helpers for all price types
        public decimal? TakeoutMarginPct     => CalcMargin(TakeoutPrice);
        public decimal? DeliveryMarginPct    => CalcMargin(DeliveryPrice);
        public decimal? RoomServiceMarginPct => CalcMargin(RoomServicePrice);

        private decimal? CalcMargin(decimal? price) =>
            (BOMCost.HasValue && price.HasValue && price > 0)
                ? Math.Round((price.Value - BOMCost.Value) / price.Value * 100, 2)
                : null;

        public string MarginBadgeClass => GrossMarginPct.HasValue
            ? (GrossMarginPct >= 60 ? "bg-success" :
               GrossMarginPct >= 40 ? "bg-warning text-dark" : "bg-danger")
            : "bg-secondary";

        public static string GetMarginBadgeClass(decimal? pct) => pct.HasValue
            ? (pct >= 60 ? "bg-success" : pct >= 40 ? "bg-warning text-dark" : "bg-danger")
            : "bg-secondary";
    }

    // ─────────────────────────────────────────────────────────
    //  BOM Configure Page  –  header + lines for one Menu Item
    // ─────────────────────────────────────────────────────────
    public class BOMConfigureViewModel
    {
        public int    MenuItemId         { get; set; }
        public string MenuItemName       { get; set; } = "";
        public string? CategoryName      { get; set; }
        /// <summary>Base (dine-in) price.</summary>
        public decimal SellingPrice      { get; set; }
        public decimal? TakeoutPrice     { get; set; }
        public decimal? DeliveryPrice    { get; set; }
        public decimal? RoomServicePrice { get; set; }

        // Recipe / BOM Header (from Recipes table)
        public int?   RecipeId           { get; set; }

        [Range(1, 100, ErrorMessage = "Portions served must be 1–100.")]
        [Display(Name = "Portions (Yield)")]
        public int    Yield            { get; set; } = 1;

        [Range(1, 100, ErrorMessage = "Yield % must be 1–100.")]
        [Display(Name = "Yield %")]
        public decimal YieldPercentage { get; set; } = 100;

        [Display(Name = "Prep Time (min)")]
        public int?   PrepTimeMinutes  { get; set; }

        public decimal? ComputedCost   { get; set; }
        public DateTime? LastCalculated { get; set; }

        // Ingredient lines
        public List<BOMLineViewModel> Lines { get; set; } = new();

        // Derived – margin for each price type
        public decimal? GrossMarginPct       => CalcMargin(SellingPrice);
        public decimal? TakeoutMarginPct     => CalcMargin(TakeoutPrice);
        public decimal? DeliveryMarginPct    => CalcMargin(DeliveryPrice);
        public decimal? RoomServiceMarginPct => CalcMargin(RoomServicePrice);

        private decimal? CalcMargin(decimal? price) =>
            (ComputedCost.HasValue && price.HasValue && price > 0)
                ? Math.Round((price.Value - ComputedCost.Value) / price.Value * 100, 2)
                : null;

        public string MarginBadgeClass => GrossMarginPct.HasValue
            ? (GrossMarginPct >= 60 ? "bg-success" :
               GrossMarginPct >= 40 ? "bg-warning text-dark" : "bg-danger")
            : "bg-secondary";

        public static string GetMarginClass(decimal? pct) => pct.HasValue
            ? (pct >= 60 ? "text-success" : pct >= 40 ? "text-warning" : "text-danger")
            : "text-secondary";

        public static string GetMarginBadgeClass(decimal? pct) => pct.HasValue
            ? (pct >= 60 ? "bg-success" : pct >= 40 ? "bg-warning text-dark" : "bg-danger")
            : "bg-secondary";
    }

    // ─────────────────────────────────────────────────────────
    //  One BOM Line  (MenuItemIngredients row + display fields)
    // ─────────────────────────────────────────────────────────
    public class BOMLineViewModel
    {
        public int    Id               { get; set; }   // MenuItemIngredients.Id
        public int    MenuItemId       { get; set; }

        [Required(ErrorMessage = "Select an ingredient.")]
        [Display(Name = "Ingredient")]
        public int    IngredientId     { get; set; }
        public string IngredientName   { get; set; } = "";
        public string? ItemCategory    { get; set; }

        [Required]
        [Range(0.001, 99999, ErrorMessage = "Qty must be > 0.")]
        [Display(Name = "Qty (Consumption UOM)")]
        public decimal Quantity        { get; set; }

        // UOM info (read-only display after ingredient selected)
        public int?   ConsumptionUOMId   { get; set; }
        public string? ConsumptionUOMCode { get; set; }
        public int?   PurchaseUOMId      { get; set; }
        public string? PurchaseUOMCode   { get; set; }
        public decimal? ConversionFactor { get; set; }   // RecipeUOM per 1 PurchaseUOM
        public decimal? StandardCost     { get; set; }   // cost per 1 PurchaseUOM

        [Display(Name = "Optional")]
        public bool   IsOptional       { get; set; }

        [StringLength(200)]
        public string? Instructions    { get; set; }

        // Computed display: Quantity ÷ ConversionFactor × StandardCost
        public decimal? LineCost =>
            (ConversionFactor.HasValue && ConversionFactor > 0 && StandardCost.HasValue)
                ? Math.Round(Quantity / ConversionFactor.Value * StandardCost.Value, 4)
                : null;
    }

    // ─────────────────────────────────────────────────────────
    //  AJAX save payload  (POST: SaveBOMLine)
    // ─────────────────────────────────────────────────────────
    public class SaveBOMLineRequest
    {
        public int    LineId        { get; set; }   // 0 = new
        public int    MenuItemId    { get; set; }
        public int    IngredientId  { get; set; }
        public decimal Quantity     { get; set; }
        public bool   IsOptional    { get; set; }
        public string? Instructions { get; set; }
    }

    // ─────────────────────────────────────────────────────────
    //  AJAX save-header payload  (POST: SaveBOMHeader)
    // ─────────────────────────────────────────────────────────
    public class SaveBOMHeaderRequest
    {
        public int     MenuItemId      { get; set; }
        public int     Yield           { get; set; } = 1;
        public decimal YieldPercentage { get; set; } = 100;
        public int?    PrepTimeMinutes { get; set; }
    }
}
