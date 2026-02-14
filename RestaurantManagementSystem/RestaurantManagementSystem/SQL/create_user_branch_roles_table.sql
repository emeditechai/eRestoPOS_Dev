IF OBJECT_ID(N'dbo.UserBranchRoles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserBranchRoles
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserBranchRoles PRIMARY KEY,
        UserId INT NOT NULL,
        BranchId INT NOT NULL,
        RoleId INT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_UserBranchRoles_IsActive DEFAULT(1),
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_UserBranchRoles_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2(3) NULL,
        CONSTRAINT UQ_UserBranchRoles_User_Branch_Role UNIQUE(UserId, BranchId, RoleId)
    );

    IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
        ALTER TABLE dbo.UserBranchRoles ADD CONSTRAINT FK_UserBranchRoles_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id);

    IF OBJECT_ID(N'dbo.Branches', N'U') IS NOT NULL
        ALTER TABLE dbo.UserBranchRoles ADD CONSTRAINT FK_UserBranchRoles_Branches FOREIGN KEY(BranchId) REFERENCES dbo.Branches(BranchId);

    IF OBJECT_ID(N'dbo.Roles', N'U') IS NOT NULL
        ALTER TABLE dbo.UserBranchRoles ADD CONSTRAINT FK_UserBranchRoles_Roles FOREIGN KEY(RoleId) REFERENCES dbo.Roles(Id);

    CREATE INDEX IX_UserBranchRoles_User_Branch ON dbo.UserBranchRoles(UserId, BranchId);
    CREATE INDEX IX_UserBranchRoles_RoleId ON dbo.UserBranchRoles(RoleId);
END
GO

-- Optional one-time backfill from existing UserRoles + UserBranches for legacy users
-- INSERT INTO dbo.UserBranchRoles (UserId, BranchId, RoleId, IsActive, CreatedAt, UpdatedAt)
-- SELECT ub.UserId, ub.BranchId, ur.RoleId, 1, SYSUTCDATETIME(), NULL
-- FROM dbo.UserBranches ub
-- INNER JOIN dbo.UserRoles ur ON ur.UserId = ub.UserId
-- LEFT JOIN dbo.UserBranchRoles ubr
--     ON ubr.UserId = ub.UserId
--     AND ubr.BranchId = ub.BranchId
--     AND ubr.RoleId = ur.RoleId
-- WHERE ubr.Id IS NULL;
