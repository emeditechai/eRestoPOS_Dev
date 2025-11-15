using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.ViewModels.Authorization
{
    public class MenuEditViewModel
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(60)]
        [Display(Name = "Menu Code")]
        public string Code { get; set; }

        [Display(Name = "Parent Code")]
        [StringLength(60)]
        public string ParentCode { get; set; }

        [Required]
        [StringLength(120)]
        [Display(Name = "Display Name")]
        public string DisplayName { get; set; }

        [StringLength(255)]
        public string Description { get; set; }

        [StringLength(80)]
        public string Area { get; set; }

        [StringLength(120)]
        [Display(Name = "Controller")]
        public string ControllerName { get; set; }

        [StringLength(120)]
        [Display(Name = "Action")]
        public string ActionName { get; set; }

        [StringLength(200)]
        public string RouteValues { get; set; }

        [Display(Name = "Custom URL")]
        [StringLength(400)]
        public string CustomUrl { get; set; }

        [StringLength(100)]
        [Display(Name = "Icon CSS")]
        public string IconCss { get; set; }

        [Display(Name = "Display Order")]
        public int DisplayOrder { get; set; } = 1;

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Visible")]
        public bool IsVisible { get; set; } = true;

        [Display(Name = "Theme Color")] 
        [StringLength(30)]
        public string ThemeColor { get; set; }

        [Display(Name = "Shortcut Hint")]
        [StringLength(40)]
        public string ShortcutHint { get; set; }

        [Display(Name = "Open in new tab")]
        public bool OpenInNewTab { get; set; }
    }
}
