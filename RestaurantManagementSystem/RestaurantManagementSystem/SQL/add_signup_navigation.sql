-- =============================================================================
-- Script  : add_signup_navigation.sql
-- Purpose : Adds the "Sign Up" navigation entry under the Settings menu,
--           seeds the from_Signup column on dbo.Users, and grants the
--           Administrator role full permissions on the new menu entry.
--
-- Safe    : Idempotent — can be re-run on any database without side effects.
-- Author  : eRestoPOS Auto-Migration
-- Date    : 2026-08-16
-- =============================================================================

PRINT '-- Step 1: Add from_Signup column to dbo.Users (if missing) --';
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Users', 'from_Signup') IS NULL
    BEGIN
        ALTER TABLE dbo.Users ADD from_Signup BIT NOT NULL DEFAULT 0;
        PRINT '  Column from_Signup added to dbo.Users.';
    END
    IF COL_LENGTH('dbo.Users', 'TermsAcceptedAt') IS NULL
    BEGIN
        ALTER TABLE dbo.Users ADD TermsAcceptedAt DATETIME NULL;
        PRINT '  Column TermsAcceptedAt added to dbo.Users.';
    END
    ELSE
        PRINT '  Column TermsAcceptedAt already exists in dbo.Users.';
END

PRINT '-- Step 1b: Add from_Signup column to dbo.Branches (if missing) --';
IF OBJECT_ID(N'dbo.Branches', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Branches', 'from_Signup') IS NULL
    BEGIN
        ALTER TABLE dbo.Branches ADD from_Signup BIT NOT NULL DEFAULT 0;
        PRINT '  Column from_Signup added to dbo.Branches.';
    END
    ELSE
        PRINT '  Column from_Signup already exists in dbo.Branches.';
END

-- =============================================================================
-- Step 2: Insert the Sign Up entry into NavigationMenus
-- =============================================================================
PRINT '-- Step 2: Seed NAV_SETTINGS_SIGNUP navigation entry --';

IF OBJECT_ID(N'dbo.NavigationMenus', N'U') IS NULL
BEGIN
    PRINT '  dbo.NavigationMenus not found – skipping navigation seed.';
END
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_SETTINGS')
BEGIN
    PRINT '  NAV_SETTINGS parent not found – skipping Sign Up seed.';
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_SETTINGS_SIGNUP')
    BEGIN
        INSERT INTO dbo.NavigationMenus
               (Code, ParentCode, DisplayName, Description, Area,
                ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
                DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
        VALUES ('NAV_SETTINGS_SIGNUP', 'NAV_SETTINGS', 'Sign Up',
                'Public sign-up page – create a new restaurant branch and admin account', NULL,
                'SignUp', 'Index', NULL, NULL,
                'fas fa-rocket compact-icon text-warning',
                99, 1, 1, NULL, NULL, 1);

        PRINT '  NAV_SETTINGS_SIGNUP inserted.';
    END
    ELSE
    BEGIN
        -- Ensure it is active and pointing at the right controller/action
        UPDATE dbo.NavigationMenus
        SET    IsActive = 1,
               IsVisible = 1,
               ControllerName = 'SignUp',
               ActionName     = 'Index',
               IconCss        = 'fas fa-rocket compact-icon text-warning',
               OpenInNewTab   = 1,
               UpdatedAt      = GETDATE()
        WHERE  Code = 'NAV_SETTINGS_SIGNUP';

        PRINT '  NAV_SETTINGS_SIGNUP already exists – updated to active.';
    END

    -- ==========================================================================
    -- Step 3: Grant Administrator role full permissions
    -- ==========================================================================
    PRINT '-- Step 3: Grant Administrator permissions on NAV_SETTINGS_SIGNUP --';

    DECLARE @AdminRoleId INT = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Administrator');
    IF @AdminRoleId IS NOT NULL
    BEGIN
        IF OBJECT_ID(N'dbo.RoleMenuPermissions', N'U') IS NOT NULL
        BEGIN
            INSERT INTO dbo.RoleMenuPermissions
                   (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete,
                    CanApprove, CanPrint, CanExport, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
            SELECT @AdminRoleId, nm.Id, 1, 1, 1, 1, 1, 1, 1,
                   GETDATE(), 0, GETDATE(), 0
            FROM   dbo.NavigationMenus nm
            WHERE  nm.Code = 'NAV_SETTINGS_SIGNUP'
              AND  NOT EXISTS (
                       SELECT 1 FROM dbo.RoleMenuPermissions rmp
                        WHERE rmp.RoleId = @AdminRoleId AND rmp.MenuId = nm.Id);

            PRINT '  Administrator role permissions granted.';
        END
        ELSE
            PRINT '  dbo.RoleMenuPermissions not found – skipping permission grant.';
    END
    ELSE
        PRINT '  Administrator role not found – skipping permission grant.';
END

-- =============================================================================
-- Verification: show inserted rows
-- =============================================================================
PRINT '';
PRINT '-- Verification --';
SELECT Code, ParentCode, DisplayName, ControllerName, ActionName, IsActive, IsVisible
FROM   dbo.NavigationMenus
WHERE  Code IN ('NAV_SETTINGS', 'NAV_SETTINGS_SIGNUP')
ORDER  BY Code;

PRINT 'Script completed successfully.';
