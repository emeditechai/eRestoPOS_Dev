-- =============================================================================
-- FILE  : upgrade_ingredients_item_master.sql
-- DESC  : Upgrades dbo.Ingredients to a full Stock Item Master
--         and adds optional UOMId FK to dbo.MenuItemIngredients.
--         Script is IDEMPOTENT – safe to re-run.
-- DATE  : 2025-07
-- =============================================================================

-- ─── 1. Ensure UomMaster exists (prerequisite) ────────────────────────────────
IF OBJECT_ID('dbo.UomMaster') IS NULL
BEGIN
    RAISERROR('UomMaster table not found. Run create_uom_master_v2.sql first.', 16, 1);
    RETURN;
END

-- ─── 2. Add new columns to dbo.Ingredients ───────────────────────────────────
IF COL_LENGTH('dbo.Ingredients', 'ItemCategory') IS NULL
    ALTER TABLE dbo.Ingredients ADD ItemCategory NVARCHAR(50) NULL;

IF COL_LENGTH('dbo.Ingredients', 'Description') IS NULL
    ALTER TABLE dbo.Ingredients ADD Description NVARCHAR(500) NULL;

IF COL_LENGTH('dbo.Ingredients', 'PurchaseUOMId') IS NULL
    ALTER TABLE dbo.Ingredients ADD PurchaseUOMId INT NULL;

IF COL_LENGTH('dbo.Ingredients', 'RecipeUOMId') IS NULL
    ALTER TABLE dbo.Ingredients ADD RecipeUOMId INT NULL;

IF COL_LENGTH('dbo.Ingredients', 'PurchaseToRecipeFactor') IS NULL
    ALTER TABLE dbo.Ingredients ADD PurchaseToRecipeFactor DECIMAL(18,6) NULL;

IF COL_LENGTH('dbo.Ingredients', 'StandardCost') IS NULL
    ALTER TABLE dbo.Ingredients ADD StandardCost DECIMAL(18,4) NULL;

IF COL_LENGTH('dbo.Ingredients', 'ReorderLevel') IS NULL
    ALTER TABLE dbo.Ingredients ADD ReorderLevel DECIMAL(18,3) NULL;

IF COL_LENGTH('dbo.Ingredients', 'IsActive') IS NULL
BEGIN
    ALTER TABLE dbo.Ingredients ADD IsActive BIT NOT NULL DEFAULT 1;
    -- Activate all existing rows
    UPDATE dbo.Ingredients SET IsActive = 1 WHERE IsActive IS NULL;
END

IF COL_LENGTH('dbo.Ingredients', 'CreatedAt') IS NULL
    ALTER TABLE dbo.Ingredients ADD CreatedAt DATETIME2 NULL DEFAULT SYSUTCDATETIME();

IF COL_LENGTH('dbo.Ingredients', 'UpdatedAt') IS NULL
    ALTER TABLE dbo.Ingredients ADD UpdatedAt DATETIME2 NULL;

-- ─── 3. FK: Ingredients.PurchaseUOMId → UomMaster ────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = 'FK_Ingredients_PurchaseUOM'
      AND parent_object_id = OBJECT_ID('dbo.Ingredients'))
BEGIN
    ALTER TABLE dbo.Ingredients
        ADD CONSTRAINT FK_Ingredients_PurchaseUOM
        FOREIGN KEY (PurchaseUOMId) REFERENCES dbo.UomMaster(UOMId);

    PRINT 'FK_Ingredients_PurchaseUOM created.';
END

-- ─── 4. FK: Ingredients.RecipeUOMId → UomMaster ──────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = 'FK_Ingredients_RecipeUOM'
      AND parent_object_id = OBJECT_ID('dbo.Ingredients'))
BEGIN
    ALTER TABLE dbo.Ingredients
        ADD CONSTRAINT FK_Ingredients_RecipeUOM
        FOREIGN KEY (RecipeUOMId) REFERENCES dbo.UomMaster(UOMId);

    PRINT 'FK_Ingredients_RecipeUOM created.';
END

-- ─── 5. MenuItemIngredients: add nullable UOMId FK column ─────────────────────
IF OBJECT_ID('dbo.MenuItemIngredients') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.MenuItemIngredients', 'UOMId') IS NULL
    BEGIN
        ALTER TABLE dbo.MenuItemIngredients ADD UOMId INT NULL;
        PRINT 'MenuItemIngredients.UOMId column added.';
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = 'FK_MenuItemIngredients_UOM'
          AND parent_object_id = OBJECT_ID('dbo.MenuItemIngredients'))
    BEGIN
        ALTER TABLE dbo.MenuItemIngredients
            ADD CONSTRAINT FK_MenuItemIngredients_UOM
            FOREIGN KEY (UOMId) REFERENCES dbo.UomMaster(UOMId);

        PRINT 'FK_MenuItemIngredients_UOM created.';
    END

    -- Make Unit column nullable (backward compat upgrade)
    IF EXISTS (
        SELECT 1 FROM sys.columns c
        JOIN sys.objects o ON o.object_id = c.object_id
        WHERE o.name = 'MenuItemIngredients' AND c.name = 'Unit' AND c.is_nullable = 0)
    BEGIN
        ALTER TABLE dbo.MenuItemIngredients
            ALTER COLUMN Unit NVARCHAR(20) NULL;
        PRINT 'MenuItemIngredients.Unit made nullable.';
    END
END

-- ─── 6. Create dbo.StockItemCategories lookup table ─────────────────────────
IF OBJECT_ID('dbo.StockItemCategories') IS NULL
BEGIN
    CREATE TABLE dbo.StockItemCategories (
        Id           INT IDENTITY(1,1) PRIMARY KEY,
        Name         NVARCHAR(100) NOT NULL,
        Description  NVARCHAR(300) NULL,
        DisplayOrder INT           NOT NULL DEFAULT 0,
        IsActive     BIT           NOT NULL DEFAULT 1,
        CreatedAt    DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt    DATETIME2     NULL
    );

    INSERT INTO dbo.StockItemCategories (Name, DisplayOrder, IsActive, CreatedAt) VALUES
        ('Vegetable',         1,  1, SYSUTCDATETIME()),
        ('Meat',              2,  1, SYSUTCDATETIME()),
        ('Seafood',           3,  1, SYSUTCDATETIME()),
        ('Spice & Herb',      4,  1, SYSUTCDATETIME()),
        ('Dairy',             5,  1, SYSUTCDATETIME()),
        ('Grain & Flour',     6,  1, SYSUTCDATETIME()),
        ('Beverage',          7,  1, SYSUTCDATETIME()),
        ('Sauce & Condiment', 8,  1, SYSUTCDATETIME()),
        ('Packaging',         9,  1, SYSUTCDATETIME()),
        ('Finish Goods',      10, 1, SYSUTCDATETIME()),
        ('Other',             11, 1, SYSUTCDATETIME());

    PRINT 'StockItemCategories table created and seeded.';
END
ELSE
BEGIN
    -- Ensure 'Finish Goods' exists on older installs
    IF NOT EXISTS (SELECT 1 FROM dbo.StockItemCategories WHERE Name = 'Finish Goods')
    BEGIN
        INSERT INTO dbo.StockItemCategories (Name, DisplayOrder, IsActive, CreatedAt)
        VALUES ('Finish Goods', 10, 1, SYSUTCDATETIME());
        PRINT 'Finish Goods category added.';
    END
END

-- ─── 7. Navigation: Stocks > Item Master + Stock Categories ──────────────────
IF OBJECT_ID('dbo.NavigationMenus') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_STOCKS_ITEMS')
    BEGIN
        INSERT INTO dbo.NavigationMenus
               (Code, ParentCode, DisplayName, Description, Area,
                ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
                DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
        VALUES ('NAV_STOCKS_ITEMS', 'NAV_STOCKS', 'Item Master',
                'Ingredient / inventory item master with UOM mapping', NULL,
                'Master', 'IngredientsList', NULL, NULL,
                'fas fa-boxes-stacked compact-icon text-success',
                2, 1, 1, NULL, NULL, 0);
        PRINT 'NAV_STOCKS_ITEMS nav entry inserted.';
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_STOCKS_CATEGORIES')
    BEGIN
        INSERT INTO dbo.NavigationMenus
               (Code, ParentCode, DisplayName, Description, Area,
                ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
                DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
        VALUES ('NAV_STOCKS_CATEGORIES', 'NAV_STOCKS', 'Stock Categories',
                'Manage stock item categories', NULL,
                'Master', 'StockCategoryList', NULL, NULL,
                'fas fa-tags compact-icon text-warning',
                3, 1, 1, NULL, NULL, 0);
        PRINT 'NAV_STOCKS_CATEGORIES nav entry inserted.';
    END

    -- Grant access to Administrator role
    DECLARE @AdminRoleId2 INT = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Administrator');
    IF @AdminRoleId2 IS NOT NULL
    BEGIN
        INSERT INTO dbo.RoleMenuPermissions
               (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete,
                CanApprove, CanPrint, CanExport, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
        SELECT @AdminRoleId2, nm.Id, 1, 1, 1, 1, 1, 1, 1,
               SYSUTCDATETIME(), 0, SYSUTCDATETIME(), 0
        FROM dbo.NavigationMenus nm
        WHERE nm.Code IN ('NAV_STOCKS_ITEMS', 'NAV_STOCKS_CATEGORIES')
          AND NOT EXISTS (
              SELECT 1 FROM dbo.RoleMenuPermissions rmp
              WHERE rmp.RoleId = @AdminRoleId2 AND rmp.MenuId = nm.Id);
    END
END

-- ─── 8. Hide Ingredients entry from Menu nav (moved to Stocks) ──────────────
IF OBJECT_ID('dbo.NavigationMenus') IS NOT NULL
    UPDATE dbo.NavigationMenus SET IsVisible = 0 WHERE Code = 'NAV_MENU_INGREDIENTS';

PRINT '=== upgrade_ingredients_item_master.sql completed successfully ===';
GO

