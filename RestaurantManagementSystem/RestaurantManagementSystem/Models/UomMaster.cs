using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantManagementSystem.Models
{
    /// <summary>
    /// Unit of Measurement Master used in Bill of Material (BOM) for restaurant ingredients.
    /// Supports a self-referencing hierarchy so derived UOMs (e.g., GRM) point to a base UOM (e.g., KG).
    /// </summary>
    [Table("UomMaster", Schema = "dbo")]
    public class UomMaster
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int UOMId { get; set; }

        /// <summary>Short code used in recipes and purchase orders. E.g. KG, GRM, LTR, ML, PCS.</summary>
        [Required]
        [StringLength(15)]
        public string UOMCode { get; set; } = string.Empty;

        /// <summary>Full descriptive name. E.g. Kilogram, Gram, Litre, Millilitre, Pieces.</summary>
        [Required]
        [StringLength(100)]
        public string UOMName { get; set; } = string.Empty;

        /// <summary>
        /// Measurement category: Weight | Volume | Count | Other
        /// </summary>
        [Required]
        [StringLength(20)]
        public string UOMType { get; set; } = "Count";

        /// <summary>
        /// Points to the base UOM for this type. NULL means this IS the base unit.
        /// E.g. GRM.BaseUOMId → KG's UOMId.
        /// </summary>
        public int? BaseUOMId { get; set; }

        /// <summary>
        /// How many of this UOM equal ONE of the base UOM.
        /// E.g. GRM → BaseUOM = KG, ConversionFactor = 0.001 (1 GRM = 0.001 KG).
        /// Base units themselves have ConversionFactor = 1.
        /// </summary>
        [Column(TypeName = "decimal(18,6)")]
        public decimal ConversionFactor { get; set; } = 1m;

        /// <summary>Optional purchase pack size. E.g. 50 for a 50-KG sack.</summary>
        [Column(TypeName = "decimal(18,3)")]
        public decimal? PackSize { get; set; }

        /// <summary>Decimal places to show on quantity entry screens for this UOM.</summary>
        public int DecimalPlaces { get; set; } = 3;

        [StringLength(300)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // ── Navigation properties ──────────────────────────────────────────────
        [ForeignKey(nameof(BaseUOMId))]
        public virtual UomMaster? BaseUOM { get; set; }

        // Not mapped to DB – used in views for display
        [NotMapped]
        public string? BaseUOMCode { get; set; }
        [NotMapped]
        public string? BaseUOMName { get; set; }
    }
}
