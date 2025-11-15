namespace RestaurantManagementSystem.Models.Authorization
{
    public class PermissionSet
    {
        public bool CanView { get; set; }
        public bool CanAdd { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool CanApprove { get; set; }
        public bool CanPrint { get; set; }
        public bool CanExport { get; set; }

        public static PermissionSet FullAccess => new()
        {
            CanView = true,
            CanAdd = true,
            CanEdit = true,
            CanDelete = true,
            CanApprove = true,
            CanPrint = true,
            CanExport = true
        };
    }
}
