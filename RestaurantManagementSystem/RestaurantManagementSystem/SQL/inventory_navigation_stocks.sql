-- ============================================================
-- Inventory / Stocks Navigation Setup  (AUTHORITATIVE – run this, NOT inventory_navigation_setup.sql)
-- Uses current schema: dbo.NavigationMenus(Code, ParentCode, ...)
-- Safe to re-run (MERGE / idempotent)
--
-- FIXES applied by this MERGE:
--   NAV_STOCKS_GODOWN  : was 'Godown'/'Index'             → correct 'Master'/'GodownList'
--   NAV_STOCKS_PARTY   : was 'Party'/'Index'              → correct 'Master'/'PartyList'
--   NAV_STOCKS_DASHBOARD: was 'InventoryDashboard'/'Index'→ correct 'Inventory'/'Index'
--   NAV_STOCKS_PARAMS  : was 'InventoryParameters'/'Index'→ correct 'Inventory'/'Parameters'
--   NAV_STOCKS_OPENING : was 'OpeningStock'/'Index'       → correct 'Inventory'/'OpeningStock'
--   NAV_STOCKS_LEDGER  : was 'StockLedger'/'Index'        → correct 'Inventory'/'StockLedger'
-- Also adds UOM, Items, Categories, BOM entries if missing.
-- ============================================================

USE [dev_Restaurant];
GO

SET XACT_ABORT ON;
GO

DECLARE @Menus TABLE (
    Code           NVARCHAR(60)  NOT NULL,
    ParentCode     NVARCHAR(60)  NULL,
    DisplayName    NVARCHAR(120) NOT NULL,
    Description    NVARCHAR(255) NULL,
    Area           NVARCHAR(80)  NULL,
    ControllerName NVARCHAR(120) NULL,
    ActionName     NVARCHAR(120) NULL,
    RouteValues    NVARCHAR(200) NULL,
    CustomUrl      NVARCHAR(400) NULL,
    IconCss        NVARCHAR(100) NULL,
    DisplayOrder   INT           NOT NULL,
    IsActive       BIT           NOT NULL,
    IsVisible      BIT           NOT NULL,
    ThemeColor     NVARCHAR(30)  NULL,
    ShortcutHint   NVARCHAR(40)  NULL,
    OpenInNewTab   BIT           NOT NULL
);

INSERT INTO @Menus
    (Code, ParentCode, DisplayName, Description,
     Area, ControllerName, ActionName, RouteValues, CustomUrl,
     IconCss, DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
VALUES
    -- ── Parent ────────────────────────────────────────────────────────────────
    (N'NAV_STOCKS', NULL, N'Stocks', N'Inventory and stock management',
     NULL, NULL, NULL, NULL, NULL,
     N'fas fa-boxes compact-icon', 11, 1, 1, N'#22c55e', NULL, 0),

    -- ── Masters (old pages – these MUST keep working) ─────────────────────────
    (N'NAV_STOCKS_UOM',        N'NAV_STOCKS', N'UOM Master',        N'Unit of measurement master',                 NULL, N'Uom',    N'Index',           NULL, NULL, N'fas fa-ruler-combined compact-icon text-primary',  1, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_ITEMS',      N'NAV_STOCKS', N'Item Master',       N'Ingredient / inventory item master',         NULL, N'Master', N'IngredientsList', NULL, NULL, N'fas fa-boxes-stacked compact-icon text-success',   2, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_CATEGORIES', N'NAV_STOCKS', N'Stock Categories',  N'Manage stock item categories',               NULL, N'Master', N'StockCategoryList', NULL, NULL, N'fas fa-tags compact-icon text-warning',           3, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_GODOWN',     N'NAV_STOCKS', N'Godown Master',     N'Manage branch-wise godowns / warehouses',   NULL, N'Master', N'GodownList',      NULL, NULL, N'fas fa-warehouse compact-icon text-info',          4, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_PARTY',      N'NAV_STOCKS', N'Party Master',      N'Manage vendors, suppliers and traders',     NULL, N'Master', N'PartyList',       NULL, NULL, N'fas fa-handshake compact-icon text-purple',        5, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_BOM',        N'NAV_STOCKS', N'BOM Configure',     N'Bill of Material – link menu items to ingredients', NULL, N'BOM', N'BOMList',  NULL, NULL, N'fas fa-layer-group compact-icon text-success',     6, 1, 1, NULL, NULL, 0),

    -- ── Inventory Dashboard ────────────────────────────────────────────────────
    (N'NAV_STOCKS_DASHBOARD',  N'NAV_STOCKS', N'Inventory Dashboard', N'Inventory dashboard overview',            NULL, N'Inventory', N'Index',             NULL, NULL, N'fas fa-tachometer-alt compact-icon text-primary', 7, 1, 1, NULL, NULL, 0),

    -- ── Transactions (new pages) ──────────────────────────────────────────────
    (N'NAV_STOCKS_PARAMS',            N'NAV_STOCKS', N'Inv. Parameters',   N'Inventory parameters',              NULL, N'Inventory',     N'Parameters',       NULL, NULL, N'fas fa-sliders-h compact-icon text-secondary',   8, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_OPENING',           N'NAV_STOCKS', N'Opening Stock',     N'Opening stock entries',             NULL, N'Inventory',     N'OpeningStock',     NULL, NULL, N'fas fa-layer-group compact-icon text-info',      9, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_PO',                N'NAV_STOCKS', N'Purchase Orders',   N'Purchase order management',         NULL, N'PurchaseOrder', N'Index',            NULL, NULL, N'fas fa-shopping-cart compact-icon text-primary', 10, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_GRN',               N'NAV_STOCKS', N'Goods Receipt',     N'Goods receipt notes',               NULL, N'GRN',           N'Index',            NULL, NULL, N'fas fa-truck-loading compact-icon text-primary', 11, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_TRANSFER',          N'NAV_STOCKS', N'Stock Transfer',    N'Stock transfer entries',            NULL, N'StockTransfer', N'Index',            NULL, NULL, N'fas fa-exchange-alt compact-icon text-warning',  12, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_DAMAGE',            N'NAV_STOCKS', N'Damage / Wastage',  N'Damage and wastage entries',        NULL, N'DamageEntry',   N'Index',            NULL, NULL, N'fas fa-trash-alt compact-icon text-danger',      13, 1, 1, NULL, NULL, 0),

    -- ── Reports ───────────────────────────────────────────────────────────────
    (N'NAV_STOCKS_LEDGER',            N'NAV_STOCKS', N'Stock Ledger',      N'Stock movement ledger',             NULL, N'Inventory', N'StockLedger',      NULL, NULL, N'fas fa-book-open compact-icon text-primary',     14, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_SUMMARY',           N'NAV_STOCKS', N'Current Stock',     N'Current stock summary',             NULL, N'Inventory', N'StockSummary',     NULL, NULL, N'fas fa-cubes compact-icon text-primary',         15, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_CLOSING',           N'NAV_STOCKS', N'Closing Stock',     N'Closing stock report',              NULL, N'Inventory', N'ClosingStock',     NULL, NULL, N'fas fa-file-invoice compact-icon text-primary',  16, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_VALUATION',         N'NAV_STOCKS', N'Stock Valuation',   N'Stock valuation report',            NULL, N'Inventory', N'StockValuation',   NULL, NULL, N'fas fa-calculator compact-icon text-primary',    17, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_PURCHASE_REGISTER', N'NAV_STOCKS', N'Purchase Register', N'Purchase register',                 NULL, N'Inventory', N'PurchaseRegister', NULL, NULL, N'fas fa-receipt compact-icon text-primary',       18, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_TRANSFER_REGISTER', N'NAV_STOCKS', N'Transfer Register', N'Transfer register',                 NULL, N'Inventory', N'TransferRegister', NULL, NULL, N'fas fa-list-alt compact-icon text-primary',      19, 1, 1, NULL, NULL, 0),
    (N'NAV_STOCKS_DAMAGE_REGISTER',   N'NAV_STOCKS', N'Damage Register',   N'Damage register',                   NULL, N'Inventory', N'DamageRegister',   NULL, NULL, N'fas fa-clipboard-list compact-icon text-primary',20, 1, 1, NULL, NULL, 0);

MERGE dbo.NavigationMenus AS target
USING @Menus AS source
    ON target.Code = source.Code
WHEN MATCHED THEN
    UPDATE SET
        ParentCode     = source.ParentCode,
        DisplayName    = source.DisplayName,
        Description    = source.Description,
        Area           = source.Area,
        ControllerName = source.ControllerName,
        ActionName     = source.ActionName,
        RouteValues    = source.RouteValues,
        CustomUrl      = source.CustomUrl,
        IconCss        = source.IconCss,
        DisplayOrder   = source.DisplayOrder,
        IsActive       = source.IsActive,
        IsVisible      = source.IsVisible,
        ThemeColor     = source.ThemeColor,
        ShortcutHint   = source.ShortcutHint,
        OpenInNewTab   = source.OpenInNewTab,
        UpdatedAt      = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (Code, ParentCode, DisplayName, Description, Area, ControllerName, ActionName,
            RouteValues, CustomUrl, IconCss, DisplayOrder, IsActive, IsVisible,
            ThemeColor, ShortcutHint, OpenInNewTab)
    VALUES (source.Code, source.ParentCode, source.DisplayName, source.Description,
            source.Area, source.ControllerName, source.ActionName,
            source.RouteValues, source.CustomUrl, source.IconCss, source.DisplayOrder,
            source.IsActive, source.IsVisible, source.ThemeColor, source.ShortcutHint,
            source.OpenInNewTab);

PRINT 'Inventory navigation entries merged successfully.';

IF OBJECT_ID(N'dbo.RoleMenuPermissions', N'U') IS NOT NULL
BEGIN
    DECLARE @AdminRoleId INT = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = N'Administrator');

    IF @AdminRoleId IS NOT NULL
    BEGIN
        INSERT INTO dbo.RoleMenuPermissions
               (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete, CanApprove, CanPrint, CanExport,
                CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
        SELECT
            @AdminRoleId, nm.Id, 1, 1, 1, 1, 1, 1, 1,
            SYSUTCDATETIME(), 0, SYSUTCDATETIME(), 0
        FROM dbo.NavigationMenus nm
        WHERE EXISTS (SELECT 1 FROM @Menus m WHERE m.Code = nm.Code)
          AND NOT EXISTS (
              SELECT 1
              FROM dbo.RoleMenuPermissions rmp
              WHERE rmp.RoleId = @AdminRoleId
                AND rmp.MenuId = nm.Id
          );

        PRINT 'Administrator role granted access to all Inventory menus.';
    END
    ELSE
    BEGIN
        PRINT 'WARNING: Administrator role not found. Grant menu permissions manually.';
    END
END

SELECT Id, Code, ParentCode, DisplayName, ControllerName, ActionName, DisplayOrder, IsActive
FROM dbo.NavigationMenus
WHERE EXISTS (SELECT 1 FROM @Menus m WHERE m.Code = dbo.NavigationMenus.Code)
ORDER BY CASE WHEN ParentCode IS NULL THEN 0 ELSE 1 END, ParentCode, DisplayOrder, DisplayName;
GO
