using System;

namespace RestaurantManagementSystem.Models.Authorization
{
    public class RoleMenuPermission
    {
        public int RoleId { get; set; }
        public int MenuId { get; set; }
    public string MenuCode { get; set; } = string.Empty;
    public string MenuName { get; set; } = string.Empty;
    public string? ParentCode { get; set; }
        public bool CanView { get; set; }
        public bool CanAdd { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool CanApprove { get; set; }
        public bool CanPrint { get; set; }
        public bool CanExport { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
