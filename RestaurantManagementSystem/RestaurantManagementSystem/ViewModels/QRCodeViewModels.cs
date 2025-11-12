using RestaurantManagementSystem.Models;

namespace RestaurantManagementSystem.ViewModels
{
    public class QRCodeGenerationViewModel
    {
        public string QRCodeBase64 { get; set; } = string.Empty;
        public string QRCodeUrl { get; set; } = string.Empty;
        public int? TableId { get; set; }
        public string TableName { get; set; } = string.Empty;
        public QRCodeType CodeType { get; set; } = QRCodeType.GeneralMenu;
        public string RestaurantName { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
    }

    public class PublicMenuViewModel
    {
        public RestaurantInfo RestaurantInfo { get; set; } = new();
        // Backwards-compatible: still populate when needed
        public List<CategoryMenuItems> MenuCategories { get; set; } = new();
        // New: Group -> Category -> SubCategory -> Items
        public List<MenuGroupViewModel> MenuGroups { get; set; } = new();
        public int? TableId { get; set; }
        public string TableName { get; set; } = string.Empty;
        public bool IsTableSpecific { get; set; }
    }

    public class MenuGroupViewModel
    {
        public int? GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public List<CategoryMenuItems> Categories { get; set; } = new();
    }

    public class CategoryMenuItems
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string CategoryDescription { get; set; } = string.Empty;
        public List<PublicMenuItem> MenuItems { get; set; } = new();
        public List<SubCategoryMenuItems> SubCategories { get; set; } = new();
    }

    public class SubCategoryMenuItems
    {
        public int? SubCategoryId { get; set; }
        public string SubCategoryName { get; set; } = string.Empty;
        public List<PublicMenuItem> MenuItems { get; set; } = new();
    }

    public class PublicMenuItem
    {
        public int MenuItemId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsAvailable { get; set; }
        public bool IsVegetarian { get; set; }
        public bool IsSpicy { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string? SubCategoryName { get; set; } = string.Empty;
    }

    public class RestaurantInfo
    {
        public string Name { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
    }

    public class QRCodeManagementViewModel
    {
        public List<QRCodeItem> QRCodes { get; set; } = new();
        public List<Table> Tables { get; set; } = new();
        public RestaurantInfo RestaurantInfo { get; set; } = new();
    }

    public class QRCodeItem
    {
        public int Id { get; set; }
        public string QRCodeBase64 { get; set; } = string.Empty;
        public string QRCodeUrl { get; set; } = string.Empty;
        public QRCodeType CodeType { get; set; }
        public int? TableId { get; set; }
        public string TableName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
    }

    public enum QRCodeType
    {
        GeneralMenu = 0,
        TableSpecific = 1,
        Takeaway = 2,
        Delivery = 3
    }
}