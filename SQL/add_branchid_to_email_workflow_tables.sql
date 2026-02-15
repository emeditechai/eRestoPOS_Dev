-- =====================================================
-- Branch-wise migration for Email workflow tables
-- Tables: tbl_EmailTemplates, tbl_EmailLog, tbl_EmailCampaignHistory
-- =====================================================

SET NOCOUNT ON;

DECLARE @DefaultBranchId INT;
DECLARE @Sql NVARCHAR(MAX);
SELECT TOP 1 @DefaultBranchId = BranchId
FROM dbo.Branches
ORDER BY CASE WHEN ISNULL(Is_MainBranch, 0) = 1 THEN 0 ELSE 1 END, BranchId;

IF @DefaultBranchId IS NULL
BEGIN
    SELECT TOP 1 @DefaultBranchId = BranchId FROM dbo.Branches ORDER BY BranchId;
END;

PRINT 'Using default BranchId: ' + ISNULL(CAST(@DefaultBranchId AS NVARCHAR(20)), 'NULL');

-- tbl_EmailTemplates
IF COL_LENGTH('dbo.tbl_EmailTemplates', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.tbl_EmailTemplates ADD BranchId INT NULL;
    PRINT 'Added BranchId to tbl_EmailTemplates';
END

SET @Sql = N'UPDATE dbo.tbl_EmailTemplates
SET BranchId = ISNULL(BranchId, @DefaultBranchId)
WHERE BranchId IS NULL;';
EXEC sp_executesql @Sql, N'@DefaultBranchId INT', @DefaultBranchId;

IF COL_LENGTH('dbo.tbl_EmailTemplates', 'BranchId') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID('dbo.tbl_EmailTemplates')
          AND name = 'IX_tbl_EmailTemplates_BranchId'
    )
    BEGIN
        SET @Sql = N'CREATE INDEX IX_tbl_EmailTemplates_BranchId
                     ON dbo.tbl_EmailTemplates(BranchId);';
        EXEC sp_executesql @Sql;
        PRINT 'Created IX_tbl_EmailTemplates_BranchId';
    END
END

-- tbl_EmailLog
IF COL_LENGTH('dbo.tbl_EmailLog', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.tbl_EmailLog ADD BranchId INT NULL;
    PRINT 'Added BranchId to tbl_EmailLog';
END

SET @Sql = N'UPDATE dbo.tbl_EmailLog
SET BranchId = ISNULL(BranchId, @DefaultBranchId)
WHERE BranchId IS NULL;';
EXEC sp_executesql @Sql, N'@DefaultBranchId INT', @DefaultBranchId;

IF COL_LENGTH('dbo.tbl_EmailLog', 'BranchId') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID('dbo.tbl_EmailLog')
          AND name = 'IX_tbl_EmailLog_BranchId_SentAt'
    )
    BEGIN
        SET @Sql = N'CREATE INDEX IX_tbl_EmailLog_BranchId_SentAt
                     ON dbo.tbl_EmailLog(BranchId, SentAt DESC);';
        EXEC sp_executesql @Sql;
        PRINT 'Created IX_tbl_EmailLog_BranchId_SentAt';
    END
END

-- tbl_EmailCampaignHistory
IF COL_LENGTH('dbo.tbl_EmailCampaignHistory', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.tbl_EmailCampaignHistory ADD BranchId INT NULL;
    PRINT 'Added BranchId to tbl_EmailCampaignHistory';
END

SET @Sql = N'UPDATE dbo.tbl_EmailCampaignHistory
SET BranchId = ISNULL(BranchId, @DefaultBranchId)
WHERE BranchId IS NULL;';
EXEC sp_executesql @Sql, N'@DefaultBranchId INT', @DefaultBranchId;

IF COL_LENGTH('dbo.tbl_EmailCampaignHistory', 'BranchId') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID('dbo.tbl_EmailCampaignHistory')
          AND name = 'IX_tbl_EmailCampaignHistory_BranchId_SentAt'
    )
    BEGIN
        SET @Sql = N'CREATE INDEX IX_tbl_EmailCampaignHistory_BranchId_SentAt
                     ON dbo.tbl_EmailCampaignHistory(BranchId, SentAt DESC);';
        EXEC sp_executesql @Sql;
        PRINT 'Created IX_tbl_EmailCampaignHistory_BranchId_SentAt';
    END
END

PRINT 'Branch-wise email workflow migration completed.';
