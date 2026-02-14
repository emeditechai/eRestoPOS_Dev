using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.Models
{
    public class Recipe
    {
        public int Id { get; set; }
        public int? BranchId { get; set; }
        
        [Required]
        public int MenuItemId { get; set; }
        public MenuItem MenuItem { get; set; }
        
        [Required(ErrorMessage = "Recipe title is required")]
        [StringLength(100)]
        public string Title { get; set; }
        
        [Required(ErrorMessage = "Preparation instructions are required")]
        [DataType(DataType.MultilineText)]
        public string PreparationInstructions { get; set; }
        
        [Required(ErrorMessage = "Cooking instructions are required")]
        [DataType(DataType.MultilineText)]
        public string CookingInstructions { get; set; }
        
        [Display(Name = "Plating Instructions")]
        [DataType(DataType.MultilineText)]
        public string PlatingInstructions { get; set; }
        
        [Display(Name = "Yield (Servings)")]
        [Range(1, 100, ErrorMessage = "Yield must be between 1 and 100 servings")]
        public int Yield { get; set; } = 1;
        
        [Display(Name = "Yield Percentage")]
        [Range(0, 100, ErrorMessage = "Yield percentage must be between 0 and 100")]
        public decimal YieldPercentage { get; set; } = 100;
        
        [Display(Name = "Preparation Time (Minutes)")]
        [Range(1, 180, ErrorMessage = "Preparation time must be between 1 and 180 minutes")]
        public int PreparationTimeMinutes { get; set; }
        
        [Display(Name = "Cooking Time (Minutes)")]
        [Range(0, 360, ErrorMessage = "Cooking time must be between 0 and 360 minutes")]
        public int CookingTimeMinutes { get; set; }
        
        [Display(Name = "Last Updated")]
        [DataType(DataType.DateTime)]
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        
        [Display(Name = "Created By")]
        public int? CreatedById { get; set; }
        
        [Display(Name = "Notes")]
        [DataType(DataType.MultilineText)]
        public string Notes { get; set; }
        
        [Display(Name = "Is Archived")]
        public bool IsArchived { get; set; } = false;
        
        [Display(Name = "Menu Item Name")]
        public string MenuItemName { get; set; }
        
        [Display(Name = "Version")]
        public int Version { get; set; } = 1;
        
        // Navigation properties
        public virtual ICollection<RecipeStep> Steps { get; set; } = new List<RecipeStep>();
    }
}
