using System.Collections.Generic;

namespace RestaurantManagementSystem.ViewModels.Authorization
{
    public class RoleMenuTreeNode
    {
        public int MenuId { get; set; }
        public string Code { get; set; }
        public string DisplayName { get; set; }
        public string ParentCode { get; set; }
        public bool IsAssigned { get; set; }
        public List<RoleMenuTreeNode> Children { get; set; } = new();
    }
}
