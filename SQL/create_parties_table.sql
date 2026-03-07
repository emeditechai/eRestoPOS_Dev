-- =============================================================
--  PARTY MASTER  –  create_parties_table.sql
--  Run this script once on the target database.  Fully idempotent.
-- =============================================================

-- 1. Create Parties table
IF OBJECT_ID('dbo.Parties', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Parties (
        Id            INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_Parties PRIMARY KEY,
        PartyCode     NVARCHAR(20)       NOT NULL,
        PartyName     NVARCHAR(200)      NOT NULL,
        PartyType     NVARCHAR(20)       NOT NULL,   -- Vendor | Supplier | Trader
        Email         NVARCHAR(200)      NULL,
        PhoneNumber   NVARCHAR(20)       NOT NULL,
        Address       NVARCHAR(500)      NULL,
        PinCode       NVARCHAR(10)       NULL,
        IsCreditAllow BIT                NOT NULL CONSTRAINT DF_Parties_IsCreditAllow DEFAULT 0,
        AllowBalance  DECIMAL(18,2)      NULL,       -- only relevant when IsCreditAllow = 1
        IsActive      BIT                NOT NULL CONSTRAINT DF_Parties_IsActive      DEFAULT 1,
        CreatedAt     DATETIME2          NULL,
        UpdatedAt     DATETIME2          NULL,

        CONSTRAINT UQ_Parties_PartyCode UNIQUE (PartyCode)
    );
    PRINT 'Table dbo.Parties created.';
END
ELSE
BEGIN
    PRINT 'Table dbo.Parties already exists – skipping CREATE.';
END
GO

-- 2. Add any missing columns (upgrade / re-run safety)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Parties') AND name = 'Email')
    ALTER TABLE dbo.Parties ADD Email NVARCHAR(200) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Parties') AND name = 'PinCode')
    ALTER TABLE dbo.Parties ADD PinCode NVARCHAR(10) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Parties') AND name = 'AllowBalance')
    ALTER TABLE dbo.Parties ADD AllowBalance DECIMAL(18,2) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Parties') AND name = 'UpdatedAt')
    ALTER TABLE dbo.Parties ADD UpdatedAt DATETIME2 NULL;
GO

-- 3. Ensure unique constraint on PartyCode
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Parties') AND name = 'UQ_Parties_PartyCode')
BEGIN
    ALTER TABLE dbo.Parties ADD CONSTRAINT UQ_Parties_PartyCode UNIQUE (PartyCode);
    PRINT 'Unique constraint UQ_Parties_PartyCode added.';
END
GO

-- 4. Add NAV_STOCKS_PARTY navigation entry under Stocks
IF OBJECT_ID('dbo.NavigationMenus', 'U') IS NOT NULL
BEGIN
    -- Ensure parent NAV_STOCKS exists
    IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_STOCKS')
    BEGIN
        INSERT INTO dbo.NavigationMenus
               (Code, ParentCode, DisplayName, Description, Area,
                ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
                DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
        VALUES ('NAV_STOCKS', NULL, 'Stocks', 'Stock and inventory masters', NULL,
                NULL, NULL, NULL, NULL, 'fas fa-boxes compact-icon',
                11, 1, 1, '#22c55e', NULL, 0);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_STOCKS_PARTY')
    BEGIN
        INSERT INTO dbo.NavigationMenus
               (Code, ParentCode, DisplayName, Description, Area,
                ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
                DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
        VALUES ('NAV_STOCKS_PARTY', 'NAV_STOCKS', 'Party Master',
                'Manage vendors, suppliers and traders', NULL,
                'Master', 'PartyList', NULL, NULL,
                'fas fa-handshake compact-icon text-purple',
                5, 1, 1, NULL, NULL, 0);
        PRINT 'NAV_STOCKS_PARTY nav entry inserted.';
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
        FROM   dbo.NavigationMenus nm
        WHERE  nm.Code = 'NAV_STOCKS_PARTY'
          AND  NOT EXISTS (
               SELECT 1 FROM dbo.RoleMenuPermissions rmp
                WHERE rmp.RoleId = @AdminRoleId AND rmp.MenuId = nm.Id);
        PRINT 'Administrator permissions granted to NAV_STOCKS_PARTY.';
    END
END
GO

-- =============================================================
--  Done.
--    1. Created dbo.Parties table
--    2. Added missing columns for upgrade installs
--    3. Ensured UQ_Parties_PartyCode unique constraint
--    4. Inserted NAV_STOCKS_PARTY nav menu entry
--    5. Granted Administrator role permissions
-- =============================================================
