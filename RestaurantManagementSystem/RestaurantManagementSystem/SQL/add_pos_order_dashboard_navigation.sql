-- ═══════════════════════════════════════════════════════════════════════════════
-- POS Order Dashboard Navigation Entry
-- Date    : 2026-04-26
-- Summary : Adds "POS Dashboard" link under the Orders menu, pointing to
--           Order/POSDashboard. Role permissions copied from NAV_ORDERS_DASH.
-- ═══════════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;

-- PRE-FLIGHT: verify NAV_ORDERS parent exists
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_ORDERS')
BEGIN
    RAISERROR('NAV_ORDERS not found. Run create_navigation_permissions.sql first.', 16, 1);
    RETURN;
END

-- ── 1. Insert / update NavigationMenus entry ─────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_POS_ORDER_DASH')
BEGIN
    INSERT INTO dbo.NavigationMenus
        (Code, ParentCode, DisplayName, Description, Area,
         ControllerName, ActionName, RouteValues, CustomUrl,
         IconCss, DisplayOrder, IsActive, IsVisible)
    VALUES
        ('NAV_POS_ORDER_DASH', 'NAV_ORDERS', 'POS Dashboard', NULL, NULL,
         'Order', 'POSDashboard', NULL, NULL,
         'fas fa-cash-register compact-icon text-warning', 5, 1, 1);

    PRINT 'NAV_POS_ORDER_DASH navigation entry inserted.';
END
ELSE
BEGIN
    UPDATE dbo.NavigationMenus SET
        ParentCode     = 'NAV_ORDERS',
        DisplayName    = 'POS Dashboard',
        ControllerName = 'Order',
        ActionName     = 'POSDashboard',
        IconCss        = 'fas fa-cash-register compact-icon text-warning',
        DisplayOrder   = 5,
        IsActive       = 1,
        IsVisible      = 1
    WHERE Code = 'NAV_POS_ORDER_DASH';

    PRINT 'NAV_POS_ORDER_DASH already exists — updated.';
END

-- ── 2. Copy role permissions from NAV_ORDERS_DASH ────────────────────────────
DECLARE @NewMenuId    INT;
DECLARE @SourceMenuId INT;

SELECT @NewMenuId    = Id FROM dbo.NavigationMenus WHERE Code = 'NAV_POS_ORDER_DASH';
SELECT @SourceMenuId = Id FROM dbo.NavigationMenus WHERE Code = 'NAV_ORDERS_DASH';

IF @NewMenuId IS NULL
BEGIN
    RAISERROR('NAV_POS_ORDER_DASH entry not found after insert. Check NavigationMenus schema.', 16, 1);
    RETURN;
END

IF @SourceMenuId IS NOT NULL
BEGIN
    INSERT INTO dbo.RoleMenuPermissions
        (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete,
         CanApprove, CanPrint, CanExport,
         CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
    SELECT
        src.RoleId,
        @NewMenuId,
        src.CanView,
        src.CanAdd,
        src.CanEdit,
        src.CanDelete,
        src.CanApprove,
        src.CanPrint,
        src.CanExport,
        SYSUTCDATETIME(), 0, SYSUTCDATETIME(), 0
    FROM dbo.RoleMenuPermissions src
    WHERE src.MenuId = @SourceMenuId
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RoleMenuPermissions ex
          WHERE ex.MenuId = @NewMenuId AND ex.RoleId = src.RoleId
      );

    PRINT CONCAT('Copied ', @@ROWCOUNT, ' role permission(s) from NAV_ORDERS_DASH to NAV_POS_ORDER_DASH.');
END
ELSE
BEGIN
    PRINT 'NAV_ORDERS_DASH not found — no permissions copied. Grant permissions manually in Role Management.';
END

-- ── 3. Verification ───────────────────────────────────────────────────────────
SELECT nm.Id, nm.Code, nm.DisplayName, nm.ParentCode, nm.ControllerName, nm.ActionName, nm.DisplayOrder
FROM   dbo.NavigationMenus nm
WHERE  nm.Code IN ('NAV_ORDERS', 'NAV_ORDERS_DASH', 'NAV_POS_ORDER_DASH')
ORDER  BY nm.DisplayOrder;

PRINT 'add_pos_order_dashboard_navigation.sql completed successfully.';
