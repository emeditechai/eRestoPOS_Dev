-- =============================================================
--  GODOWN MASTER  –  create_godowns_table.sql
--  Run this script on any database that does NOT yet have the
--  dbo.Godowns table.  The script is fully idempotent.
-- =============================================================

-- 1. Create Godowns table (idempotent)
IF OBJECT_ID('dbo.Godowns', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Godowns (
        Id            INT IDENTITY(1,1) NOT NULL
                        CONSTRAINT PK_Godowns PRIMARY KEY,
        BranchId      INT           NOT NULL,
        Code          NVARCHAR(20)  NOT NULL,
        GodownName    NVARCHAR(150) NOT NULL,
        IsMainGodown  BIT           NOT NULL CONSTRAINT DF_Godowns_IsMainGodown  DEFAULT 0,
        Address       NVARCHAR(500) NULL,
        IsActive      BIT           NOT NULL CONSTRAINT DF_Godowns_IsActive      DEFAULT 1,
        CreatedAt     DATETIME2     NULL,
        UpdatedAt     DATETIME2     NULL,

        CONSTRAINT UQ_Godowns_BranchCode UNIQUE (BranchId, Code)
    );

    PRINT 'Table dbo.Godowns created.';
END
ELSE
BEGIN
    PRINT 'Table dbo.Godowns already exists – skipping CREATE.';
END
GO

-- 2. Add any missing columns to an existing table (for re-runs / upgrades)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Godowns') AND name = 'Address')
    ALTER TABLE dbo.Godowns ADD Address NVARCHAR(500) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Godowns') AND name = 'UpdatedAt')
    ALTER TABLE dbo.Godowns ADD UpdatedAt DATETIME2 NULL;
GO

-- 3. Ensure the unique index on (BranchId, Code) exists  
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.Godowns')
      AND name = 'UQ_Godowns_BranchCode')
BEGIN
    ALTER TABLE dbo.Godowns
        ADD CONSTRAINT UQ_Godowns_BranchCode UNIQUE (BranchId, Code);
    PRINT 'Unique constraint UQ_Godowns_BranchCode added.';
END
GO

-- 4. Add NAV_STOCKS_GODOWN navigation entry under Stocks
IF OBJECT_ID('dbo.NavigationMenus', 'U') IS NOT NULL
BEGIN
    -- Ensure parent NAV_STOCKS exists first
    IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_STOCKS')
    BEGIN
        INSERT INTO dbo.NavigationMenus
               (Code, ParentCode, DisplayName, Description, Area,
                ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
                DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
        VALUES ('NAV_STOCKS', NULL, 'Stocks', 'Stock and inventory masters', NULL,
                NULL, NULL, NULL, NULL, 'fas fa-boxes compact-icon',
                11, 1, 1, '#22c55e', NULL, 0);
        PRINT 'NAV_STOCKS parent node inserted.';
    END

    -- Insert Godown child entry
    IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_STOCKS_GODOWN')
    BEGIN
        INSERT INTO dbo.NavigationMenus
               (Code, ParentCode, DisplayName, Description, Area,
                ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
                DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
        VALUES ('NAV_STOCKS_GODOWN', 'NAV_STOCKS', 'Godown Master',
                'Manage branch-wise godowns / warehouses', NULL,
                'Master', 'GodownList', NULL, NULL,
                'fas fa-warehouse compact-icon text-info',
                4, 1, 1, NULL, NULL, 0);
        PRINT 'NAV_STOCKS_GODOWN nav entry inserted.';
    END
END
GO

-- 5. Grant full permissions to Administrator role
IF OBJECT_ID('dbo.NavigationMenus',      'U') IS NOT NULL
AND OBJECT_ID('dbo.RoleMenuPermissions', 'U') IS NOT NULL
AND OBJECT_ID('dbo.Roles',               'U') IS NOT NULL
BEGIN
    DECLARE @AdminRoleId INT = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Administrator');
    IF @AdminRoleId IS NOT NULL
    BEGIN
        INSERT INTO dbo.RoleMenuPermissions
               (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete,
                CanApprove, CanPrint, CanExport, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
        SELECT @AdminRoleId, nm.Id, 1, 1, 1, 1, 1, 1, 1,
               SYSUTCDATETIME(), 0, SYSUTCDATETIME(), 0
        FROM dbo.NavigationMenus nm
        WHERE nm.Code = 'NAV_STOCKS_GODOWN'
          AND NOT EXISTS (
              SELECT 1 FROM dbo.RoleMenuPermissions rmp
               WHERE rmp.RoleId = @AdminRoleId AND rmp.MenuId = nm.Id);
        PRINT 'Administrator permissions granted to NAV_STOCKS_GODOWN.';
    END
END
GO

-- =============================================================
--  Done.  Steps summary:
--    1. Created dbo.Godowns table (if not exists)
--    2. Added missing columns (Address, UpdatedAt) for upgrades
--    3. Ensured UQ_Godowns_BranchCode unique constraint
--    4. Inserted NAV_STOCKS_GODOWN nav menu entry
--    5. Granted Administrator role permissions
-- =============================================================
