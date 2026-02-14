/*
    User-Branch mapping table
    - Allows one user to access one or multiple branches
    - Supports a default branch per user
*/

SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.UserBranches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserBranches
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserBranches PRIMARY KEY,
        UserId INT NOT NULL,
        BranchId INT NOT NULL,
        IsDefault BIT NOT NULL CONSTRAINT DF_UserBranches_IsDefault DEFAULT (0),
        IsActive BIT NOT NULL CONSTRAINT DF_UserBranches_IsActive DEFAULT (1),
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_UserBranches_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2(3) NULL,
        CONSTRAINT UQ_UserBranches_UserId_BranchId UNIQUE (UserId, BranchId),
        CONSTRAINT FK_UserBranches_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_UserBranches_Branches FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId)
    );

    CREATE INDEX IX_UserBranches_UserId ON dbo.UserBranches(UserId);
    CREATE INDEX IX_UserBranches_BranchId ON dbo.UserBranches(BranchId);
END
GO
