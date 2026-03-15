using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.Models
{
    public class BranchLocation
    {
        public int LocationId { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Location Name")]
        public string LocationName { get; set; } = string.Empty;

        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;
    }
}
