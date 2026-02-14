/*
    Add Branch Master page under Settings navigation
    - Adds NavigationMenus entry under NAV_SETTINGS
    - Copies role permissions from NAV_SETTINGS_RESTAURANT where available

    Page endpoints: Master/BranchList
*/

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

DECLARE @SettingsMenuId INT;
DECLARE @BaselineMenuId INT;
DECLARE @BranchMenuId INT;

SELECT @SettingsMenuId = Id FROM dbo.NavigationMenus WHERE Code = 'NAV_SETTINGS';
SELECT @BaselineMenuId = Id FROM dbo.NavigationMenus WHERE Code = 'NAV_SETTINGS_RESTAURANT';

IF @SettingsMenuId IS NULL
BEGIN
    RAISERROR('NAV_SETTINGS menu not found in dbo.NavigationMenus. Run create_navigation_permissions.sql first.', 16, 1);
END

IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_SETTINGS_BRANCH_MASTER')
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
        'NAV_SETTINGS_BRANCH_MASTER',
        'NAV_SETTINGS',
        'Branch Master',
        'Branch Master - add/edit branches',
        NULL,
        'Master',
        'BranchList',
        NULL,
        NULL,
        'fas fa-code-branch compact-icon text-primary',
        2,
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
    SET ParentCode = 'NAV_SETTINGS',
        DisplayName = 'Branch Master',
        ControllerName = 'Master',
        ActionName = 'BranchList',
        IsActive = 1,
        IsVisible = 1,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Code = 'NAV_SETTINGS_BRANCH_MASTER';
END

SELECT @BranchMenuId = Id FROM dbo.NavigationMenus WHERE Code = 'NAV_SETTINGS_BRANCH_MASTER';

IF @BranchMenuId IS NULL
BEGIN
    RAISERROR('Failed to create/find NAV_SETTINGS_BRANCH_MASTER in dbo.NavigationMenus.', 16, 1);
END

-- Copy permissions from Restaurant Settings (recommended default)
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
        @BranchMenuId,
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
              AND existing.MenuId = @BranchMenuId
      );
END
ELSE
BEGIN
    -- Fallback: grant administrators (if present)
    DECLARE @AdminRoleId INT;
    SELECT @AdminRoleId = Id FROM dbo.Roles WHERE Name = 'Administrator';

    IF @AdminRoleId IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.RoleMenuPermissions WHERE RoleId = @AdminRoleId AND MenuId = @BranchMenuId)
        BEGIN
            INSERT INTO dbo.RoleMenuPermissions (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete, CanApprove, CanPrint, CanExport, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
            VALUES (@AdminRoleId, @BranchMenuId, 1, 1, 1, 1, 1, 1, 1, SYSUTCDATETIME(), NULL, SYSUTCDATETIME(), NULL);
        END
    END
END

COMMIT TRANSACTION;
GO

-- Verify
SELECT Code, ParentCode, DisplayName, ControllerName, ActionName, DisplayOrder, IsActive, IsVisible
FROM dbo.NavigationMenus
WHERE Code = 'NAV_SETTINGS_BRANCH_MASTER';
GO
