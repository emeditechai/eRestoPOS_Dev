SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @DefaultBranchId INT;
    DECLARE @Sql NVARCHAR(MAX);
    SELECT TOP 1 @DefaultBranchId = BranchId
    FROM dbo.Branches
    WHERE ISNULL(IsActive, 1) = 1
    ORDER BY CASE WHEN ISNULL(Is_MainBranch, 0) = 1 THEN 0 ELSE 1 END, BranchId;

    IF OBJECT_ID(N'dbo.Ingredients', N'U') IS NOT NULL
    BEGIN
        IF COL_LENGTH('dbo.Ingredients', 'BranchId') IS NULL
            ALTER TABLE dbo.Ingredients ADD BranchId INT NULL;

        IF COL_LENGTH('dbo.Ingredients', 'BranchId') IS NOT NULL
        BEGIN
            IF @DefaultBranchId IS NOT NULL
            BEGIN
                SET @Sql = N'UPDATE dbo.Ingredients SET BranchId = @DefaultBranchId WHERE BranchId IS NULL;';
                EXEC sp_executesql @Sql, N'@DefaultBranchId INT', @DefaultBranchId;
            END

            SET @Sql = N'UPDATE i
                         SET i.BranchId = NULL
                         FROM dbo.Ingredients i
                         LEFT JOIN dbo.Branches b ON b.BranchId = i.BranchId
                         WHERE i.BranchId IS NOT NULL AND b.BranchId IS NULL;';
            EXEC sp_executesql @Sql;
        END

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Ingredients_Branches')
            ALTER TABLE dbo.Ingredients ADD CONSTRAINT FK_Ingredients_Branches FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Ingredients') AND name = N'IX_Ingredients_BranchId')
            CREATE INDEX IX_Ingredients_BranchId ON dbo.Ingredients(BranchId);
    END

    IF OBJECT_ID(N'dbo.Counters', N'U') IS NOT NULL
    BEGIN
        IF COL_LENGTH('dbo.Counters', 'BranchId') IS NULL
            ALTER TABLE dbo.Counters ADD BranchId INT NULL;

        IF COL_LENGTH('dbo.Counters', 'BranchId') IS NOT NULL
        BEGIN
            IF @DefaultBranchId IS NOT NULL
            BEGIN
                SET @Sql = N'UPDATE dbo.Counters SET BranchId = @DefaultBranchId WHERE BranchId IS NULL;';
                EXEC sp_executesql @Sql, N'@DefaultBranchId INT', @DefaultBranchId;
            END

            SET @Sql = N'UPDATE c
                         SET c.BranchId = NULL
                         FROM dbo.Counters c
                         LEFT JOIN dbo.Branches b ON b.BranchId = c.BranchId
                         WHERE c.BranchId IS NOT NULL AND b.BranchId IS NULL;';
            EXEC sp_executesql @Sql;
        END

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Counters_Branches')
            ALTER TABLE dbo.Counters ADD CONSTRAINT FK_Counters_Branches FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Counters') AND name = N'IX_Counters_BranchId')
            CREATE INDEX IX_Counters_BranchId ON dbo.Counters(BranchId);

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Counters') AND name = N'UX_Counters_CounterCode')
            DROP INDEX UX_Counters_CounterCode ON dbo.Counters;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Counters') AND name = N'UX_Counters_Branch_CounterCode')
            CREATE UNIQUE INDEX UX_Counters_Branch_CounterCode ON dbo.Counters(BranchId, CounterCode);
    END

    IF OBJECT_ID(N'dbo.Tables', N'U') IS NOT NULL
    BEGIN
        IF COL_LENGTH('dbo.Tables', 'BranchId') IS NULL
            ALTER TABLE dbo.Tables ADD BranchId INT NULL;

        IF COL_LENGTH('dbo.Tables', 'BranchId') IS NOT NULL
        BEGIN
            IF @DefaultBranchId IS NOT NULL
            BEGIN
                SET @Sql = N'UPDATE dbo.Tables SET BranchId = @DefaultBranchId WHERE BranchId IS NULL;';
                EXEC sp_executesql @Sql, N'@DefaultBranchId INT', @DefaultBranchId;
            END

            SET @Sql = N'UPDATE t
                         SET t.BranchId = NULL
                         FROM dbo.Tables t
                         LEFT JOIN dbo.Branches b ON b.BranchId = t.BranchId
                         WHERE t.BranchId IS NOT NULL AND b.BranchId IS NULL;';
            EXEC sp_executesql @Sql;
        END

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Tables_Branches')
            ALTER TABLE dbo.Tables ADD CONSTRAINT FK_Tables_Branches FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Tables') AND name = N'IX_Tables_BranchId')
            CREATE INDEX IX_Tables_BranchId ON dbo.Tables(BranchId);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Tables') AND name = N'UX_Tables_Branch_TableNumber')
            CREATE UNIQUE INDEX UX_Tables_Branch_TableNumber ON dbo.Tables(BranchId, TableNumber);
    END

    IF OBJECT_ID(N'dbo.TableSections', N'U') IS NOT NULL
    BEGIN
        IF COL_LENGTH('dbo.TableSections', 'BranchId') IS NULL
            ALTER TABLE dbo.TableSections ADD BranchId INT NULL;

        IF COL_LENGTH('dbo.TableSections', 'BranchId') IS NOT NULL
        BEGIN
            IF @DefaultBranchId IS NOT NULL
            BEGIN
                SET @Sql = N'UPDATE dbo.TableSections SET BranchId = @DefaultBranchId WHERE BranchId IS NULL;';
                EXEC sp_executesql @Sql, N'@DefaultBranchId INT', @DefaultBranchId;
            END

            SET @Sql = N'UPDATE ts
                         SET ts.BranchId = NULL
                         FROM dbo.TableSections ts
                         LEFT JOIN dbo.Branches b ON b.BranchId = ts.BranchId
                         WHERE ts.BranchId IS NOT NULL AND b.BranchId IS NULL;';
            EXEC sp_executesql @Sql;
        END

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TableSections_Branches')
            ALTER TABLE dbo.TableSections ADD CONSTRAINT FK_TableSections_Branches FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TableSections') AND name = N'IX_TableSections_BranchId')
            CREATE INDEX IX_TableSections_BranchId ON dbo.TableSections(BranchId);

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TableSections') AND name = N'UQ__TableSec__737584F66AF4EA95')
            DROP INDEX UQ__TableSec__737584F66AF4EA95 ON dbo.TableSections;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TableSections') AND name = N'UX_TableSections_Branch_Name')
            CREATE UNIQUE INDEX UX_TableSections_Branch_Name ON dbo.TableSections(BranchId, Name);
    END

    IF OBJECT_ID(N'dbo.MenuItems', N'U') IS NOT NULL
    BEGIN
        IF COL_LENGTH('dbo.MenuItems', 'BranchId') IS NULL
            ALTER TABLE dbo.MenuItems ADD BranchId INT NULL;

        IF COL_LENGTH('dbo.MenuItems', 'BranchId') IS NOT NULL
        BEGIN
            IF @DefaultBranchId IS NOT NULL
            BEGIN
                SET @Sql = N'UPDATE dbo.MenuItems SET BranchId = @DefaultBranchId WHERE BranchId IS NULL;';
                EXEC sp_executesql @Sql, N'@DefaultBranchId INT', @DefaultBranchId;
            END

            SET @Sql = N'UPDATE m
        
                IF COL_LENGTH('dbo.MenuItems', 'PLUCodeNormalized') IS NULL
                    ALTER TABLE dbo.MenuItems ADD PLUCodeNormalized AS UPPER(LTRIM(RTRIM(ISNULL(PLUCode, '')))) PERSISTED;
                         SET m.BranchId = NULL
                         FROM dbo.MenuItems m
                         LEFT JOIN dbo.Branches b ON b.BranchId = m.BranchId
                         WHERE m.BranchId IS NOT NULL AND b.BranchId IS NULL;';
            EXEC sp_executesql @Sql;
        END

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.MenuItems') AND name = N'UX_MenuItems_Branch_PLUCode')
                    DROP INDEX UX_MenuItems_Branch_PLUCode ON dbo.MenuItems;

                IF EXISTS (
                    SELECT 1
                    FROM dbo.MenuItems
                    WHERE BranchId IS NOT NULL
                      AND UPPER(LTRIM(RTRIM(ISNULL(PLUCode, '')))) <> ''
                    GROUP BY BranchId, UPPER(LTRIM(RTRIM(ISNULL(PLUCode, ''))))
                    HAVING COUNT(*) > 1
                )
                    THROW 51000, 'Duplicate PLU Code found within same branch. Resolve duplicates in dbo.MenuItems, then rerun migration.', 1;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.MenuItems') AND name = N'UX_MenuItems_Branch_PLUNormalized')
                    CREATE UNIQUE INDEX UX_MenuItems_Branch_PLUNormalized
                        ON dbo.MenuItems(BranchId, PLUCodeNormalized)
                        WHERE BranchId IS NOT NULL AND PLUCodeNormalized <> '';

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.MenuItems') AND name = N'IX_MenuItems_BranchId')
            CREATE INDEX IX_MenuItems_BranchId ON dbo.MenuItems(BranchId);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.MenuItems') AND name = N'UX_MenuItems_Branch_PLUCode')
            CREATE UNIQUE INDEX UX_MenuItems_Branch_PLUCode ON dbo.MenuItems(BranchId, PLUCode) WHERE PLUCode IS NOT NULL;
    END

    IF OBJECT_ID(N'dbo.Recipes', N'U') IS NOT NULL
    BEGIN
        IF COL_LENGTH('dbo.Recipes', 'BranchId') IS NULL
            ALTER TABLE dbo.Recipes ADD BranchId INT NULL;

        IF COL_LENGTH('dbo.Recipes', 'BranchId') IS NOT NULL
        BEGIN
            IF @DefaultBranchId IS NOT NULL
            BEGIN
                SET @Sql = N'UPDATE dbo.Recipes SET BranchId = @DefaultBranchId WHERE BranchId IS NULL;';
                EXEC sp_executesql @Sql, N'@DefaultBranchId INT', @DefaultBranchId;
            END

            SET @Sql = N'UPDATE r
                         SET r.BranchId = NULL
                         FROM dbo.Recipes r
                         LEFT JOIN dbo.Branches b ON b.BranchId = r.BranchId
                         WHERE r.BranchId IS NOT NULL AND b.BranchId IS NULL;';
            EXEC sp_executesql @Sql;
        END

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Recipes_Branches')
            ALTER TABLE dbo.Recipes ADD CONSTRAINT FK_Recipes_Branches FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Recipes') AND name = N'IX_Recipes_BranchId')
            CREATE INDEX IX_Recipes_BranchId ON dbo.Recipes(BranchId);
    END

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO
