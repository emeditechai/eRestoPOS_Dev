-- =============================================
-- Script : inventory_navigation_setup.sql
-- Purpose: Add all Inventory & Stock Management navigation entries under
--          NAV_STOCKS parent. Safe to re-run (uses MERGE).
-- =============================================

USE [dev_Restaurant];
GO

SET XACT_ABORT ON;

DECLARE @Menus TABLE (
    Code         NVARCHAR(60)  NOT NULL,
    ParentCode   NVARCHAR(60)  NULL,
    DisplayName  NVARCHAR(120) NOT NULL,
    Description  NVARCHAR(255) NULL,
    Area         NVARCHAR(80)  NULL,
    ControllerName NVARCHAR(120) NULL,
    ActionName   NVARCHAR(120) NULL,
    RouteValues  NVARCHAR(200) NULL,
    CustomUrl    NVARCHAR(400) NULL,
    IconCss      NVARCHAR(100) NULL,
    DisplayOrder INT           NOT NULL,
    IsActive     BIT           NOT NULL,
    IsVisible    BIT           NOT NULL,
    ThemeColor   NVARCHAR(30)  NULL,
    ShortcutHint NVARCHAR(40)  NULL,
    OpenInNewTab BIT           NOT NULL
);

INSERT INTO @Menus
    (Code, ParentCode, DisplayName, Description,
     Area, ControllerName, ActionName, RouteValues, CustomUrl,
     IconCss, DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
VALUES
    -- ─── Parent: Stocks ─────────────────────────────────────────────────────
    ('NAV_STOCKS', NULL, 'Stocks', 'Inventory & Stock Management',
     NULL, NULL, NULL, NULL, NULL,
     'fas fa-boxes-stacked compact-icon', 11, 1, 1, '#22c55e', NULL, 0),

    -- ─── Inventory Dashboard ────────────────────────────────────────────────
    ('NAV_STOCKS_DASHBOARD', 'NAV_STOCKS', 'Inventory Dashboard', 'Stock overview and alerts',
     NULL, 'InventoryDashboard', 'Index', NULL, NULL,
     'fas fa-chart-pie compact-icon text-success', 1, 1, 1, NULL, NULL, 0),

    -- ─── Masters ────────────────────────────────────────────────────────────
    ('NAV_STOCKS_GODOWN', 'NAV_STOCKS', 'Godown Master', 'Manage godowns / warehouses',
     NULL, 'Godown', 'Index', NULL, NULL,
     'fas fa-warehouse compact-icon text-info', 2, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_PARTY', 'NAV_STOCKS', 'Supplier Master', 'Manage suppliers & vendors',
     NULL, 'Party', 'Index', NULL, NULL,
     'fas fa-truck compact-icon text-warning', 3, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_UOM', 'NAV_STOCKS', 'UOM Master', 'Unit of Measurement master for BOM',
     NULL, 'Uom', 'Index', NULL, NULL,
     'fas fa-ruler-combined compact-icon text-primary', 4, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_PARAMS', 'NAV_STOCKS', 'Inventory Parameters', 'Configure inventory behavior',
     NULL, 'InventoryParameters', 'Index', NULL, NULL,
     'fas fa-sliders compact-icon text-secondary', 5, 1, 1, NULL, NULL, 0),

    -- ─── Transactions ────────────────────────────────────────────────────────
    ('NAV_STOCKS_OPENING', 'NAV_STOCKS', 'Opening Stock', 'Enter opening stock balances',
     NULL, 'OpeningStock', 'Index', NULL, NULL,
     'fas fa-layer-group compact-icon text-primary', 6, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_PO', 'NAV_STOCKS', 'Purchase Orders', 'Create and manage purchase orders',
     NULL, 'PurchaseOrder', 'Index', NULL, NULL,
     'fas fa-file-purchase compact-icon text-danger', 7, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_GRN', 'NAV_STOCKS', 'GRN', 'Goods Receipt Notes',
     NULL, 'GRN', 'Index', NULL, NULL,
     'fas fa-clipboard-check compact-icon text-success', 8, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_TRANSFER', 'NAV_STOCKS', 'Stock Transfer', 'Transfer stock between godowns',
     NULL, 'StockTransfer', 'Index', NULL, NULL,
     'fas fa-exchange-alt compact-icon text-info', 9, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_DAMAGE', 'NAV_STOCKS', 'Damage Entry', 'Record damaged / wasted stock',
     NULL, 'DamageEntry', 'Index', NULL, NULL,
     'fas fa-trash-alt compact-icon text-danger', 10, 1, 1, NULL, NULL, 0),

    -- ─── Reports ─────────────────────────────────────────────────────────────
    ('NAV_STOCKS_LEDGER', 'NAV_STOCKS', 'Stock Ledger', 'View complete stock movement ledger',
     NULL, 'StockLedger', 'Index', NULL, NULL,
     'fas fa-book compact-icon text-primary', 11, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_REPORTS', 'NAV_STOCKS', 'Reports', 'Inventory reports sub-menu',
     NULL, NULL, NULL, NULL, NULL,
     'fas fa-chart-bar compact-icon text-warning', 12, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_RPT_SUMMARY', 'NAV_STOCKS_REPORTS', 'Stock Summary', 'Current stock by godown/item',
     NULL, 'StockLedger', 'StockSummary', NULL, NULL,
     'fas fa-table compact-icon text-info', 1, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_RPT_CLOSING', 'NAV_STOCKS_REPORTS', 'Closing Stock', 'Closing stock calculation',
     NULL, 'StockLedger', 'ClosingStock', NULL, NULL,
     'fas fa-calendar-check compact-icon text-success', 2, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_RPT_VALUATION', 'NAV_STOCKS_REPORTS', 'Stock Valuation', 'Stock value at average cost',
     NULL, 'StockLedger', 'Valuation', NULL, NULL,
     'fas fa-rupee-sign compact-icon text-warning', 3, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_RPT_PURCHASE', 'NAV_STOCKS_REPORTS', 'Purchase Register', 'GRN-wise purchase register',
     NULL, 'StockLedger', 'PurchaseRegister', NULL, NULL,
     'fas fa-shopping-cart compact-icon text-danger', 4, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_RPT_TRANSFER', 'NAV_STOCKS_REPORTS', 'Transfer Register', 'Stock transfer history',
     NULL, 'StockLedger', 'TransferRegister', NULL, NULL,
     'fas fa-arrows-alt-h compact-icon text-info', 5, 1, 1, NULL, NULL, 0),

    ('NAV_STOCKS_RPT_DAMAGE', 'NAV_STOCKS_REPORTS', 'Damage Register', 'Damage / wastage register',
     NULL, 'StockLedger', 'DamageRegister', NULL, NULL,
     'fas fa-exclamation-triangle compact-icon text-danger', 6, 1, 1, NULL, NULL, 0);

MERGE dbo.NavigationMenus AS target
USING @Menus AS source ON target.Code = source.Code
WHEN MATCHED THEN UPDATE SET
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
WHEN NOT MATCHED THEN INSERT
    (Code, ParentCode, DisplayName, Description, Area, ControllerName, ActionName,
     RouteValues, CustomUrl, IconCss, DisplayOrder, IsActive, IsVisible,
     ThemeColor, ShortcutHint, OpenInNewTab)
VALUES
    (source.Code, source.ParentCode, source.DisplayName, source.Description,
     source.Area, source.ControllerName, source.ActionName,
     source.RouteValues, source.CustomUrl, source.IconCss, source.DisplayOrder,
     source.IsActive, source.IsVisible, source.ThemeColor, source.ShortcutHint,
     source.OpenInNewTab);

PRINT 'Inventory navigation entries merged.';

-- Grant full permissions to Administrator role
DECLARE @AdminRoleId INT = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Administrator');
IF @AdminRoleId IS NOT NULL
BEGIN
    INSERT INTO dbo.RoleMenuPermissions
           (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete, CanApprove, CanPrint, CanExport,
            CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
    SELECT
        @AdminRoleId, nm.Id, 1, 1, 1, 1, 1, 1, 1,
        SYSUTCDATETIME(), 0, SYSUTCDATETIME(), 0
    FROM dbo.NavigationMenus nm
    WHERE nm.Code IN (
        'NAV_STOCKS', 'NAV_STOCKS_DASHBOARD', 'NAV_STOCKS_GODOWN',
        'NAV_STOCKS_PARTY', 'NAV_STOCKS_UOM', 'NAV_STOCKS_PARAMS',
        'NAV_STOCKS_OPENING', 'NAV_STOCKS_PO', 'NAV_STOCKS_GRN',
        'NAV_STOCKS_TRANSFER', 'NAV_STOCKS_DAMAGE', 'NAV_STOCKS_LEDGER',
        'NAV_STOCKS_REPORTS', 'NAV_STOCKS_RPT_SUMMARY', 'NAV_STOCKS_RPT_CLOSING',
        'NAV_STOCKS_RPT_VALUATION', 'NAV_STOCKS_RPT_PURCHASE',
        'NAV_STOCKS_RPT_TRANSFER', 'NAV_STOCKS_RPT_DAMAGE'
    )
    AND NOT EXISTS (
        SELECT 1 FROM dbo.RoleMenuPermissions rmp
        WHERE rmp.RoleId = @AdminRoleId AND rmp.MenuId = nm.Id
    );
    PRINT 'Admin role permissions granted for inventory navigation.';
END
ELSE
    PRINT 'WARNING: Administrator role not found. Permissions not granted.';
GO
