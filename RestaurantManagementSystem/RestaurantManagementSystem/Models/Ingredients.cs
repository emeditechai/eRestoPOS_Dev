using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantManagementSystem.Models
{
    /// <summary>
    /// Ingredient / Inventory Item Master.
    /// Each record represents a stockable ingredient that can be:
    ///   - Purchased (with a Purchase UOM)
    ///   - Stored in inventory
    ///   - Used in Menu Item recipes / BOM (with a Recipe UOM)
    /// </summary>
    public class Ingredients
    {
        // ── Core (original columns – never removed) ───────────────────────────
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public required string IngredientsName { get; set; }

        [StringLength(150)]
        public string? DisplayName { get; set; }

        [Required(ErrorMessage = "Item Code is required.")]
        [StringLength(20)]
        public string? Code { get; set; }

        // ── Item Master extensions ─────────────────────────────────────────────

        /// <summary>
        /// Item category for grouping.
        /// Values: Vegetable, Meat, Seafood, Spice, Dairy, Grain, Beverage, Packaging, Other
        /// </summary>
        [StringLength(50)]
        public string? ItemCategory { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>FK to UomMaster – how this ingredient is purchased (e.g. KG).</summary>
        public int? PurchaseUOMId { get; set; }

        /// <summary>FK to UomMaster – how this ingredient is measured in recipes/BOM (e.g. GRM).</summary>
        public int? RecipeUOMId { get; set; }

        /// <summary>
        /// Conversion factor from Purchase UOM to Recipe UOM.
        /// E.g. 1 KG (purchase) = 1000 GRM (recipe) → factor = 1000.
        /// Leave 1 if both UOMs are the same.
        /// </summary>
        [Column(TypeName = "decimal(18,6)")]
        public decimal? PurchaseToRecipeFactor { get; set; }

        /// <summary>Standard cost per 1 unit of Purchase UOM (for costing).</summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal? StandardCost { get; set; }

        /// <summary>Minimum stock level before reorder alert (in Recipe UOM).</summary>
        [Column(TypeName = "decimal(18,3)")]
        public decimal? ReorderLevel { get; set; }

        /// <summary>
        /// GST percentage applicable on this ingredient (e.g. 18 for 18%).
        /// For intra-state: CGST = GSTPercent/2, SGST = GSTPercent/2.
        /// For inter-state: IGST = GSTPercent.
        /// Default 0 = GST exempt.
        /// </summary>
        [Column(TypeName = "decimal(5,2)")]
        public decimal GSTPercent { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // ── NotMapped display helpers (populated in controller via JOIN) ───────
        [NotMapped] public string? PurchaseUOMCode { get; set; }
        [NotMapped] public string? PurchaseUOMName { get; set; }
        [NotMapped] public string? RecipeUOMCode { get; set; }
        [NotMapped] public string? RecipeUOMName { get; set; }

        // ── Navigation properties ─────────────────────────────────────────────
        public UomMaster? PurchaseUOM { get; set; }
        public UomMaster? RecipeUOM { get; set; }
    }
}
