using System.Collections.Generic;

namespace RestaurantManagementSystem.Models.Authorization
{
    public class NavigationMenuNode
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
        public string ThemeColor { get; set; }
        public string ShortcutHint { get; set; }
        public bool OpenInNewTab { get; set; }
        public bool CanView { get; set; }
        public bool HasChildren => Children.Count > 0;
        public List<NavigationMenuNode> Children { get; set; } = new();
    }
}
