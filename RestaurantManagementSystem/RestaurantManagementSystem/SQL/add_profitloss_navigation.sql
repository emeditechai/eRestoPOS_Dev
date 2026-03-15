-- ============================================================
-- Script   : add_profitloss_navigation.sql
-- Purpose  : Seed NAV_REPORTS_PROFITLOSS navigation entry
--            and grant Administrator role full permissions.
-- Safe     : Idempotent – can be run multiple times.
-- Deployed : 2026-03-15
-- ============================================================

IF OBJECT_ID(N'dbo.NavigationMenus', N'U') IS NULL
BEGIN
    PRINT 'NavigationMenus table not found. Skipping.';
    RETURN;
END

-- ── Insert nav menu entry ────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_REPORTS_PROFITLOSS')
BEGIN
    INSERT INTO dbo.NavigationMenus
           (Code, ParentCode, DisplayName, Description, Area,
            ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
            DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
    VALUES ('NAV_REPORTS_PROFITLOSS', 'NAV_REPORTS', 'P&L Analysis',
            'Profit & Loss Analysis Report with BOM-based ingredient costing', NULL,
            'Reports', 'ProfitLoss', NULL, NULL,
            'fas fa-chart-line compact-icon text-success',
            99, 1, 1, NULL, NULL, 0);

    PRINT 'NAV_REPORTS_PROFITLOSS nav entry inserted.';
END
ELSE
BEGIN
    PRINT 'NAV_REPORTS_PROFITLOSS already exists. Skipping insert.';
END

-- ── Grant full permissions to Administrator role ─────────────
DECLARE @AdminRoleId INT = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Administrator');

IF @AdminRoleId IS NOT NULL
BEGIN
    INSERT INTO dbo.RoleMenuPermissions
           (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete,
            CanApprove, CanPrint, CanExport, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
    SELECT @AdminRoleId, nm.Id, 1, 1, 1, 1, 1, 1, 1,
           SYSUTCDATETIME(), 0, SYSUTCDATETIME(), 0
    FROM   dbo.NavigationMenus nm
    WHERE  nm.Code = 'NAV_REPORTS_PROFITLOSS'
      AND  NOT EXISTS (
               SELECT 1 FROM dbo.RoleMenuPermissions rmp
               WHERE  rmp.RoleId = @AdminRoleId AND rmp.MenuId = nm.Id);

    PRINT 'Administrator permissions granted for NAV_REPORTS_PROFITLOSS.';
END
ELSE
BEGIN
    PRINT 'Administrator role not found. Skipping permission grant.';
END
