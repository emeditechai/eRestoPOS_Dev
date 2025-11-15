using System;

namespace RestaurantManagementSystem.Models.Authorization
{
    public class NavigationMenu
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string ParentCode { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Area { get; set; }
        public string ControllerName { get; set; }
        public string ActionName { get; set; }
        public string RouteValues { get; set; }
        public string CustomUrl { get; set; }
        public string IconCss { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
        public bool IsVisible { get; set; }
        public string ThemeColor { get; set; }
        public string ShortcutHint { get; set; }
        public bool OpenInNewTab { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
