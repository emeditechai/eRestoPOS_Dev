IF OBJECT_ID(N'dbo.NavigationMenus', N'U') IS NULL
BEGIN
    PRINT 'NavigationMenus table not found. Skipping waitlist report navigation seed.';
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM dbo.NavigationMenus WHERE Code = 'NAV_REPORTS_WAITLIST_GUESTS')
BEGIN
    INSERT INTO dbo.NavigationMenus
           (Code, ParentCode, DisplayName, Description, Area,
            ControllerName, ActionName, RouteValues, CustomUrl, IconCss,
            DisplayOrder, IsActive, IsVisible, ThemeColor, ShortcutHint, OpenInNewTab)
    VALUES ('NAV_REPORTS_WAITLIST_GUESTS', 'NAV_REPORTS', 'Waitlist Guest Report',
            'Waitlist and seated guest operational report by date range', NULL,
            'Reports', 'WaitlistGuestReport', NULL, NULL,
            'fas fa-chair compact-icon text-info',
            14, 1, 1, NULL, NULL, 0);

    PRINT 'NAV_REPORTS_WAITLIST_GUESTS nav entry inserted.';
END
ELSE
BEGIN
    PRINT 'NAV_REPORTS_WAITLIST_GUESTS already exists. Skipping insert.';
END

DECLARE @AdminRoleId INT = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Administrator');
IF @AdminRoleId IS NOT NULL
BEGIN
    INSERT INTO dbo.RoleMenuPermissions
           (RoleId, MenuId, CanView, CanAdd, CanEdit, CanDelete,
            CanApprove, CanPrint, CanExport, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
    SELECT @AdminRoleId, nm.Id, 1, 1, 1, 1, 1, 1, 1,
           SYSUTCDATETIME(), 0, SYSUTCDATETIME(), 0
    FROM dbo.NavigationMenus nm
    WHERE nm.Code = 'NAV_REPORTS_WAITLIST_GUESTS'
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RoleMenuPermissions rmp
          WHERE rmp.RoleId = @AdminRoleId AND rmp.MenuId = nm.Id);

    PRINT 'Administrator permissions granted for NAV_REPORTS_WAITLIST_GUESTS.';
END