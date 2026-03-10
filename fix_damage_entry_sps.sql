USE [dev_Restaurant];
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetDamageEntryList
    @BranchId INT,
    @Status   NVARCHAR(20) = NULL,
    @FromDate DATE = NULL,
    @ToDate   DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        de.DamageId, de.DamageNumber, de.BranchId, de.GodownId,
        de.DamageDate, de.DamageType, de.Remarks, de.Status,
        de.TotalQty, de.TotalValue,
        g.GodownName,
        de.CreatedAt
    FROM dbo.DamageEntry de
    INNER JOIN dbo.Godowns g ON g.Id = de.GodownId
    WHERE de.BranchId  = @BranchId
      AND (@Status   IS NULL OR de.Status     = @Status)
      AND (@FromDate IS NULL OR de.DamageDate >= @FromDate)
      AND (@ToDate   IS NULL OR de.DamageDate <= @ToDate)
    ORDER BY de.DamageDate DESC, de.DamageNumber DESC;
END;
GO

SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetDamageEntryById
    @DamageId INT
AS
BEGIN
    SET NOCOUNT ON;
    -- RS1: header
    SELECT
        de.DamageId, de.DamageNumber, de.BranchId, de.GodownId,
        de.DamageDate, de.DamageType, de.Remarks, de.Status,
        de.TotalQty, de.TotalValue, g.GodownName, de.CreatedAt
    FROM dbo.DamageEntry de
    INNER JOIN dbo.Godowns g ON g.Id = de.GodownId
    WHERE de.DamageId = @DamageId;

    -- RS2: lines
    SELECT
        dd.DamageDetailId, dd.DamageId, dd.ItemId, dd.UOMId,
        dd.Quantity, dd.UnitCost, dd.Reason,
        i.IngredientsName AS ItemName, u.UOMCode, u.UOMName
    FROM dbo.DamageEntryDetails dd
    INNER JOIN dbo.Ingredients i ON i.Id    = dd.ItemId
    INNER JOIN dbo.UomMaster   u ON u.UOMId = dd.UOMId
    WHERE dd.DamageId = @DamageId
    ORDER BY dd.DamageDetailId;
END;
GO

SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_SaveDamageEntry
    @DamageId    INT,
    @BranchId    INT,
    @GodownId    INT,
    @DamageDate  DATE,
    @DamageType  NVARCHAR(20)   = 'Damage',
    @Remarks     NVARCHAR(500)  = NULL,
    @UserId      INT            = NULL,
    @DetailsJson NVARCHAR(MAX)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @ActualId INT = @DamageId;

    IF @DamageId > 0
    BEGIN
        UPDATE dbo.DamageEntry
        SET GodownId   = @GodownId,
            DamageDate = @DamageDate,
            DamageType = @DamageType,
            Remarks    = @Remarks,
            UpdatedAt  = SYSUTCDATETIME()
        WHERE DamageId = @DamageId AND Status = 'Draft';

        DELETE FROM dbo.DamageEntryDetails WHERE DamageId = @DamageId;
    END
    ELSE
    BEGIN
        DECLARE @DamageNumber NVARCHAR(30) =
            'DMG-' + RIGHT('000' + CAST(@BranchId AS NVARCHAR(10)), 3) + '-' +
            FORMAT(GETDATE(), 'yyyyMMdd') + '-' +
            RIGHT('0000' + CAST(ISNULL((SELECT COUNT(*)+1 FROM dbo.DamageEntry WHERE BranchId=@BranchId),1) AS NVARCHAR(10)), 4);

        INSERT INTO dbo.DamageEntry
            (DamageNumber, BranchId, GodownId, DamageDate, DamageType, Status,
             Remarks, TotalQty, TotalValue, CreatedAt, CreatedBy)
        VALUES
            (@DamageNumber, @BranchId, @GodownId, @DamageDate, @DamageType, 'Draft',
             @Remarks, 0, 0, SYSUTCDATETIME(), @UserId);

        SET @ActualId = SCOPE_IDENTITY();
    END

    IF @DetailsJson IS NOT NULL AND LEN(@DetailsJson) > 2
    BEGIN
        INSERT INTO dbo.DamageEntryDetails (DamageId, ItemId, UOMId, Quantity, UnitCost, Reason)
        SELECT
            @ActualId,
            CAST(j.itemId   AS INT),
            CAST(j.uomId    AS INT),
            CAST(j.quantity AS DECIMAL(18,3)),
            CAST(j.unitCost AS DECIMAL(18,4)),
            j.reason
        FROM OPENJSON(@DetailsJson)
        WITH (
            itemId   INT            '$.itemId',
            uomId    INT            '$.uomId',
            quantity DECIMAL(18,3)  '$.quantity',
            unitCost DECIMAL(18,4)  '$.unitCost',
            reason   NVARCHAR(200)  '$.reason'
        ) j
        WHERE j.quantity > 0;

        UPDATE dbo.DamageEntry
        SET TotalQty   = (SELECT ISNULL(SUM(Quantity),        0) FROM dbo.DamageEntryDetails WHERE DamageId = @ActualId),
            TotalValue = (SELECT ISNULL(SUM(Quantity*UnitCost),0) FROM dbo.DamageEntryDetails WHERE DamageId = @ActualId)
        WHERE DamageId = @ActualId;
    END

    COMMIT;
    SELECT @ActualId AS DamageId;
END;
GO

SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_PostDamageEntry
    @DamageId INT,
    @UserId   INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @BranchId     INT,
            @GodownId     INT,
            @DamageDate   DATE,
            @DamageNumber NVARCHAR(30);

    SELECT @BranchId     = BranchId,
           @GodownId     = GodownId,
           @DamageDate   = DamageDate,
           @DamageNumber = DamageNumber
    FROM dbo.DamageEntry
    WHERE DamageId = @DamageId AND Status = 'Draft';

    IF @BranchId IS NULL
    BEGIN
        ROLLBACK;
        THROW 50000, 'Damage entry not found or already posted.', 1;
    END

    -- Verify BranchId matches the godown's owning branch (cross-branch safety)
    DECLARE @GodownBranchId INT;
    SELECT @GodownBranchId = BranchId FROM dbo.Godowns WHERE Id = @GodownId;
    IF @GodownBranchId IS NOT NULL SET @BranchId = @GodownBranchId;

    DECLARE @ItemId   INT,
            @Qty      DECIMAL(18,3),
            @UnitCost DECIMAL(18,4);

    DECLARE dmg_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT ItemId, Quantity, UnitCost
        FROM dbo.DamageEntryDetails
        WHERE DamageId = @DamageId;

    OPEN dmg_cur;
    FETCH NEXT FROM dmg_cur INTO @ItemId, @Qty, @UnitCost;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        DECLARE @CurBal DECIMAL(18,3) = 0,
                @CurAvg DECIMAL(18,4) = 0;

        SELECT @CurBal = BalanceQty, @CurAvg = AverageCost
        FROM dbo.CurrentStock
        WHERE BranchId = @BranchId AND GodownId = @GodownId AND ItemId = @ItemId;

        -- Use average cost if no unit cost provided
        SET @UnitCost = ISNULL(NULLIF(@UnitCost, 0), @CurAvg);

        DECLARE @NewBal DECIMAL(18,3) = @CurBal - @Qty;

        UPDATE dbo.CurrentStock
        SET BalanceQty  = @NewBal,
            LastUpdated = SYSUTCDATETIME()
        WHERE BranchId = @BranchId AND GodownId = @GodownId AND ItemId = @ItemId;

        INSERT INTO dbo.StockLedger
            (BranchId, GodownId, ItemId, TransactionDate, TransactionType,
             ReferenceType, ReferenceId, ReferenceNumber,
             InQuantity, OutQuantity, UnitCost,
             BalanceQty, BalanceValue, AverageCost, CreatedAt, CreatedBy)
        VALUES
            (@BranchId, @GodownId, @ItemId, @DamageDate, 'DAMAGE',
             'DAMAGE', @DamageId, @DamageNumber,
             0, @Qty, @UnitCost,
             @NewBal, @NewBal * @CurAvg, @CurAvg,
             SYSUTCDATETIME(), @UserId);

        FETCH NEXT FROM dmg_cur INTO @ItemId, @Qty, @UnitCost;
    END

    CLOSE dmg_cur;
    DEALLOCATE dmg_cur;

    UPDATE dbo.DamageEntry
    SET Status = 'Posted', UpdatedAt = SYSUTCDATETIME()
    WHERE DamageId = @DamageId;

    COMMIT;
END;
GO

SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetDamageRegister
    @BranchId INT,
    @FromDate DATE,
    @ToDate   DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        de.DamageId,
        de.DamageNumber,
        de.DamageDate,
        de.DamageType,
        g.GodownName,
        de.TotalQty,
        de.TotalValue,
        ISNULL(de.Remarks, '') AS Remarks,
        de.Status
    FROM dbo.DamageEntry de
    INNER JOIN dbo.Godowns g ON g.Id = de.GodownId
    WHERE de.BranchId    = @BranchId
      AND de.DamageDate >= @FromDate
      AND de.DamageDate <= @ToDate
      AND de.Status      = 'Posted'
    ORDER BY de.DamageDate DESC, de.DamageNumber;
END;
GO

-- Verify
SELECT o.name, m.uses_quoted_identifier
FROM sys.sql_modules m
JOIN sys.objects o ON o.object_id = m.object_id
WHERE o.name LIKE '%Damage%'
ORDER BY o.name;
GO
