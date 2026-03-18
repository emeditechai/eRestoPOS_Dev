-- =============================================================================
-- Backfill: Copy RestaurantSettings from Main Branch to all existing branches
--           that do not yet have a settings row.
--
-- Run ONCE on the target database after deploying the Branch auto-copy feature.
-- Safe to re-run – skips branches that already have a settings row.
-- =============================================================================

SET NOCOUNT ON;

-- ── Sanity checks ─────────────────────────────────────────────────────────────

IF OBJECT_ID(N'dbo.Branches', N'U') IS NULL
BEGIN
    PRINT 'ERROR: dbo.Branches table does not exist. Nothing to do.';
    RETURN;
END

IF OBJECT_ID(N'dbo.RestaurantSettings', N'U') IS NULL
BEGIN
    PRINT 'ERROR: dbo.RestaurantSettings table does not exist. Nothing to do.';
    RETURN;
END

IF COL_LENGTH(N'dbo.RestaurantSettings', N'BranchId') IS NULL
BEGIN
    PRINT 'ERROR: dbo.RestaurantSettings.BranchId column does not exist.';
    PRINT '       Run add_branchid_to_settings_mail_upi.sql first.';
    RETURN;
END

-- ── Identify source row (main branch settings, else first available) ───────────

DECLARE @SourceBranchId INT;

SELECT TOP 1 @SourceBranchId = rs.BranchId
FROM dbo.RestaurantSettings rs
LEFT JOIN dbo.Branches b ON b.BranchId = rs.BranchId
ORDER BY CASE WHEN ISNULL(b.Is_MainBranch, 0) = 1 THEN 0 ELSE 1 END, rs.Id;

IF @SourceBranchId IS NULL
BEGIN
    PRINT 'ERROR: No source row found in dbo.RestaurantSettings. Nothing to copy.';
    RETURN;
END

PRINT 'Source settings row: BranchId = ' + CAST(@SourceBranchId AS NVARCHAR);

-- ── Build dynamic column list (excludes Id, BranchId, CreatedAt, UpdatedAt) ───

DECLARE @ColList NVARCHAR(MAX) = N'';

SELECT @ColList = @ColList + QUOTENAME(COLUMN_NAME) + N', '
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo'
  AND TABLE_NAME   = 'RestaurantSettings'
  AND COLUMN_NAME NOT IN ('Id', 'BranchId', 'CreatedAt', 'UpdatedAt')
ORDER BY ORDINAL_POSITION;

-- Remove trailing comma+space
SET @ColList = LEFT(@ColList, LEN(@ColList) - 1);

-- ── Insert missing settings rows ───────────────────────────────────────────────

DECLARE @Sql NVARCHAR(MAX);

SET @Sql = N'
INSERT INTO dbo.RestaurantSettings (BranchId, ' + @ColList + N', CreatedAt, UpdatedAt)
SELECT
    b.BranchId,
    ' + @ColList + N',
    GETDATE(),
    GETDATE()
FROM dbo.Branches b
CROSS JOIN (
    SELECT TOP 1 ' + @ColList + N'
    FROM dbo.RestaurantSettings
    WHERE BranchId = @SourceBranchId
) AS src
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.RestaurantSettings rs
    WHERE rs.BranchId = b.BranchId
);';

EXEC sp_executesql @Sql,
     N'@SourceBranchId INT',
     @SourceBranchId = @SourceBranchId;

DECLARE @Inserted INT = @@ROWCOUNT;

PRINT 'Done. Settings rows created for ' + CAST(@Inserted AS NVARCHAR) + ' branch(es).';

-- ── Verification ───────────────────────────────────────────────────────────────

SELECT
    b.BranchId,
    b.BranchCode,
    b.BranchName,
    CASE WHEN ISNULL(b.Is_MainBranch, 0) = 1 THEN 'Yes' ELSE 'No' END AS IsMainBranch,
    CASE WHEN rs.Id IS NOT NULL THEN 'Yes' ELSE 'MISSING' END          AS HasSettings,
    rs.Id                                                               AS SettingsId
FROM dbo.Branches b
LEFT JOIN dbo.RestaurantSettings rs ON rs.BranchId = b.BranchId
ORDER BY b.BranchId;
