CREATE PROCEDURE [dbo].[usp_CreateOrUpdateUserWithRoles]
    @Id INT = 0,
    @Username NVARCHAR(50),
    @PasswordHash NVARCHAR(255),
    @Salt NVARCHAR(100),
    @FirstName NVARCHAR(50),
    @LastName NVARCHAR(50) = NULL,
    @Email NVARCHAR(100) = NULL,
    @Phone NVARCHAR(20) = NULL,
    @IsActive BIT = 1,
    @RoleIds NVARCHAR(MAX) = NULL, -- comma-separated role IDs
    @BranchIds NVARCHAR(MAX) = NULL -- comma-separated branch IDs
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @NewUserId INT;

    IF @Id = 0
    BEGIN
    INSERT INTO [dbo].[Users] (Username, PasswordHash, Salt, FirstName, LastName, Email, Phone, IsActive)
        VALUES (@Username, @PasswordHash, @Salt, @FirstName, @LastName, @Email, @Phone, @IsActive);
        SET @NewUserId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
    UPDATE [dbo].[Users]
        SET Username = @Username,
            PasswordHash = @PasswordHash,
            Salt = @Salt,
            FirstName = @FirstName,
            LastName = @LastName,
            Email = @Email,
            Phone = @Phone,
            IsActive = @IsActive
        WHERE Id = @Id;
        SET @NewUserId = @Id;
    END

    -- Remove existing role mappings
    DELETE FROM [dbo].[UserRoles] WHERE UserId = @NewUserId;

    -- Add new role mappings
    IF @RoleIds IS NOT NULL AND LEN(@RoleIds) > 0
    BEGIN
        DECLARE @RoleIdTable TABLE (RoleId INT);
        DECLARE @Pos INT = 0, @NextPos INT, @RoleId NVARCHAR(10);
        SET @RoleIds = @RoleIds + ',';
        WHILE CHARINDEX(',', @RoleIds, @Pos + 1) > 0
        BEGIN
            SET @NextPos = CHARINDEX(',', @RoleIds, @Pos + 1);
            SET @RoleId = SUBSTRING(@RoleIds, @Pos + 1, @NextPos - @Pos - 1);
            INSERT INTO @RoleIdTable (RoleId) VALUES (CAST(@RoleId AS INT));
            SET @Pos = @NextPos;
        END
    INSERT INTO [dbo].[UserRoles] (UserId, RoleId)
        SELECT @NewUserId, RoleId FROM @RoleIdTable;
    END

    IF OBJECT_ID(N'dbo.UserBranches', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.UserBranches
        (
            Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserBranches PRIMARY KEY,
            UserId INT NOT NULL,
            BranchId INT NOT NULL,
            IsDefault BIT NOT NULL CONSTRAINT DF_UserBranches_IsDefault DEFAULT(0),
            IsActive BIT NOT NULL CONSTRAINT DF_UserBranches_IsActive DEFAULT(1),
            CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_UserBranches_CreatedAt DEFAULT SYSUTCDATETIME(),
            UpdatedAt DATETIME2(3) NULL,
            CONSTRAINT UQ_UserBranches_UserId_BranchId UNIQUE(UserId, BranchId)
        );
    END

    DELETE FROM [dbo].[UserBranches] WHERE UserId = @NewUserId;

    IF @BranchIds IS NOT NULL AND LEN(@BranchIds) > 0
    BEGIN
        DECLARE @BranchIdTable TABLE (BranchId INT IDENTITY(1,1), Value INT);
        DECLARE @BPos INT = 0, @BNextPos INT, @BranchId NVARCHAR(10);
        DECLARE @DefaultBranchValue INT;
        SET @BranchIds = @BranchIds + ',';

        WHILE CHARINDEX(',', @BranchIds, @BPos + 1) > 0
        BEGIN
            SET @BNextPos = CHARINDEX(',', @BranchIds, @BPos + 1);
            SET @BranchId = SUBSTRING(@BranchIds, @BPos + 1, @BNextPos - @BPos - 1);
            INSERT INTO @BranchIdTable (Value) VALUES (CAST(@BranchId AS INT));
            SET @BPos = @BNextPos;
        END

        SELECT TOP 1 @DefaultBranchValue = Value FROM @BranchIdTable ORDER BY BranchId;

        INSERT INTO [dbo].[UserBranches] (UserId, BranchId, IsDefault, IsActive, CreatedAt, UpdatedAt)
        SELECT @NewUserId, Value, CASE WHEN Value = @DefaultBranchValue THEN 1 ELSE 0 END, 1, SYSUTCDATETIME(), NULL
        FROM @BranchIdTable;
    END

    SELECT @NewUserId AS UserId;
END
