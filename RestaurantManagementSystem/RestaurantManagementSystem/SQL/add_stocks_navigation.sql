-- =============================================
-- Script : add_stocks_navigation.sql
-- Purpose: Add "Stocks" top-level navigation menu and
--          "UOM Master" child page for the Restaurant BOM module.
--          Safe to re-run (uses MERGE).
-- =============================================

USE [dev_Restaurant]
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
        (Code,             ParentCode,      DisplayName,   Description,
         Area, ControllerName, ActionName,  RouteValues, CustomUrl,
         IconCss,                            DisplayOrder, IsActive, IsVisible,
         ThemeColor, ShortcutHint, OpenInNewTab)
VALUES
    -- ── Parent: Stocks ──────────────────────────────────────────────────────
    ('NAV_STOCKS',     NULL,           'Stocks',      'Stock and inventory masters',
     NULL, NULL,         NULL,         NULL, NULL,
     'fas fa-boxes compact-icon',      11,  1, 1,
     '#22c55e', NULL, 0),

    -- ── Child: UOM Master ───────────────────────────────────────────────────
    ('NAV_STOCKS_UOM', 'NAV_STOCKS',   'UOM Master',  'Unit of Measurement master for BOM',
     NULL, 'Uom',        'Index',      NULL, NULL,
     'fas fa-ruler-combined compact-icon text-primary',
                                        1,  1, 1,
     NULL, NULL, 0);

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

PRINT 'Stocks navigation entries merged.';

-- ── Grant full permissions to Administrator role ─────────────────────────────
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
    WHERE nm.Code IN ('NAV_STOCKS', 'NAV_STOCKS_UOM')
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RoleMenuPermissions rmp
          WHERE rmp.RoleId = @AdminRoleId AND rmp.MenuId = nm.Id
      );

    PRINT 'Administrator role permissions granted for Stocks navigation.';
END
ELSE
    PRINT 'WARNING: Administrator role not found. Grant permissions manually.';
GO

-- Verify
SELECT Id, Code, ParentCode, DisplayName, ControllerName, ActionName, DisplayOrder
FROM dbo.NavigationMenus
WHERE Code IN ('NAV_STOCKS','NAV_STOCKS_UOM')
ORDER BY DisplayOrder;
GO
