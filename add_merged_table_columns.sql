-- Add missing merged-table columns to the Tables table
-- Safe to run multiple times (checks existence first)

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Tables' AND COLUMN_NAME = 'IsPartOfMergedOrder'
)
BEGIN
    ALTER TABLE dbo.Tables ADD IsPartOfMergedOrder BIT NOT NULL DEFAULT 0;
    PRINT 'Added column: IsPartOfMergedOrder';
END
ELSE
    PRINT 'Column already exists: IsPartOfMergedOrder';

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Tables' AND COLUMN_NAME = 'MergedTableNames'
)
BEGIN
    ALTER TABLE dbo.Tables ADD MergedTableNames NVARCHAR(MAX) NULL;
    PRINT 'Added column: MergedTableNames';
END
ELSE
    PRINT 'Column already exists: MergedTableNames';
