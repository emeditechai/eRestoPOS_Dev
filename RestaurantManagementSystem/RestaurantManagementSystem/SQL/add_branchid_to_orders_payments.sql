/*
    Idempotent migration: add and backfill BranchId on Orders and Payments
*/

SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Add Orders.BranchId if missing
    IF COL_LENGTH('dbo.Orders', 'BranchId') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD BranchId INT NULL;
    END

    -- Add Payments.BranchId if missing
    IF COL_LENGTH('dbo.Payments', 'BranchId') IS NULL
    BEGIN
        ALTER TABLE dbo.Payments ADD BranchId INT NULL;
    END

    -- Backfill Orders.BranchId from related table data where possible
    IF COL_LENGTH('dbo.Orders', 'BranchId') IS NOT NULL
    BEGIN
        IF OBJECT_ID('dbo.TableTurnovers', 'U') IS NOT NULL
           AND COL_LENGTH('dbo.TableTurnovers', 'BranchId') IS NOT NULL
           AND COL_LENGTH('dbo.Orders', 'TableTurnoverId') IS NOT NULL
        BEGIN
                        EXEC sp_executesql N'
                                UPDATE o
                                SET o.BranchId = tt.BranchId
                                FROM dbo.Orders o
                                INNER JOIN dbo.TableTurnovers tt ON tt.Id = o.TableTurnoverId
                                WHERE o.BranchId IS NULL
                                    AND tt.BranchId IS NOT NULL;';
        END

        IF OBJECT_ID('dbo.Tables', 'U') IS NOT NULL
           AND COL_LENGTH('dbo.Tables', 'BranchId') IS NOT NULL
           AND OBJECT_ID('dbo.TableTurnovers', 'U') IS NOT NULL
           AND COL_LENGTH('dbo.Orders', 'TableTurnoverId') IS NOT NULL
        BEGIN
                        EXEC sp_executesql N'
                                UPDATE o
                                SET o.BranchId = t.BranchId
                                FROM dbo.Orders o
                                INNER JOIN dbo.TableTurnovers tt ON tt.Id = o.TableTurnoverId
                                INNER JOIN dbo.Tables t ON t.Id = tt.TableId
                                WHERE o.BranchId IS NULL
                                    AND t.BranchId IS NOT NULL;';
        END

        IF OBJECT_ID('dbo.Branches', 'U') IS NOT NULL
           AND COL_LENGTH('dbo.Branches', 'BranchId') IS NOT NULL
        BEGIN
            DECLARE @DefaultBranchId INT;
            SELECT TOP (1) @DefaultBranchId = b.BranchId
            FROM dbo.Branches b
            ORDER BY b.BranchId;

            IF @DefaultBranchId IS NOT NULL
            BEGIN
                EXEC sp_executesql
                    N'UPDATE dbo.Orders
                      SET BranchId = @DefaultBranchId
                      WHERE BranchId IS NULL;',
                    N'@DefaultBranchId INT',
                    @DefaultBranchId;
            END
        END
    END

    -- Backfill Payments.BranchId from Orders.BranchId
    IF COL_LENGTH('dbo.Payments', 'BranchId') IS NOT NULL
       AND COL_LENGTH('dbo.Orders', 'BranchId') IS NOT NULL
    BEGIN
                EXEC sp_executesql N'
                        UPDATE p
                        SET p.BranchId = o.BranchId
                        FROM dbo.Payments p
                        INNER JOIN dbo.Orders o ON o.Id = p.OrderId
                        WHERE p.BranchId IS NULL
                            AND o.BranchId IS NOT NULL;';
    END

    -- Helpful indexes
    IF COL_LENGTH('dbo.Orders', 'BranchId') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Orders_BranchId' AND object_id = OBJECT_ID('dbo.Orders'))
    BEGIN
        EXEC sp_executesql N'CREATE INDEX IX_Orders_BranchId ON dbo.Orders(BranchId);';
    END

    IF COL_LENGTH('dbo.Payments', 'BranchId') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payments_BranchId' AND object_id = OBJECT_ID('dbo.Payments'))
    BEGIN
        EXEC sp_executesql N'CREATE INDEX IX_Payments_BranchId ON dbo.Payments(BranchId);';
    END

    -- Foreign keys to Branches when possible
    IF COL_LENGTH('dbo.Orders', 'BranchId') IS NOT NULL
       AND OBJECT_ID('dbo.Branches', 'U') IS NOT NULL
         AND COL_LENGTH('dbo.Branches', 'BranchId') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Orders_Branches_BranchId')
    BEGIN
        EXEC sp_executesql N'
            ALTER TABLE dbo.Orders WITH NOCHECK
            ADD CONSTRAINT FK_Orders_Branches_BranchId FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId);';
    END

    IF COL_LENGTH('dbo.Payments', 'BranchId') IS NOT NULL
       AND OBJECT_ID('dbo.Branches', 'U') IS NOT NULL
         AND COL_LENGTH('dbo.Branches', 'BranchId') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Payments_Branches_BranchId')
    BEGIN
        EXEC sp_executesql N'
            ALTER TABLE dbo.Payments WITH NOCHECK
            ADD CONSTRAINT FK_Payments_Branches_BranchId FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId);';
    END

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
