/*
    Add Printer Setup page under Utility navigation
    - Adds NavigationMenus entry under NAV_UTILITY
    - Copies role permissions from NAV_UTILITY_TABLE_SECTIONS where available

    Page endpoint: Utility/PrinterSetup
*/

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

DECLARE @UtilityMenuId  INT;
DECLARE @BaselineMenuId INT;
DECLARE @PrinterMenuId  INT;

SELECT @UtilityMenuId  = Id FROM dbo.NavigationMenus WHERE Code = 'NAV_UTILITY';
SELECT @BaselineMenuId = Id FROM dbo.NavigationMenus WHERE Code = 'NAV_UTILITY_TABLE_SECTIONS';

IF @UtilityMenuId IS NULL
BEGIN
    RAISERROR('NAV_UTILITY menu not found in dbo.NavigationMenus. Run create_utility_navigation.sql first.', 16, 1);
END

IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_UTILITY_PRINTER_SETUP')
BEGIN
    INSERT INTO dbo.NavigationMenus
    (
        Code, ParentCode, DisplayName, Description, Area,
        ControllerName, ActionName, RouteValues, CustomUrl,
        IconCss, DisplayOrder, IsActive, IsVisible,
        ThemeColor, ShortcutHint, OpenInNewTab,
        CreatedAt, UpdatedAt
    )
    VALUES
    (
        'NAV_UTILITY_PRINTER_SETUP',
        'NAV_UTILITY',
        'Printer Setup',
        'Bluetooth thermal printer pairing and configuration',
        NULL,
        'Utility',
        'PrinterSetup',
        NULL,
        NULL,
        'fas fa-print compact-icon text-primary',
        25,
        1,
        1,
        NULL,
        NULL,
        0,
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );
END
ELSE
BEGIN
    UPDATE dbo.NavigationMenus
    SET ParentCode      = 'NAV_UTILITY',
        DisplayName     = 'Printer Setup',
        ControllerName  = 'Utility',
        ActionName      = 'PrinterSetup',
        IsActive        = 1,
        IsVisible       = 1,
        UpdatedAt       = SYSUTCDATETIME()
    WHERE Code = 'NAV_UTILITY_PRINTER_SETUP';
END

SELECT @PrinterMenuId = Id FROM dbo.NavigationMenus WHERE Code = 'NAV_UTILITY_PRINTER_SETUP';

IF @PrinterMenuId IS NULL
BEGIN
    RAISERROR('Failed to create/find NAV_UTILITY_PRINTER_SETUP in dbo.NavigationMenus.', 16, 1);
END

-- Copy permissions from Table Sections (recommended default for Utility children)
IF @BaselineMenuId IS NOT NULL
BEGIN
    INSERT INTO dbo.RoleMenuPermissions
    (
        RoleId, MenuId,
        CanView, CanAdd, CanEdit, CanDelete, CanApprove, CanPrint, CanExport,
        CreatedAt, CreatedBy, UpdatedAt, UpdatedBy
    )
    SELECT
        rmp.RoleId,
        @PrinterMenuId,
        rmp.CanView,
        rmp.CanAdd,
        rmp.CanEdit,
        rmp.CanDelete,
        rmp.CanApprove,
        rmp.CanPrint,
        rmp.CanExport,
        SYSUTCDATETIME(),
        rmp.CreatedBy,
        SYSUTCDATETIME(),
        rmp.UpdatedBy
    FROM dbo.RoleMenuPermissions rmp
    WHERE rmp.MenuId = @BaselineMenuId
      AND rmp.CanView = 1
      AND NOT EXISTS (
            SELECT 1
            FROM dbo.RoleMenuPermissions existing
            WHERE existing.RoleId = rmp.RoleId
              AND existing.MenuId = @PrinterMenuId
      );
END

COMMIT TRANSACTION;

PRINT 'NAV_UTILITY_PRINTER_SETUP inserted/updated successfully.';
GO
