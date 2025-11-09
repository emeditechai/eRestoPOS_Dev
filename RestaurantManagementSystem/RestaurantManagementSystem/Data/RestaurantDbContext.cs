using Microsoft.EntityFrameworkCore;
using RestaurantManagementSystem.Models;

namespace RestaurantManagementSystem.Data
{
    public class RestaurantDbContext : DbContext
    {
        public RestaurantDbContext(DbContextOptions<RestaurantDbContext> options) 
            : base(options)
        {
        }

        // Only include model types that we're sure exist
        public DbSet<Category> Categories { get; set; } = null!;
        public DbSet<SubCategory> SubCategories { get; set; } = null!;
        public DbSet<Ingredients> Ingredients { get; set; } = null!;
        public DbSet<Table> Tables { get; set; } = null!;
        public DbSet<Reservation> Reservations { get; set; } = null!;
        
        // User Management
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Role> Roles { get; set; } = null!;
        public DbSet<UserRoleAssignment> UserRoles { get; set; } = null!;
        
        // Menu and Recipe Management
        public DbSet<MenuItem> MenuItems { get; set; } = null!;
    public DbSet<MenuItemGroup> MenuItemGroups { get; set; } = null!;
        public DbSet<Allergen> Allergens { get; set; } = null!;
        public DbSet<MenuItemAllergen> MenuItemAllergens { get; set; } = null!;
        public DbSet<Modifier> Modifiers { get; set; } = null!;
        public DbSet<MenuItemModifier> MenuItemModifiers { get; set; } = null!;
        public DbSet<MenuItemIngredient> MenuItemIngredients { get; set; } = null!;
        public DbSet<Recipe> Recipes { get; set; } = null!;
        public DbSet<RecipeStep> RecipeSteps { get; set; } = null!;
        
        // Restaurant Settings
        public DbSet<RestaurantSettings> RestaurantSettings { get; set; } = null!;
        
        // Day Closing
        public DbSet<CashierDayOpening> CashierDayOpenings { get; set; } = null!;
        public DbSet<CashierDayClose> CashierDayClosings { get; set; } = null!;
        public DbSet<DayLockAudit> DayLockAudits { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Set default schema to dbo for all entities
            modelBuilder.HasDefaultSchema("dbo");
            
            // Configure Category entity
            modelBuilder.Entity<Category>(entity => 
            {
                entity.ToTable("Categories", "dbo");
                entity.Property(e => e.Id).HasColumnName("Id").IsRequired();
                entity.Property(e => e.Name).HasColumnName("Name").IsRequired();
                entity.Property(e => e.IsActive).HasColumnName("IsActive").IsRequired();
                
                // Ignore CategoryName for database operations
                entity.Ignore(e => e.CategoryName);
            });
            
            // Configure SubCategory entity
            modelBuilder.Entity<SubCategory>(entity =>
            {
                entity.ToTable("SubCategories", "dbo");
                entity.Property(e => e.Id).HasColumnName("Id").IsRequired();
                entity.Property(e => e.Name).HasColumnName("Name").IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).HasColumnName("Description").HasMaxLength(500);
                entity.Property(e => e.IsActive).HasColumnName("IsActive").IsRequired();
                entity.Property(e => e.CategoryId).HasColumnName("CategoryId").IsRequired();
                entity.Property(e => e.DisplayOrder).HasColumnName("DisplayOrder");
                entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt").IsRequired();
                entity.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt");
                
                // Configure foreign key relationship
                entity.HasOne(s => s.Category)
                      .WithMany(c => c.SubCategories)
                      .HasForeignKey(s => s.CategoryId)
                      .OnDelete(DeleteBehavior.Restrict);
                
                // Ignore SubCategoryName for database operations
                entity.Ignore(e => e.SubCategoryName);
            });
            
            // Configure MenuItem entity
            modelBuilder.Entity<MenuItem>(entity =>
            {
                entity.ToTable("MenuItems", "dbo");
                
                // Configure foreign key relationship with Category
                entity.HasOne(m => m.Category)
                      .WithMany()
                      .HasForeignKey(m => m.CategoryId)
                      .OnDelete(DeleteBehavior.Restrict);
                      
                // Configure foreign key relationship with SubCategory (optional)
                entity.HasOne(m => m.SubCategory)
                      .WithMany()
                      .HasForeignKey(m => m.SubCategoryId)
                      .OnDelete(DeleteBehavior.SetNull)
                      .IsRequired(false);
            });

            // Configure MenuItemGroup entity to match existing dbo.menuitemgroup table
            modelBuilder.Entity<RestaurantManagementSystem.Models.MenuItemGroup>(entity =>
            {
                entity.ToTable("menuitemgroup", "dbo");
                entity.HasKey(e => e.ID);
                entity.Property(e => e.ID).HasColumnName("ID");
                entity.Property(e => e.ItemGroup).HasColumnName("itemgroup").HasMaxLength(20);
                entity.Property(e => e.IsActive).HasColumnName("is_active");
                entity.Property(e => e.GST_Perc).HasColumnName("GST_Perc").HasColumnType("numeric(12,2)");
            });

            // Configure MenuItemGroup entity to match existing table
            modelBuilder.Entity<MenuItemGroup>(entity =>
            {
                entity.ToTable("menuitemgroup", "dbo");
                entity.HasKey(e => e.ID);
                entity.Property(e => e.ID).HasColumnName("ID").IsRequired();
                entity.Property(e => e.ItemGroup).HasColumnName("itemgroup").HasMaxLength(20);
                entity.Property(e => e.IsActive).HasColumnName("is_active");
                entity.Property(e => e.GST_Perc).HasColumnName("GST_Perc").HasColumnType("numeric(12,2)");
            });
            
            // Seed data for Categories
            modelBuilder.Entity<Category>().HasData(
                new Category { Id = 1, Name = "Appetizers", IsActive = true },
                new Category { Id = 2, Name = "Main Course", IsActive = true },
                new Category { Id = 3, Name = "Desserts", IsActive = true },
                new Category { Id = 4, Name = "Beverages", IsActive = true }
            );
            
            // Seed data for SubCategories
            var seedDate = new DateTime(2024, 1, 1, 12, 0, 0);
            modelBuilder.Entity<SubCategory>().HasData(
                new SubCategory { Id = 1, Name = "Hot Appetizers", Description = "Warm appetizer dishes", CategoryId = 1, IsActive = true, DisplayOrder = 1, CreatedAt = seedDate, UpdatedAt = null },
                new SubCategory { Id = 2, Name = "Cold Appetizers", Description = "Cold appetizer dishes", CategoryId = 1, IsActive = true, DisplayOrder = 2, CreatedAt = seedDate, UpdatedAt = null },
                new SubCategory { Id = 3, Name = "Meat Dishes", Description = "Meat-based main courses", CategoryId = 2, IsActive = true, DisplayOrder = 1, CreatedAt = seedDate, UpdatedAt = null },
                new SubCategory { Id = 4, Name = "Vegetarian Dishes", Description = "Vegetarian main courses", CategoryId = 2, IsActive = true, DisplayOrder = 2, CreatedAt = seedDate, UpdatedAt = null },
                new SubCategory { Id = 5, Name = "Cakes", Description = "Various types of cakes", CategoryId = 3, IsActive = true, DisplayOrder = 1, CreatedAt = seedDate, UpdatedAt = null },
                new SubCategory { Id = 6, Name = "Ice Cream", Description = "Ice cream desserts", CategoryId = 3, IsActive = true, DisplayOrder = 2, CreatedAt = seedDate, UpdatedAt = null },
                new SubCategory { Id = 7, Name = "Hot Beverages", Description = "Coffee, tea, hot chocolate", CategoryId = 4, IsActive = true, DisplayOrder = 1, CreatedAt = seedDate, UpdatedAt = null },
                new SubCategory { Id = 8, Name = "Cold Beverages", Description = "Juices, sodas, iced drinks", CategoryId = 4, IsActive = true, DisplayOrder = 2, CreatedAt = seedDate, UpdatedAt = null }
            );

            // Seed data for Ingredients
            modelBuilder.Entity<Ingredients>().HasData(
                new Ingredients { Id = 1, IngredientsName = "Tomato", DisplayName = "Tomato", Code = "TMT" },
                new Ingredients { Id = 2, IngredientsName = "Cheese", DisplayName = "Cheese", Code = "CHS" },
                new Ingredients { Id = 3, IngredientsName = "Chicken", DisplayName = "Chicken", Code = "CHK" },
                new Ingredients { Id = 4, IngredientsName = "Basil", DisplayName = "Basil", Code = "BSL" }
            );

            // Seed data for Tables
            modelBuilder.Entity<Table>().HasData(
                new Table { Id = 1, TableNumber = "T1", Capacity = 4, Status = TableStatus.Available, IsActive = true },
                new Table { Id = 2, TableNumber = "T2", Capacity = 2, Status = TableStatus.Occupied, IsActive = true },
                new Table { Id = 3, TableNumber = "T3", Capacity = 6, Status = TableStatus.Available, IsActive = true },
                new Table { Id = 4, TableNumber = "T4", Capacity = 8, Status = TableStatus.Reserved, IsActive = true }
            );

            // Seed data for Reservations
            modelBuilder.Entity<Reservation>().HasData(
                new Reservation { 
                    Id = 1, 
                    ReservationDate = DateTime.Today, 
                    ReservationTime = DateTime.Today.AddHours(19),
                    PartySize = 4, 
                    GuestName = "John Smith", 
                    PhoneNumber = "555-1234", 
                    Status = ReservationStatus.Confirmed, 
                    TableId = 4 
                },
                new Reservation { 
                    Id = 2, 
                    ReservationDate = DateTime.Today.AddDays(1), 
                    ReservationTime = DateTime.Today.AddDays(1).AddHours(18).AddMinutes(30),
                    PartySize = 2, 
                    GuestName = "Mary Johnson", 
                    PhoneNumber = "555-5678", 
                    Status = ReservationStatus.Pending 
                }
            );
            
            base.OnModelCreating(modelBuilder);
        }
    }
}
