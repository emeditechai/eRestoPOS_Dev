SET NOCOUNT ON;

DECLARE @MainBranchId INT = 1;

DECLARE @Sql NVARCHAR(MAX);

/* =========================
   RestaurantSettings
   ========================= */
IF COL_LENGTH('dbo.RestaurantSettings', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.RestaurantSettings ADD BranchId INT NULL;
END

IF @MainBranchId IS NOT NULL
BEGIN
    SET @Sql = N'UPDATE dbo.RestaurantSettings
                 SET BranchId = @MainBranchId
                 WHERE BranchId IS NULL;';
    EXEC sp_executesql @Sql, N'@MainBranchId INT', @MainBranchId;
END

SET @Sql = N';WITH cte AS
(
    SELECT Id,
           ROW_NUMBER() OVER (PARTITION BY BranchId ORDER BY Id DESC) AS rn
    FROM dbo.RestaurantSettings
    WHERE BranchId IS NOT NULL
)
DELETE FROM cte WHERE rn > 1;';
EXEC sp_executesql @Sql;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.RestaurantSettings')
      AND name = 'IX_RestaurantSettings_BranchId'
)
BEGIN
    SET @Sql = N'CREATE UNIQUE INDEX IX_RestaurantSettings_BranchId
                 ON dbo.RestaurantSettings(BranchId)
                 WHERE BranchId IS NOT NULL;';
    EXEC sp_executesql @Sql;
END

IF OBJECT_ID('dbo.Branches', 'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.foreign_keys
       WHERE name = 'FK_RestaurantSettings_Branches_BranchId'
   )
BEGIN
    SET @Sql = N'ALTER TABLE dbo.RestaurantSettings WITH NOCHECK
                 ADD CONSTRAINT FK_RestaurantSettings_Branches_BranchId
                 FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId);';
    EXEC sp_executesql @Sql;
END

/* =========================
   tbl_MailConfiguration
   ========================= */
IF COL_LENGTH('dbo.tbl_MailConfiguration', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.tbl_MailConfiguration ADD BranchId INT NULL;
END

IF @MainBranchId IS NOT NULL
BEGIN
    SET @Sql = N'UPDATE dbo.tbl_MailConfiguration
                 SET BranchId = @MainBranchId
                 WHERE BranchId IS NULL;';
    EXEC sp_executesql @Sql, N'@MainBranchId INT', @MainBranchId;
END

SET @Sql = N';WITH cte AS
(
    SELECT Id,
           ROW_NUMBER() OVER (PARTITION BY BranchId ORDER BY Id DESC) AS rn
    FROM dbo.tbl_MailConfiguration
    WHERE BranchId IS NOT NULL
)
DELETE FROM cte WHERE rn > 1;';
EXEC sp_executesql @Sql;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.tbl_MailConfiguration')
      AND name = 'IX_tbl_MailConfiguration_BranchId'
)
BEGIN
    SET @Sql = N'CREATE UNIQUE INDEX IX_tbl_MailConfiguration_BranchId
                 ON dbo.tbl_MailConfiguration(BranchId)
                 WHERE BranchId IS NOT NULL;';
    EXEC sp_executesql @Sql;
END

IF OBJECT_ID('dbo.Branches', 'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.foreign_keys
       WHERE name = 'FK_tbl_MailConfiguration_Branches_BranchId'
   )
BEGIN
    SET @Sql = N'ALTER TABLE dbo.tbl_MailConfiguration WITH NOCHECK
                 ADD CONSTRAINT FK_tbl_MailConfiguration_Branches_BranchId
                 FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId);';
    EXEC sp_executesql @Sql;
END

/* =========================
   UPISettings
   ========================= */
IF COL_LENGTH('dbo.UPISettings', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.UPISettings ADD BranchId INT NULL;
END

IF @MainBranchId IS NOT NULL
BEGIN
    SET @Sql = N'UPDATE dbo.UPISettings
                 SET BranchId = @MainBranchId
                 WHERE BranchId IS NULL;';
    EXEC sp_executesql @Sql, N'@MainBranchId INT', @MainBranchId;
END

SET @Sql = N';WITH cte AS
(
    SELECT Id,
           ROW_NUMBER() OVER (PARTITION BY BranchId ORDER BY Id DESC) AS rn
    FROM dbo.UPISettings
    WHERE BranchId IS NOT NULL
)
DELETE FROM cte WHERE rn > 1;';
EXEC sp_executesql @Sql;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.UPISettings')
      AND name = 'IX_UPISettings_BranchId'
)
BEGIN
    SET @Sql = N'CREATE UNIQUE INDEX IX_UPISettings_BranchId
                 ON dbo.UPISettings(BranchId)
                 WHERE BranchId IS NOT NULL;';
    EXEC sp_executesql @Sql;
END

IF OBJECT_ID('dbo.Branches', 'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.foreign_keys
       WHERE name = 'FK_UPISettings_Branches_BranchId'
   )
BEGIN
    SET @Sql = N'ALTER TABLE dbo.UPISettings WITH NOCHECK
                 ADD CONSTRAINT FK_UPISettings_Branches_BranchId
                 FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId);';
    EXEC sp_executesql @Sql;
END

PRINT 'Branch-wise migration completed for RestaurantSettings, tbl_MailConfiguration, and UPISettings.';
