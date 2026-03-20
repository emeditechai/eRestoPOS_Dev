-- ============================================================
-- Migration: Add "Menu Item Rate Edit" page under Utility nav
-- Feature  : Branch-wise bulk menu item rate edit page
-- Deploy   : Run once on each environment (dev, staging, prod)
-- Safe     : Idempotent – checks existence before inserting
-- Date     : 2026-03-20
-- ============================================================
SET NOCOUNT ON;

DECLARE @UtilityMenuId   INT;
DECLARE @RateEditMenuId  INT;

-- Resolve NAV_UTILITY parent
SELECT @UtilityMenuId = Id FROM dbo.NavigationMenus WHERE Code = 'NAV_UTILITY';

IF @UtilityMenuId IS NULL
BEGIN
    RAISERROR('NAV_UTILITY not found in dbo.NavigationMenus. Run create_utility_navigation.sql first.', 16, 1);
    RETURN;
END

-- ── Insert nav entry if not already present ─────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_UTILITY_MENU_RATE_EDIT')
BEGIN
    INSERT INTO dbo.NavigationMenus
    (
        Code, ParentCode, DisplayName, Description, Area,
        ControllerName, ActionName, RouteValues, CustomUrl,
        IconCss, DisplayOrder, IsActive, IsVisible
    )
    VALUES
    (
        'NAV_UTILITY_MENU_RATE_EDIT',   -- Code
        'NAV_UTILITY',                  -- ParentCode
        'Menu Item Rate Edit',          -- DisplayName
        'Branch-wise bulk edit for Menu Item Base, Takeout, Delivery & Room Service rates',
        NULL,                           -- Area
        'Utility',                      -- ControllerName
        'MenuItemRateEdit',             -- ActionName
        NULL,                           -- RouteValues
        NULL,                           -- CustomUrl
        'fas fa-tags compact-icon text-success', -- IconCss
        20,                             -- DisplayOrder (after existing items)
        1,                              -- IsActive
        1                               -- IsVisible
    );
    PRINT 'NAV_UTILITY_MENU_RATE_EDIT navigation entry created.';
END
ELSE
BEGIN
    -- Ensure controller/action are up-to-date
    UPDATE dbo.NavigationMenus
    SET    ControllerName = 'Utility',
           ActionName     = 'MenuItemRateEdit',
           DisplayName    = 'Menu Item Rate Edit',
           IsActive       = 1,
           IsVisible      = 1
    WHERE  Code = 'NAV_UTILITY_MENU_RATE_EDIT';
    PRINT 'NAV_UTILITY_MENU_RATE_EDIT already exists – updated controller/action.';
END

-- ── Copy role permissions from a baseline Utility item (optional) ─
-- Copies CanView=1 permissions from any existing NAV_UTILITY child to the new entry.
-- Safe: won't duplicate if already exists.
SELECT @RateEditMenuId = Id FROM dbo.NavigationMenus WHERE Code = 'NAV_UTILITY_MENU_RATE_EDIT';

IF @RateEditMenuId IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.RoleMenuPermissions WHERE MenuId IN (SELECT Id FROM dbo.NavigationMenus WHERE ParentCode = 'NAV_UTILITY'))
BEGIN
    INSERT INTO dbo.RoleMenuPermissions
    (
        RoleId, MenuId,
        CanView, CanAdd, CanEdit, CanDelete, CanApprove, CanPrint, CanExport,
        CreatedAt, CreatedBy, UpdatedAt, UpdatedBy
    )
    SELECT
        rmp.RoleId,
        @RateEditMenuId,
        rmp.CanView,
        rmp.CanAdd,
        rmp.CanEdit,
        rmp.CanDelete,
        0, 0, 0,
        SYSUTCDATETIME(),
        rmp.CreatedBy,
        SYSUTCDATETIME(),
        rmp.UpdatedBy
    FROM dbo.RoleMenuPermissions rmp
    WHERE rmp.MenuId = @UtilityMenuId
      AND rmp.CanView = 1
      AND NOT EXISTS (
            SELECT 1 FROM dbo.RoleMenuPermissions x
            WHERE x.RoleId = rmp.RoleId AND x.MenuId = @RateEditMenuId
      );
    PRINT 'Role permissions seeded from NAV_UTILITY parent.';
END

-- ── Verify ──────────────────────────────────────────────────
SELECT Code, DisplayName, ControllerName, ActionName, DisplayOrder, IsActive
FROM   dbo.NavigationMenus
WHERE  Code = 'NAV_UTILITY_MENU_RATE_EDIT';
GO
