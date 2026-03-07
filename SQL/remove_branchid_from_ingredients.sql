-- =============================================================
--  Remove BranchId from dbo.Ingredients
--  Run this once on any database that still has the column.
--  The script is fully idempotent.
-- =============================================================

-- Drop the column only if it exists
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID('dbo.Ingredients')
      AND  name = 'BranchId'
)
BEGIN
    -- Drop any FK constraints that reference BranchId (if any)
    DECLARE @fk NVARCHAR(200);
    SELECT @fk = CONSTRAINT_NAME
    FROM   INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE
    WHERE  TABLE_NAME   = 'Ingredients'
      AND  COLUMN_NAME  = 'BranchId';
    IF @fk IS NOT NULL
        EXEC('ALTER TABLE dbo.Ingredients DROP CONSTRAINT ' + @fk);

    -- Drop any indexes that include BranchId
    DECLARE @idx NVARCHAR(200);
    DECLARE idx_cur CURSOR FOR
        SELECT i.name
        FROM   sys.indexes i
        JOIN   sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN   sys.columns c        ON c.object_id  = ic.object_id AND c.column_id = ic.column_id
        WHERE  i.object_id = OBJECT_ID('dbo.Ingredients')
          AND  c.name = 'BranchId';
    OPEN idx_cur;
    FETCH NEXT FROM idx_cur INTO @idx;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC('DROP INDEX ' + @idx + ' ON dbo.Ingredients');
        FETCH NEXT FROM idx_cur INTO @idx;
    END
    CLOSE idx_cur;
    DEALLOCATE idx_cur;

    -- Finally drop the column
    ALTER TABLE dbo.Ingredients DROP COLUMN BranchId;

    PRINT 'Column BranchId removed from dbo.Ingredients.';
END
ELSE
BEGIN
    PRINT 'Column BranchId does not exist in dbo.Ingredients – nothing to do.';
END
GO
