# UOM (Unit of Measurement) Implementation Plan for Menu Item Master

## 📋 Overview
This document provides a comprehensive step-by-step implementation plan for adding UOM (Unit of Measurement) functionality to the Menu Item Master system. The implementation will support beverage serving sizes, stock management, and inventory calculations.

---

## 🗂️ Database Structure

### **Phase 1: UOM Master Table Creation**

#### **Table: `tbl_mst_uom`**

```sql
CREATE TABLE [dbo].[tbl_mst_uom] (
    [UOM_Id] INT IDENTITY(1,1) PRIMARY KEY,
    [UOM_Name] NVARCHAR(50) NOT NULL,
    [UOM_Type] NVARCHAR(50) NOT NULL,
    [Base_Quantity_ML] DECIMAL(10,2) NOT NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedBy] INT NULL,
    [CreatedDate] DATETIME NOT NULL DEFAULT GETDATE(),
    [UpdatedDate] DATETIME NULL,
    [UpdatedBy] INT NULL,
    
    CONSTRAINT [UQ_tbl_mst_uom_Name] UNIQUE ([UOM_Name]),
    CONSTRAINT [CHK_tbl_mst_uom_BaseQuantity] CHECK ([Base_Quantity_ML] > 0)
);
```

**Column Details:**
- **UOM_Id**: Primary key with auto-increment
- **UOM_Name**: Display name (e.g., "30 ml Peg", "Pint", "750ml Bottle")
- **UOM_Type**: Category for grouping (Peg/Bottle/Beer/Wine/Cocktail/SoftDrink/Food)
- **Base_Quantity_ML**: Standard quantity in milliliters for calculation
- **IsActive**: Soft delete flag
- **CreatedBy**: User ID who created the UOM
- **CreatedDate**: Creation timestamp
- **UpdatedDate**: Last modification timestamp
- **UpdatedBy**: User ID who last modified

#### **Sample UOM Data**

```sql
-- Insert sample UOM data
INSERT INTO [dbo].[tbl_mst_uom] ([UOM_Name], [UOM_Type], [Base_Quantity_ML], [IsActive], [CreatedBy], [CreatedDate])
VALUES
    -- Spirits/Liquor Pegs
    ('30 ml', 'Peg', 30.00, 1, 1, GETDATE()),
    ('60 ml', 'Peg', 60.00, 1, 1, GETDATE()),
    ('90 ml', 'Peg', 90.00, 1, 1, GETDATE()),
    
    -- Standard Bottles
    ('180 ml', 'Bottle', 180.00, 1, 1, GETDATE()),
    ('375 ml', 'Bottle', 375.00, 1, 1, GETDATE()),
    ('750 ml', 'Bottle', 750.00, 1, 1, GETDATE()),
    
    -- Beer
    ('Pint', 'Beer', 330.00, 1, 1, GETDATE()),
    ('Pitcher', 'Beer', 1500.00, 1, 1, GETDATE()),
    
    -- Wine
    ('Glass', 'Wine', 150.00, 1, 1, GETDATE()),
    ('Bottle', 'Wine', 750.00, 1, 1, GETDATE());
```

#### **Example Data Table**

| UOM_Id | UOM_Name | UOM_Type | Base_Quantity_ML |
|--------|----------|----------|------------------|
| 1      | 30 ml    | Peg      | 30               |
| 2      | 60 ml    | Peg      | 60               |
| 3      | 90 ml    | Peg      | 90               |
| 4      | 180 ml   | Bottle   | 180              |
| 5      | 375 ml   | Bottle   | 375              |
| 6      | 750 ml   | Bottle   | 750              |
| 7      | Pint     | Beer     | 330              |
| 8      | Pitcher  | Beer     | 1500             |
| 9      | Glass    | Wine     | 150              |
| 10     | Bottle   | Wine     | 750              |

---

### **Phase 2: MenuItems Table Modification**

#### **Add UOM_Id Column to MenuItems**

```sql
-- Add UOM_Id column to MenuItems table
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo' 
    AND TABLE_NAME = 'MenuItems' 
    AND COLUMN_NAME = 'UOM_Id'
)
BEGIN
    ALTER TABLE [dbo].[MenuItems] 
    ADD [UOM_Id] INT NULL;
    
    PRINT 'UOM_Id column added to MenuItems table successfully.';
END
ELSE
BEGIN
    PRINT 'UOM_Id column already exists in MenuItems table.';
END
GO

-- Add foreign key constraint
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys 
    WHERE name = 'FK_MenuItems_UOM'
)
BEGIN
    ALTER TABLE [dbo].[MenuItems]
    ADD CONSTRAINT [FK_MenuItems_UOM]
    FOREIGN KEY ([UOM_Id])
    REFERENCES [dbo].[tbl_mst_uom]([UOM_Id]);
    
    PRINT 'Foreign key constraint FK_MenuItems_UOM added successfully.';
END
ELSE
BEGIN
    PRINT 'Foreign key constraint FK_MenuItems_UOM already exists.';
END
GO

-- Create index for better performance
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_MenuItems_UOM_Id'
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_MenuItems_UOM_Id]
    ON [dbo].[MenuItems]([UOM_Id])
    INCLUDE ([Name], [Price], [IsAvailable]);
    
    PRINT 'Index IX_MenuItems_UOM_Id created successfully.';
END
ELSE
BEGIN
    PRINT 'Index IX_MenuItems_UOM_Id already exists.';
END
GO
```

---

## 📝 Implementation Steps (Detailed)

### **STEP 1: Database Setup**

#### **1.1 Create UOM Master Table SQL Script**

**File**: `RestaurantManagementSystem/SQL/create_uom_master_table.sql`

```sql
-- =============================================
-- Script: Create UOM Master Table
-- Description: Creates tbl_mst_uom table for Unit of Measurement master data
-- Author: Restaurant Management System
-- Date: November 2025
-- =============================================

USE [dev_Restaurant]
GO

-- Drop table if exists (for development only)
-- DROP TABLE IF EXISTS [dbo].[tbl_mst_uom];

-- Create UOM Master Table
CREATE TABLE [dbo].[tbl_mst_uom] (
    [UOM_Id] INT IDENTITY(1,1) NOT NULL,
    [UOM_Name] NVARCHAR(50) NOT NULL,
    [UOM_Type] NVARCHAR(50) NOT NULL,
    [Base_Quantity_ML] DECIMAL(10,2) NOT NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedBy] INT NULL,
    [CreatedDate] DATETIME NOT NULL DEFAULT GETDATE(),
    [UpdatedDate] DATETIME NULL,
    [UpdatedBy] INT NULL,
    
    CONSTRAINT [PK_tbl_mst_uom] PRIMARY KEY CLUSTERED ([UOM_Id] ASC),
    CONSTRAINT [UQ_tbl_mst_uom_Name] UNIQUE ([UOM_Name]),
    CONSTRAINT [CHK_tbl_mst_uom_BaseQuantity] CHECK ([Base_Quantity_ML] > 0),
    CONSTRAINT [CHK_tbl_mst_uom_Type] CHECK ([UOM_Type] IN ('Peg', 'Bottle', 'Beer', 'Wine', 'Cocktail', 'SoftDrink', 'Food', 'Other'))
);
GO

PRINT 'tbl_mst_uom table created successfully!';
GO
```

#### **1.2 Insert Sample UOM Data**

**File**: `RestaurantManagementSystem/SQL/insert_uom_sample_data.sql`

```sql
-- =============================================
-- Script: Insert Sample UOM Data
-- Description: Inserts common UOM records for beverages and food
-- =============================================

USE [dev_Restaurant]
GO

-- Check if data already exists
IF (SELECT COUNT(*) FROM [dbo].[tbl_mst_uom]) = 0
BEGIN
    PRINT 'Inserting sample UOM data...';
    
    INSERT INTO [dbo].[tbl_mst_uom] ([UOM_Name], [UOM_Type], [Base_Quantity_ML], [IsActive], [CreatedBy], [CreatedDate])
    VALUES
        -- Spirits/Liquor Pegs
        ('30 ml', 'Peg', 30.00, 1, 1, GETDATE()),
        ('60 ml', 'Peg', 60.00, 1, 1, GETDATE()),
        ('90 ml', 'Peg', 90.00, 1, 1, GETDATE()),
        
        -- Standard Bottles
        ('180 ml', 'Bottle', 180.00, 1, 1, GETDATE()),
        ('375 ml', 'Bottle', 375.00, 1, 1, GETDATE()),
        ('750 ml', 'Bottle', 750.00, 1, 1, GETDATE()),
        
        -- Beer
        ('Pint', 'Beer', 330.00, 1, 1, GETDATE()),
        ('Pitcher', 'Beer', 1500.00, 1, 1, GETDATE()),
        
        -- Wine
        ('Glass', 'Wine', 150.00, 1, 1, GETDATE()),
        ('Bottle', 'Wine', 750.00, 1, 1, GETDATE());
    
    PRINT 'Sample UOM data inserted successfully!';
    PRINT 'Total records inserted: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
END
ELSE
BEGIN
    PRINT 'UOM data already exists. Skipping insert.';
    PRINT 'Current record count: ' + CAST((SELECT COUNT(*) FROM [dbo].[tbl_mst_uom]) AS NVARCHAR(10));
END
GO
```

#### **1.3 Add UOM_Id to MenuItems Table**

**File**: `RestaurantManagementSystem/SQL/add_uom_to_menuitems.sql`

```sql
-- =============================================
-- Script: Add UOM_Id to MenuItems
-- Description: Adds UOM_Id column and foreign key to MenuItems table
-- =============================================

USE [dev_Restaurant]
GO

-- Step 1: Add UOM_Id column if it doesn't exist
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo' 
    AND TABLE_NAME = 'MenuItems' 
    AND COLUMN_NAME = 'UOM_Id'
)
BEGIN
    ALTER TABLE [dbo].[MenuItems] 
    ADD [UOM_Id] INT NULL;
    
    PRINT 'UOM_Id column added to MenuItems table successfully.';
END
ELSE
BEGIN
    PRINT 'UOM_Id column already exists in MenuItems table.';
END
GO

-- Step 2: Add foreign key constraint
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys 
    WHERE name = 'FK_MenuItems_UOM'
)
BEGIN
    ALTER TABLE [dbo].[MenuItems]
    ADD CONSTRAINT [FK_MenuItems_UOM]
    FOREIGN KEY ([UOM_Id])
    REFERENCES [dbo].[tbl_mst_uom]([UOM_Id])
    ON DELETE SET NULL;  -- If UOM is deleted, set MenuItems.UOM_Id to NULL
    
    PRINT 'Foreign key constraint FK_MenuItems_UOM added successfully.';
END
ELSE
BEGIN
    PRINT 'Foreign key constraint FK_MenuItems_UOM already exists.';
END
GO

-- Step 3: Create index for better query performance
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_MenuItems_UOM_Id' 
    AND object_id = OBJECT_ID('[dbo].[MenuItems]')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_MenuItems_UOM_Id]
    ON [dbo].[MenuItems]([UOM_Id])
    INCLUDE ([Name], [Price], [IsAvailable]);
    
    PRINT 'Index IX_MenuItems_UOM_Id created successfully.';
END
ELSE
BEGIN
    PRINT 'Index IX_MenuItems_UOM_Id already exists.';
END
GO

-- Step 4: Set default UOM for existing beverage items (optional migration)
/*
-- Example: Set default "Per Plate" UOM for existing food items
UPDATE [dbo].[MenuItems]
SET [UOM_Id] = (SELECT TOP 1 UOM_Id FROM [dbo].[tbl_mst_uom] WHERE UOM_Name = 'Per Plate')
WHERE [UOM_Id] IS NULL 
AND [menuitemgroupID] = (SELECT ID FROM [dbo].[menuitemgroup] WHERE itemgroup = 'Foods');

-- Example: Set default "30 ml Peg" for existing bar items
UPDATE [dbo].[MenuItems]
SET [UOM_Id] = (SELECT TOP 1 UOM_Id FROM [dbo].[tbl_mst_uom] WHERE UOM_Name = '30 ml Peg')
WHERE [UOM_Id] IS NULL 
AND [menuitemgroupID] = (SELECT ID FROM [dbo].[menuitemgroup] WHERE itemgroup = 'Bar');
*/

PRINT 'UOM integration with MenuItems completed successfully!';
GO
```

---

### **STEP 2: C# Model Classes**

#### **2.1 Create UOM Model**

**File**: `RestaurantManagementSystem/Models/UOM.cs`

```csharp
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantManagementSystem.Models
{
    [Table("tbl_mst_uom")]
    public class UOM
    {
        [Key]
        [Column("UOM_Id")]
        public int Id { get; set; }

        [Required(ErrorMessage = "UOM Name is required")]
        [StringLength(50, ErrorMessage = "UOM Name cannot exceed 50 characters")]
        [Column("UOM_Name")]
        public string Name { get; set; }

        [Required(ErrorMessage = "UOM Type is required")]
        [StringLength(50, ErrorMessage = "UOM Type cannot exceed 50 characters")]
        [Column("UOM_Type")]
        public string Type { get; set; }

        [Required(ErrorMessage = "Base Quantity is required")]
        [Range(0.01, 999999.99, ErrorMessage = "Base Quantity must be greater than 0")]
        [Column("Base_Quantity_ML")]
        public decimal BaseQuantityML { get; set; }

        [Column("IsActive")]
        public bool IsActive { get; set; } = true;

        [Column("CreatedBy")]
        public int? CreatedBy { get; set; }

        [Column("CreatedDate")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [Column("UpdatedDate")]
        public DateTime? UpdatedDate { get; set; }

        [Column("UpdatedBy")]
        public int? UpdatedBy { get; set; }

        // Navigation property
        public virtual ICollection<MenuItem> MenuItems { get; set; }

        // Display helpers
        [NotMapped]
        public string DisplayName => $"{Name} ({BaseQuantityML} ml)";

        [NotMapped]
        public string TypeBadge => Type switch
        {
            "Peg" => "badge bg-warning",
            "Bottle" => "badge bg-primary",
            "Beer" => "badge bg-info",
            "Wine" => "badge bg-danger",
            "Cocktail" => "badge bg-success",
            "SoftDrink" => "badge bg-secondary",
            "Food" => "badge bg-dark",
            _ => "badge bg-light text-dark"
        };
    }

    // Enum for UOM Types
    public enum UOMType
    {
        Peg,
        Bottle,
        Beer,
        Wine,
        Cocktail,
        SoftDrink,
        Food,
        Other
    }
}
```

#### **2.2 Update MenuItem Model**

**File**: `RestaurantManagementSystem/Models/MenuItem.cs` (Add UOM property)

```csharp
// Add this property to existing MenuItem class
[Column("UOM_Id")]
public int? UOMId { get; set; }

[ForeignKey("UOMId")]
public virtual UOM UOM { get; set; }

// Display helper
[NotMapped]
public string DisplayWithUOM => UOM != null ? $"{Name} ({UOM.Name})" : Name;
```

---

### **STEP 3: ViewModels**

#### **3.1 UOM ViewModel**

**File**: `RestaurantManagementSystem/ViewModels/UOMViewModel.cs`

```csharp
using System;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagementSystem.ViewModels
{
    public class UOMViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "UOM Name is required")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "UOM Name must be between 2 and 50 characters")]
        [Display(Name = "UOM Name")]
        public string Name { get; set; }

        [Required(ErrorMessage = "UOM Type is required")]
        [Display(Name = "UOM Type")]
        public string Type { get; set; }

        [Required(ErrorMessage = "Base Quantity is required")]
        [Range(0.01, 999999.99, ErrorMessage = "Base Quantity must be between 0.01 and 999999.99")]
        [Display(Name = "Base Quantity (ml)")]
        public decimal BaseQuantityML { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Created By")]
        public int? CreatedBy { get; set; }

        [Display(Name = "Created Date")]
        public DateTime? CreatedDate { get; set; }

        [Display(Name = "Updated Date")]
        public DateTime? UpdatedDate { get; set; }

        [Display(Name = "Updated By")]
        public int? UpdatedBy { get; set; }

        // Additional properties for display
        public string CreatedByName { get; set; }
        public string UpdatedByName { get; set; }
        public int MenuItemCount { get; set; } // Count of menu items using this UOM
    }

    public class UOMListViewModel
    {
        public List<UOMViewModel> UOMs { get; set; } = new();
        public string SearchTerm { get; set; }
        public string FilterType { get; set; }
        public bool ShowInactive { get; set; }
        
        // Statistics
        public int TotalUOMs { get; set; }
        public int ActiveUOMs { get; set; }
        public int InactiveUOMs { get; set; }
        public Dictionary<string, int> UOMsByType { get; set; } = new();
    }
}
```

#### **3.2 Update MenuItemViewModel**

**File**: `RestaurantManagementSystem/ViewModels/MenuItemViewModel.cs` (Add UOM property)

```csharp
// Add these properties to existing MenuItemViewModel class

[Display(Name = "Unit of Measurement")]
public int? UOMId { get; set; }

public string UOMName { get; set; }

public decimal? UOMBaseQuantity { get; set; }

// For dropdown population
public List<SelectListItem> UOMList { get; set; } = new();
```

---

### **STEP 4: Controller Implementation**

#### **4.1 UOM Controller**

**File**: `RestaurantManagementSystem/Controllers/UOMController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagementSystem.ViewModels;
using System.Data;

namespace RestaurantManagementSystem.Controllers
{
    public class UOMController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public UOMController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        // GET: UOM/Index
        public IActionResult Index(string searchTerm = "", string filterType = "", bool showInactive = false)
        {
            var model = new UOMListViewModel
            {
                SearchTerm = searchTerm,
                FilterType = filterType,
                ShowInactive = showInactive
            };

            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                // Build dynamic query
                var whereClause = "WHERE 1=1";
                
                if (!showInactive)
                    whereClause += " AND IsActive = 1";
                
                if (!string.IsNullOrEmpty(searchTerm))
                    whereClause += " AND (UOM_Name LIKE @SearchTerm OR UOM_Type LIKE @SearchTerm)";
                
                if (!string.IsNullOrEmpty(filterType))
                    whereClause += " AND UOM_Type = @FilterType";

                var query = $@"
                    SELECT 
                        u.UOM_Id,
                        u.UOM_Name,
                        u.UOM_Type,
                        u.Base_Quantity_ML,
                        u.IsActive,
                        u.CreatedBy,
                        u.CreatedDate,
                        u.UpdatedDate,
                        u.UpdatedBy,
                        ISNULL(creator.FirstName + ' ' + creator.LastName, 'System') AS CreatedByName,
                        ISNULL(updater.FirstName + ' ' + updater.LastName, '') AS UpdatedByName,
                        (SELECT COUNT(*) FROM MenuItems WHERE UOM_Id = u.UOM_Id) AS MenuItemCount
                    FROM tbl_mst_uom u
                    LEFT JOIN Users creator ON u.CreatedBy = creator.Id
                    LEFT JOIN Users updater ON u.UpdatedBy = updater.Id
                    {whereClause}
                    ORDER BY u.UOM_Type, u.UOM_Name";

                using (var command = new SqlCommand(query, connection))
                {
                    if (!string.IsNullOrEmpty(searchTerm))
                        command.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");
                    
                    if (!string.IsNullOrEmpty(filterType))
                        command.Parameters.AddWithValue("@FilterType", filterType);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            model.UOMs.Add(new UOMViewModel
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                Type = reader.GetString(2),
                                BaseQuantityML = reader.GetDecimal(3),
                                IsActive = reader.GetBoolean(4),
                                CreatedBy = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                                CreatedDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                                UpdatedDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                                UpdatedBy = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                                CreatedByName = reader.GetString(9),
                                UpdatedByName = reader.GetString(10),
                                MenuItemCount = reader.GetInt32(11)
                            });
                        }
                    }
                }

                // Get statistics
                model.TotalUOMs = model.UOMs.Count;
                model.ActiveUOMs = model.UOMs.Count(u => u.IsActive);
                model.InactiveUOMs = model.UOMs.Count(u => !u.IsActive);
                model.UOMsByType = model.UOMs
                    .GroupBy(u => u.Type)
                    .ToDictionary(g => g.Key, g => g.Count());
            }

            return View(model);
        }

        // GET: UOM/Create
        public IActionResult Create()
        {
            return View(new UOMViewModel());
        }

        // POST: UOM/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(UOMViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    connection.Open();

                    // Check for duplicate name
                    var checkQuery = "SELECT COUNT(*) FROM tbl_mst_uom WHERE UOM_Name = @Name";
                    using (var checkCommand = new SqlCommand(checkQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@Name", model.Name);
                        var count = (int)checkCommand.ExecuteScalar();
                        
                        if (count > 0)
                        {
                            ModelState.AddModelError("Name", "A UOM with this name already exists");
                            return View(model);
                        }
                    }

                    var query = @"
                        INSERT INTO tbl_mst_uom (UOM_Name, UOM_Type, Base_Quantity_ML, IsActive, CreatedBy, CreatedDate)
                        VALUES (@Name, @Type, @BaseQuantity, @IsActive, @CreatedBy, GETDATE())";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Name", model.Name);
                        command.Parameters.AddWithValue("@Type", model.Type);
                        command.Parameters.AddWithValue("@BaseQuantity", model.BaseQuantityML);
                        command.Parameters.AddWithValue("@IsActive", model.IsActive);
                        command.Parameters.AddWithValue("@CreatedBy", GetCurrentUserId() ?? (object)DBNull.Value);

                        command.ExecuteNonQuery();
                    }
                }

                TempData["SuccessMessage"] = "UOM created successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error creating UOM: {ex.Message}");
                return View(model);
            }
        }

        // GET: UOM/Edit/5
        public IActionResult Edit(int id)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                var query = @"
                    SELECT UOM_Id, UOM_Name, UOM_Type, Base_Quantity_ML, IsActive, CreatedBy, CreatedDate
                    FROM tbl_mst_uom
                    WHERE UOM_Id = @Id";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var model = new UOMViewModel
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                Type = reader.GetString(2),
                                BaseQuantityML = reader.GetDecimal(3),
                                IsActive = reader.GetBoolean(4),
                                CreatedBy = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                                CreatedDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
                            };

                            return View(model);
                        }
                    }
                }
            }

            return NotFound();
        }

        // POST: UOM/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, UOMViewModel model)
        {
            if (id != model.Id)
                return BadRequest();

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    connection.Open();

                    // Check for duplicate name (excluding current record)
                    var checkQuery = "SELECT COUNT(*) FROM tbl_mst_uom WHERE UOM_Name = @Name AND UOM_Id != @Id";
                    using (var checkCommand = new SqlCommand(checkQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@Name", model.Name);
                        checkCommand.Parameters.AddWithValue("@Id", id);
                        var count = (int)checkCommand.ExecuteScalar();
                        
                        if (count > 0)
                        {
                            ModelState.AddModelError("Name", "A UOM with this name already exists");
                            return View(model);
                        }
                    }

                    var query = @"
                        UPDATE tbl_mst_uom 
                        SET UOM_Name = @Name,
                            UOM_Type = @Type,
                            Base_Quantity_ML = @BaseQuantity,
                            IsActive = @IsActive,
                            UpdatedDate = GETDATE(),
                            UpdatedBy = @UpdatedBy
                        WHERE UOM_Id = @Id";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Id", id);
                        command.Parameters.AddWithValue("@Name", model.Name);
                        command.Parameters.AddWithValue("@Type", model.Type);
                        command.Parameters.AddWithValue("@BaseQuantity", model.BaseQuantityML);
                        command.Parameters.AddWithValue("@IsActive", model.IsActive);
                        command.Parameters.AddWithValue("@UpdatedBy", GetCurrentUserId() ?? (object)DBNull.Value);

                        command.ExecuteNonQuery();
                    }
                }

                TempData["SuccessMessage"] = "UOM updated successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error updating UOM: {ex.Message}");
                return View(model);
            }
        }

        // POST: UOM/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    connection.Open();

                    // Check if UOM is used in any menu items
                    var checkQuery = "SELECT COUNT(*) FROM MenuItems WHERE UOM_Id = @Id";
                    using (var checkCommand = new SqlCommand(checkQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@Id", id);
                        var count = (int)checkCommand.ExecuteScalar();
                        
                        if (count > 0)
                        {
                            TempData["ErrorMessage"] = $"Cannot delete UOM. It is currently used by {count} menu item(s).";
                            return RedirectToAction(nameof(Index));
                        }
                    }

                    // Soft delete
                    var query = "UPDATE tbl_mst_uom SET IsActive = 0, UpdatedDate = GETDATE(), UpdatedBy = @UpdatedBy WHERE UOM_Id = @Id";
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Id", id);
                        command.Parameters.AddWithValue("@UpdatedBy", GetCurrentUserId() ?? (object)DBNull.Value);
                        command.ExecuteNonQuery();
                    }
                }

                TempData["SuccessMessage"] = "UOM deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting UOM: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // Helper method to get current user ID
        private int? GetCurrentUserId()
        {
            var userIdClaim = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : null;
        }
    }
}
```

---

### **STEP 5: Views**

#### **5.1 UOM Index View**

**File**: `RestaurantManagementSystem/Views/UOM/Index.cshtml`

```cshtml
@model UOMListViewModel
@{
    ViewData["Title"] = "UOM Master";
}

<div class="container-fluid">
    <div class="row mb-3">
        <div class="col">
            <h2><i class="fas fa-ruler-combined"></i> Unit of Measurement Master</h2>
        </div>
        <div class="col-auto">
            <a asp-action="Create" class="btn btn-primary">
                <i class="fas fa-plus"></i> Add New UOM
            </a>
        </div>
    </div>

    @if (TempData["SuccessMessage"] != null)
    {
        <div class="alert alert-success alert-dismissible fade show" role="alert">
            @TempData["SuccessMessage"]
            <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
        </div>
    }

    @if (TempData["ErrorMessage"] != null)
    {
        <div class="alert alert-danger alert-dismissible fade show" role="alert">
            @TempData["ErrorMessage"]
            <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
        </div>
    }

    <!-- Statistics Cards -->
    <div class="row mb-3">
        <div class="col-md-3">
            <div class="card bg-primary text-white">
                <div class="card-body">
                    <h5 class="card-title">Total UOMs</h5>
                    <h2>@Model.TotalUOMs</h2>
                </div>
            </div>
        </div>
        <div class="col-md-3">
            <div class="card bg-success text-white">
                <div class="card-body">
                    <h5 class="card-title">Active</h5>
                    <h2>@Model.ActiveUOMs</h2>
                </div>
            </div>
        </div>
        <div class="col-md-3">
            <div class="card bg-warning text-white">
                <div class="card-body">
                    <h5 class="card-title">Inactive</h5>
                    <h2>@Model.InactiveUOMs</h2>
                </div>
            </div>
        </div>
        <div class="col-md-3">
            <div class="card bg-info text-white">
                <div class="card-body">
                    <h5 class="card-title">Types</h5>
                    <h2>@Model.UOMsByType.Count</h2>
                </div>
            </div>
        </div>
    </div>

    <!-- Filters -->
    <div class="card mb-3">
        <div class="card-body">
            <form method="get" class="row g-3">
                <div class="col-md-4">
                    <label class="form-label">Search</label>
                    <input type="text" name="searchTerm" value="@Model.SearchTerm" class="form-control" placeholder="Search by name or type..." />
                </div>
                <div class="col-md-3">
                    <label class="form-label">Filter by Type</label>
                    <select name="filterType" class="form-select">
                        <option value="">All Types</option>
                        <option value="Peg" selected="@(Model.FilterType == "Peg")">Peg</option>
                        <option value="Bottle" selected="@(Model.FilterType == "Bottle")">Bottle</option>
                        <option value="Beer" selected="@(Model.FilterType == "Beer")">Beer</option>
                        <option value="Wine" selected="@(Model.FilterType == "Wine")">Wine</option>
                        <option value="Cocktail" selected="@(Model.FilterType == "Cocktail")">Cocktail</option>
                        <option value="SoftDrink" selected="@(Model.FilterType == "SoftDrink")">Soft Drink</option>
                        <option value="Food" selected="@(Model.FilterType == "Food")">Food</option>
                    </select>
                </div>
                <div class="col-md-3">
                    <label class="form-label">&nbsp;</label>
                    <div class="form-check">
                        <input type="checkbox" name="showInactive" value="true" class="form-check-input" checked="@Model.ShowInactive" />
                        <label class="form-check-label">Show Inactive</label>
                    </div>
                </div>
                <div class="col-md-2">
                    <label class="form-label">&nbsp;</label>
                    <button type="submit" class="btn btn-primary w-100">
                        <i class="fas fa-filter"></i> Filter
                    </button>
                </div>
            </form>
        </div>
    </div>

    <!-- UOM Table -->
    <div class="card">
        <div class="card-body">
            <table class="table table-hover" id="uomTable">
                <thead class="table-dark">
                    <tr>
                        <th>UOM Name</th>
                        <th>Type</th>
                        <th>Base Quantity (ml)</th>
                        <th>Menu Items</th>
                        <th>Status</th>
                        <th>Created By</th>
                        <th>Created Date</th>
                        <th>Actions</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var uom in Model.UOMs)
                    {
                        <tr class="@(!uom.IsActive ? "table-secondary" : "")">
                            <td><strong>@uom.Name</strong></td>
                            <td>
                                <span class="badge bg-@GetTypeBadgeColor(uom.Type)">@uom.Type</span>
                            </td>
                            <td>@uom.BaseQuantityML.ToString("F2")</td>
                            <td>
                                @if (uom.MenuItemCount > 0)
                                {
                                    <span class="badge bg-info">@uom.MenuItemCount items</span>
                                }
                                else
                                {
                                    <span class="text-muted">Not used</span>
                                }
                            </td>
                            <td>
                                @if (uom.IsActive)
                                {
                                    <span class="badge bg-success">Active</span>
                                }
                                else
                                {
                                    <span class="badge bg-secondary">Inactive</span>
                                }
                            </td>
                            <td>@uom.CreatedByName</td>
                            <td>@uom.CreatedDate?.ToString("dd/MM/yyyy")</td>
                            <td>
                                <div class="btn-group">
                                    <a asp-action="Edit" asp-route-id="@uom.Id" class="btn btn-sm btn-outline-primary">
                                        <i class="fas fa-edit"></i>
                                    </a>
                                    @if (uom.MenuItemCount == 0)
                                    {
                                        <form asp-action="Delete" asp-route-id="@uom.Id" method="post" style="display:inline;" 
                                              onsubmit="return confirm('Are you sure you want to delete this UOM?');">
                                            @Html.AntiForgeryToken()
                                            <button type="submit" class="btn btn-sm btn-outline-danger">
                                                <i class="fas fa-trash"></i>
                                            </button>
                                        </form>
                                    }
                                </div>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    </div>
</div>

@section Scripts {
    <script>
        $(document).ready(function() {
            $('#uomTable').DataTable({
                order: [[1, 'asc'], [0, 'asc']],
                pageLength: 25
            });
        });
    </script>
}

@functions {
    string GetTypeBadgeColor(string type)
    {
        return type switch
        {
            "Peg" => "warning",
            "Bottle" => "primary",
            "Beer" => "info",
            "Wine" => "danger",
            "Cocktail" => "success",
            "SoftDrink" => "secondary",
            "Food" => "dark",
            _ => "light text-dark"
        };
    }
}
```

#### **5.2 UOM Create/Edit View**

**File**: `RestaurantManagementSystem/Views/UOM/Create.cshtml` and `Edit.cshtml`

```cshtml
@model UOMViewModel
@{
    ViewData["Title"] = Model.Id == 0 ? "Create UOM" : "Edit UOM";
    var isEdit = Model.Id > 0;
}

<div class="container">
    <div class="row">
        <div class="col-md-8 offset-md-2">
            <div class="card">
                <div class="card-header">
                    <h4><i class="fas fa-ruler-combined"></i> @ViewData["Title"]</h4>
                </div>
                <div class="card-body">
                    <form asp-action="@(isEdit ? "Edit" : "Create")" method="post">
                        @Html.AntiForgeryToken()
                        @Html.HiddenFor(m => m.Id)
                        
                        <div asp-validation-summary="All" class="text-danger"></div>

                        <div class="mb-3">
                            <label asp-for="Name" class="form-label"></label>
                            <input asp-for="Name" class="form-control" placeholder="e.g., 30 ml Peg, 750 ml Bottle" />
                            <span asp-validation-for="Name" class="text-danger"></span>
                        </div>

                        <div class="mb-3">
                            <label asp-for="Type" class="form-label"></label>
                            <select asp-for="Type" class="form-select">
                                <option value="">-- Select Type --</option>
                                <option value="Peg">Peg (Spirits)</option>
                                <option value="Bottle">Bottle</option>
                                <option value="Beer">Beer</option>
                                <option value="Wine">Wine</option>
                                <option value="Cocktail">Cocktail</option>
                                <option value="SoftDrink">Soft Drink</option>
                                <option value="Food">Food</option>
                                <option value="Other">Other</option>
                            </select>
                            <span asp-validation-for="Type" class="text-danger"></span>
                        </div>

                        <div class="mb-3">
                            <label asp-for="BaseQuantityML" class="form-label"></label>
                            <div class="input-group">
                                <input asp-for="BaseQuantityML" type="number" step="0.01" class="form-control" placeholder="e.g., 30.00" />
                                <span class="input-group-text">ml</span>
                            </div>
                            <span asp-validation-for="BaseQuantityML" class="text-danger"></span>
                            <small class="form-text text-muted">Base quantity used for inventory calculations</small>
                        </div>

                        <div class="mb-3 form-check">
                            <input asp-for="IsActive" type="checkbox" class="form-check-input" />
                            <label asp-for="IsActive" class="form-check-label"></label>
                        </div>

                        <div class="d-flex justify-content-between">
                            <a asp-action="Index" class="btn btn-secondary">
                                <i class="fas fa-arrow-left"></i> Back to List
                            </a>
                            <button type="submit" class="btn btn-primary">
                                <i class="fas fa-save"></i> @(isEdit ? "Update" : "Create") UOM
                            </button>
                        </div>
                    </form>
                </div>
            </div>

            @if (isEdit && Model.CreatedDate.HasValue)
            {
                <div class="card mt-3">
                    <div class="card-body">
                        <h6>Audit Information</h6>
                        <dl class="row mb-0">
                            <dt class="col-sm-4">Created By:</dt>
                            <dd class="col-sm-8">@Model.CreatedByName</dd>
                            
                            <dt class="col-sm-4">Created Date:</dt>
                            <dd class="col-sm-8">@Model.CreatedDate?.ToString("dd/MM/yyyy HH:mm")</dd>
                            
                            @if (Model.UpdatedDate.HasValue)
                            {
                                <dt class="col-sm-4">Last Updated By:</dt>
                                <dd class="col-sm-8">@Model.UpdatedByName</dd>
                                
                                <dt class="col-sm-4">Last Updated:</dt>
                                <dd class="col-sm-8">@Model.UpdatedDate?.ToString("dd/MM/yyyy HH:mm")</dd>
                            }
                        </dl>
                    </div>
                </div>
            }
        </div>
    </div>
</div>

@section Scripts {
    @{await Html.RenderPartialAsync("_ValidationScriptsPartial");}
}
```

---

### **STEP 6: Update MenuController**

#### **6.1 Modify MenuController to Include UOM**

Add methods to MenuController.cs:

```csharp
// In MenuController.cs, add UOM support

// Helper method to get active UOMs for dropdown
private List<SelectListItem> GetActiveUOMs()
{
    var uoms = new List<SelectListItem>();
    
    using (var connection = new SqlConnection(_connectionString))
    {
        connection.Open();
        
        var query = @"
            SELECT UOM_Id, UOM_Name, UOM_Type, Base_Quantity_ML
            FROM tbl_mst_uom
            WHERE IsActive = 1
            ORDER BY UOM_Type, UOM_Name";
        
        using (var command = new SqlCommand(query, connection))
        using (var reader = command.ExecuteReader())
        {
            uoms.Add(new SelectListItem { Value = "", Text = "-- Select UOM --" });
            
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var name = reader.GetString(1);
                var type = reader.GetString(2);
                var quantity = reader.GetDecimal(3);
                
                uoms.Add(new SelectListItem
                {
                    Value = id.ToString(),
                    Text = $"{name} ({type}) - {quantity}ml"
                });
            }
        }
    }
    
    return uoms;
}

// Update Create and Edit actions to populate UOM dropdown
public IActionResult Create()
{
    var model = new MenuItemViewModel();
    // ... existing code ...
    model.UOMList = GetActiveUOMs();
    return View(model);
}

public IActionResult Edit(int id)
{
    // ... existing code to load menu item ...
    model.UOMList = GetActiveUOMs();
    return View(model);
}
```

---

### **STEP 7: Update Menu Item Views**

#### **7.1 Add UOM Field to Menu Create/Edit Forms**

In `Views/Menu/Create.cshtml` and `Edit.cshtml`, add:

```cshtml
<!-- Add this after Price field -->
<div class="mb-3">
    <label asp-for="UOMId" class="form-label"></label>
    <select asp-for="UOMId" asp-items="Model.UOMList" class="form-select">
    </select>
    <span asp-validation-for="UOMId" class="text-danger"></span>
    <small class="form-text text-muted">Select unit of measurement for beverages</small>
</div>
```

---

### **STEP 8: Navigation Menu Update**

#### **8.1 Add UOM Link to _Layout.cshtml**

```cshtml
<!-- In the Settings or Masters dropdown menu -->
<li class="nav-item dropdown">
    <a class="nav-link dropdown-toggle" href="#" data-bs-toggle="dropdown">
        <i class="fas fa-cog"></i> Masters
    </a>
    <ul class="dropdown-menu">
        <li><a class="dropdown-item" asp-controller="Category" asp-action="Index">Categories</a></li>
        <li><a class="dropdown-item" asp-controller="SubCategory" asp-action="Index">Sub Categories</a></li>
        <li><a class="dropdown-item" asp-controller="UOM" asp-action="Index">Unit of Measurement</a></li>
        <!-- other menu items -->
    </ul>
</li>
```

---

## 🔄 Migration and Data Update Strategy

### **Phase 1: Initial Setup (Current)**
1. Create `tbl_mst_uom` table
2. Insert sample UOM data
3. Add `UOM_Id` column to `MenuItems` (nullable)
4. Create foreign key and index

### **Phase 2: Data Migration (Optional)**
```sql
-- Update existing bar items to use default UOM
UPDATE MenuItems
SET UOM_Id = (SELECT TOP 1 UOM_Id FROM tbl_mst_uom WHERE UOM_Name = '30 ml')
WHERE menuitemgroupID = (SELECT ID FROM menuitemgroup WHERE itemgroup = 'Bar')
AND UOM_Id IS NULL;

-- Update existing food items to use default UOM (if applicable)
-- Note: Food items may not need UOM unless using portion-based tracking
```

### **Phase 3: Make UOM Mandatory (Future)**
```sql
-- After migration is complete, make UOM_Id NOT NULL
ALTER TABLE MenuItems
ALTER COLUMN UOM_Id INT NOT NULL;
```

---

## 📊 Testing Checklist

### **Database Testing**
- [ ] Run `create_uom_master_table.sql` successfully
- [ ] Run `insert_uom_sample_data.sql` successfully
- [ ] Run `add_uom_to_menuitems.sql` successfully
- [ ] Verify foreign key relationship works
- [ ] Test cascading delete behavior

### **Application Testing**
- [ ] UOM List page displays correctly
- [ ] Create new UOM works
- [ ] Edit existing UOM works
- [ ] Delete UOM (soft delete) works
- [ ] Search and filter UOMs works
- [ ] Menu item dropdown shows UOMs
- [ ] Menu item can be created with UOM
- [ ] Menu item can be updated with UOM
- [ ] Menu item displays UOM name correctly

### **Integration Testing**
- [ ] Orders show UOM for menu items
- [ ] Reports include UOM information
- [ ] Inventory calculations use Base_Quantity_ML
- [ ] Bar dashboard shows UOM details

---

## 📈 Benefits of UOM Implementation

1. **Accurate Inventory Management**: Track beverage consumption in milliliters
2. **Flexible Pricing**: Different prices for different serving sizes
3. **Better Reporting**: Analyze sales by UOM type
4. **Stock Control**: Calculate bottle consumption based on pegs served
5. **Compliance**: Meet regulatory requirements for alcohol serving sizes
6. **Customer Clarity**: Clear display of serving sizes on menus

---

## 🎯 Next Steps After Implementation

1. **Inventory Integration**: Link UOM with stock management
2. **Recipe Costing**: Calculate cost per UOM
3. **Price Matrix**: Different prices for different UOMs of same item
4. **Bottle Tracking**: Track partial bottle consumption
5. **Wastage Monitoring**: Track spillage and wastage by UOM
6. **Reporting Enhancements**: Add UOM-based analytics

---

## 📝 Summary

This implementation provides a robust UOM master system that:
- ✅ Supports multiple beverage and food measurement units
- ✅ Integrates seamlessly with existing MenuItems structure
- ✅ Provides flexible data management through web UI
- ✅ Enables accurate inventory calculations
- ✅ Maintains data integrity through foreign keys
- ✅ Supports soft deletes for audit trail
- ✅ Includes comprehensive error handling

**Status**: Ready for implementation  
**Estimated Implementation Time**: 4-6 hours  
**Database Impact**: Low (backward compatible)  
**Breaking Changes**: None (UOM_Id is nullable)

---

**Last Updated**: November 5, 2025  
**Document Version**: 1.0  
**Author**: Restaurant Management System Team
