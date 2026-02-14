IF OBJECT_ID(N'dbo.Branches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Branches (
        BranchId INT IDENTITY PRIMARY KEY,
        BranchCode NVARCHAR(20) UNIQUE NOT NULL,
        BranchName NVARCHAR(150) NOT NULL,
        Is_MainBranch BIT,
        IsActive BIT DEFAULT 1,
        CreatedAt DATETIME DEFAULT GETDATE(),
        UpdatedAt DATETIME NULL
    );
END;
