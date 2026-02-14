namespace RestaurantManagementSystem.Models
{
    public class Ingredients
    {
        public int Id { get; set; }
        public int? BranchId { get; set; }
        public required string IngredientsName { get; set; }
        public string? DisplayName { get; set; }
        public string? Code { get; set; }
    }
}