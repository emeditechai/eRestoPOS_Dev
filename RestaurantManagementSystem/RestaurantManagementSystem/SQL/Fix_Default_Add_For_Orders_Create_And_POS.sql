SET NOCOUNT ON;

-- Fix: if a role can see "Create Order" / "POS Order" menus (CanView=1)
-- but all action flags are still 0, default CanAdd=1 so POST/create endpoints work.
-- Safe to run multiple times.

IF OBJECT_ID('dbo.RoleMenuPermissions', 'U') IS NULL OR OBJECT_ID('dbo.NavigationMenus', 'U') IS NULL
BEGIN
    PRINT 'Required tables not found: dbo.RoleMenuPermissions and/or dbo.NavigationMenus.';
    RETURN;
END

DECLARE @Before int = 0;
DECLARE @After int = 0;

SELECT @Before = COUNT(1)
FROM dbo.RoleMenuPermissions rmp
INNER JOIN dbo.NavigationMenus nm ON nm.Id = rmp.MenuId
WHERE nm.Code IN ('NAV_ORDERS_CREATE', 'NAV_ORDERS_POS')
  AND ISNULL(rmp.CanView, 0) = 1
  AND ISNULL(rmp.CanAdd, 0) = 0
  AND ISNULL(rmp.CanEdit, 0) = 0
  AND ISNULL(rmp.CanDelete, 0) = 0
  AND ISNULL(rmp.CanApprove, 0) = 0
  AND ISNULL(rmp.CanPrint, 0) = 0
  AND ISNULL(rmp.CanExport, 0) = 0;

UPDATE rmp
SET rmp.CanAdd = 1,
    rmp.UpdatedAt = SYSUTCDATETIME(),
    rmp.UpdatedBy = 0
FROM dbo.RoleMenuPermissions rmp
INNER JOIN dbo.NavigationMenus nm ON nm.Id = rmp.MenuId
WHERE nm.Code IN ('NAV_ORDERS_CREATE', 'NAV_ORDERS_POS')
  AND ISNULL(rmp.CanView, 0) = 1
  AND ISNULL(rmp.CanAdd, 0) = 0
  AND ISNULL(rmp.CanEdit, 0) = 0
  AND ISNULL(rmp.CanDelete, 0) = 0
  AND ISNULL(rmp.CanApprove, 0) = 0
  AND ISNULL(rmp.CanPrint, 0) = 0
  AND ISNULL(rmp.CanExport, 0) = 0;

SELECT @After = COUNT(1)
FROM dbo.RoleMenuPermissions rmp
INNER JOIN dbo.NavigationMenus nm ON nm.Id = rmp.MenuId
WHERE nm.Code IN ('NAV_ORDERS_CREATE', 'NAV_ORDERS_POS')
  AND ISNULL(rmp.CanView, 0) = 1
  AND ISNULL(rmp.CanAdd, 0) = 0
  AND ISNULL(rmp.CanEdit, 0) = 0
  AND ISNULL(rmp.CanDelete, 0) = 0
  AND ISNULL(rmp.CanApprove, 0) = 0
  AND ISNULL(rmp.CanPrint, 0) = 0
  AND ISNULL(rmp.CanExport, 0) = 0;

PRINT CONCAT('Rows needing default CanAdd before: ', @Before);
PRINT CONCAT('Rows still needing default CanAdd after: ', @After);
PRINT 'Done.';
