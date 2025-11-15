namespace RestaurantManagementSystem.ViewModels.Authorization
{
    public class RolePermissionMatrixRow
    {
        public int MenuId { get; set; }
        public string MenuCode { get; set; }
        public string MenuName { get; set; }
        public string ParentCode { get; set; }
        public bool CanView { get; set; }
        public bool CanAdd { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool CanApprove { get; set; }
        public bool CanPrint { get; set; }
        public bool CanExport { get; set; }
    }
}
