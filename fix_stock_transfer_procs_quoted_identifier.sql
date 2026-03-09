-- Fix: recreate all Stock Transfer SPs with QUOTED_IDENTIFIER ON
-- Root cause: SPs were created with QUOTED_IDENTIFIER OFF which breaks
-- DELETE/UPDATE on tables that have filtered indexes or computed column indexes.
USE [dev_Restaurant];
GO

SET QUOTED_IDENTIFIER ON;
GO

-- ============================================================
-- 1. usp_SaveStockTransfer
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.usp_SaveStockTransfer
    @TransferId    INT,
    @BranchId      INT,
    @FromGodownId  INT,
    @ToGodownId    INT,
    @TransferDate  DATE,
    @TransferType  NVARCHAR(20)  = 'Internal',
    @PriceMode     NVARCHAR(20)  = 'AverageCost',
    @Remarks       NVARCHAR(500) = NULL,
    @UserId        INT           = NULL,
    @DetailsJson   NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @ActualId INT = @TransferId;

    IF @TransferId > 0
    BEGIN
        UPDATE dbo.StockTransfer SET
            FromGodownId = @FromGodownId,
            ToGodownId   = @ToGodownId,
            TransferDate = @TransferDate,
            TransferType = @TransferType,
            PriceMode    = @PriceMode,
            Remarks      = @Remarks,
            UpdatedAt    = SYSUTCDATETIME()
        WHERE TransferId = @TransferId AND Status = 'Draft';

        DELETE FROM dbo.StockTransferDetails WHERE TransferId = @TransferId;
    END
    ELSE
    BEGIN
        DECLARE @TransferNumber NVARCHAR(30) =
            'TRF-' + RIGHT('000' + CAST(@BranchId AS NVARCHAR), 3) + '-' +
            FORMAT(GETDATE(), 'yyyyMMdd') + '-' +
            RIGHT('0000' + CAST(ISNULL((
                SELECT COUNT(*)+1 FROM dbo.StockTransfer WHERE BranchId = @BranchId
            ), 1) AS NVARCHAR), 4);

        INSERT INTO dbo.StockTransfer
            (TransferNumber, BranchId, FromGodownId, ToGodownId, TransferDate,
             TransferType, PriceMode, Status, Remarks, TotalQty, TotalValue,
             CreatedAt, CreatedBy)
        VALUES
            (@TransferNumber, @BranchId, @FromGodownId, @ToGodownId, @TransferDate,
             @TransferType, @PriceMode, 'Draft', @Remarks, 0, 0,
             SYSUTCDATETIME(), @UserId);

        SET @ActualId = SCOPE_IDENTITY();
    END

    IF @DetailsJson IS NOT NULL AND LEN(@DetailsJson) > 2
    BEGIN
        INSERT INTO dbo.StockTransferDetails
            (TransferId, ItemId, UOMId, Quantity, UnitCost, Remarks)
        SELECT
            @ActualId,
            CAST(j.itemId   AS INT),
            CAST(j.uomId    AS INT),
            CAST(j.quantity AS DECIMAL(18,3)),
            CAST(j.unitCost AS DECIMAL(18,4)),
            j.remarks
        FROM OPENJSON(@DetailsJson)
        WITH (
            itemId   INT            '$.itemId',
            uomId    INT            '$.uomId',
            quantity DECIMAL(18,3)  '$.quantity',
            unitCost DECIMAL(18,4)  '$.unitCost',
            remarks  NVARCHAR(200)  '$.remarks'
        ) j
        WHERE j.quantity > 0;

        UPDATE dbo.StockTransfer SET
            TotalQty   = (SELECT ISNULL(SUM(Quantity),   0) FROM dbo.StockTransferDetails WHERE TransferId = @ActualId),
            TotalValue = (SELECT ISNULL(SUM(Quantity * UnitCost), 0) FROM dbo.StockTransferDetails WHERE TransferId = @ActualId)
        WHERE TransferId = @ActualId;
    END

    COMMIT;
    SELECT @ActualId AS TransferId;
END;
GO

-- ============================================================
-- 2. usp_PostStockTransfer
-- ============================================================
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.usp_PostStockTransfer
    @TransferId INT,
    @UserId     INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @BranchId      INT,
            @FromGodownId  INT,
            @ToGodownId    INT,
            @TxDate        DATE,
            @PriceMode     NVARCHAR(20),
            @TransferNumber NVARCHAR(30);

    SELECT
        @BranchId       = BranchId,
        @FromGodownId   = FromGodownId,
        @ToGodownId     = ToGodownId,
        @TxDate         = TransferDate,
        @PriceMode      = PriceMode,
        @TransferNumber = TransferNumber
    FROM dbo.StockTransfer
    WHERE TransferId = @TransferId AND Status = 'Draft';

    IF @BranchId IS NULL
    BEGIN
        ROLLBACK;
        RAISERROR('Transfer not found or already posted.', 16, 1);
        RETURN;
    END

    DECLARE @ItemId   INT,
            @Qty      DECIMAL(18,3),
            @UnitCost DECIMAL(18,4);

    DECLARE tr_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT ItemId, Quantity, UnitCost
        FROM dbo.StockTransferDetails
        WHERE TransferId = @TransferId;

    OPEN tr_cur;
    FETCH NEXT FROM tr_cur INTO @ItemId, @Qty, @UnitCost;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Auto avg cost
        IF @PriceMode = 'AverageCost'
            SELECT @UnitCost = ISNULL(AverageCost, @UnitCost)
            FROM dbo.CurrentStock
            WHERE BranchId = @BranchId AND GodownId = @FromGodownId AND ItemId = @ItemId;

        -- Source godown: deduct
        DECLARE @FromBal DECIMAL(18,3) = 0, @FromAvg DECIMAL(18,4) = 0;
        SELECT @FromBal = BalanceQty, @FromAvg = AverageCost
        FROM dbo.CurrentStock
        WHERE BranchId = @BranchId AND GodownId = @FromGodownId AND ItemId = @ItemId;

        DECLARE @NewFromBal DECIMAL(18,3) = @FromBal - @Qty;

        IF EXISTS (SELECT 1 FROM dbo.CurrentStock WHERE BranchId = @BranchId AND GodownId = @FromGodownId AND ItemId = @ItemId)
            UPDATE dbo.CurrentStock
            SET BalanceQty = @NewFromBal, LastUpdated = SYSUTCDATETIME()
            WHERE BranchId = @BranchId AND GodownId = @FromGodownId AND ItemId = @ItemId;

        INSERT INTO dbo.StockLedger
            (BranchId, GodownId, ItemId, TransactionDate, TransactionType,
             ReferenceType, ReferenceId, ReferenceNumber,
             InQuantity, OutQuantity, UnitCost,
             BalanceQty, BalanceValue, AverageCost, CreatedAt, CreatedBy)
        VALUES
            (@BranchId, @FromGodownId, @ItemId, @TxDate, 'TRANSFER_OUT',
             'TRANSFER', @TransferId, @TransferNumber,
             0, @Qty, @UnitCost,
             @NewFromBal, @NewFromBal * @FromAvg, @FromAvg, SYSUTCDATETIME(), @UserId);

        -- Destination godown: add
        DECLARE @ToBal  DECIMAL(18,3) = 0, @ToAvg DECIMAL(18,4) = 0;
        SELECT @ToBal = BalanceQty, @ToAvg = AverageCost
        FROM dbo.CurrentStock
        WHERE BranchId = @BranchId AND GodownId = @ToGodownId AND ItemId = @ItemId;

        DECLARE @NewToBal DECIMAL(18,3) = @ToBal + @Qty;
        DECLARE @NewToAvg DECIMAL(18,4) =
            CASE WHEN @NewToBal > 0
                 THEN (@ToBal * @ToAvg + @Qty * @UnitCost) / @NewToBal
                 ELSE @UnitCost END;

        IF EXISTS (SELECT 1 FROM dbo.CurrentStock WHERE BranchId = @BranchId AND GodownId = @ToGodownId AND ItemId = @ItemId)
            UPDATE dbo.CurrentStock
            SET BalanceQty   = @NewToBal,
                AverageCost  = @NewToAvg,
                LastUpdated  = SYSUTCDATETIME()
            WHERE BranchId = @BranchId AND GodownId = @ToGodownId AND ItemId = @ItemId;
        ELSE
            INSERT INTO dbo.CurrentStock
                (BranchId, GodownId, ItemId, BalanceQty, AverageCost, LastUpdated)
            VALUES
                (@BranchId, @ToGodownId, @ItemId, @NewToBal, @NewToAvg, SYSUTCDATETIME());

        INSERT INTO dbo.StockLedger
            (BranchId, GodownId, ItemId, TransactionDate, TransactionType,
             ReferenceType, ReferenceId, ReferenceNumber,
             InQuantity, OutQuantity, UnitCost,
             BalanceQty, BalanceValue, AverageCost, CreatedAt, CreatedBy)
        VALUES
            (@BranchId, @ToGodownId, @ItemId, @TxDate, 'TRANSFER_IN',
             'TRANSFER', @TransferId, @TransferNumber,
             @Qty, 0, @UnitCost,
             @NewToBal, @NewToBal * @NewToAvg, @NewToAvg, SYSUTCDATETIME(), @UserId);

        FETCH NEXT FROM tr_cur INTO @ItemId, @Qty, @UnitCost;
    END

    CLOSE tr_cur;
    DEALLOCATE tr_cur;

    UPDATE dbo.StockTransfer
    SET Status = 'Posted', UpdatedAt = SYSUTCDATETIME()
    WHERE TransferId = @TransferId;

    COMMIT;
END;
GO

-- ============================================================
-- 3. usp_GetStockTransferList
-- ============================================================
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.usp_GetStockTransferList
    @BranchId INT,
    @Status   NVARCHAR(20) = NULL,
    @FromDate DATE         = NULL,
    @ToDate   DATE         = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        st.TransferId,
        st.TransferNumber,
        st.BranchId,
        st.FromGodownId,
        st.ToGodownId,
        st.TransferDate,
        st.TransferType,
        st.PriceMode,
        st.Remarks,
        st.Status,
        st.TotalQty,
        st.TotalValue,
        fg.GodownName AS FromGodownName,
        tg.GodownName AS ToGodownName,
        (SELECT COUNT(*) FROM dbo.StockTransferDetails WHERE TransferId = st.TransferId) AS LineCount,
        st.CreatedAt
    FROM       dbo.StockTransfer st
    INNER JOIN dbo.Godowns fg ON fg.Id = st.FromGodownId
    INNER JOIN dbo.Godowns tg ON tg.Id = st.ToGodownId
    WHERE st.BranchId = @BranchId
      AND (@Status   IS NULL OR st.Status        = @Status)
      AND (@FromDate IS NULL OR st.TransferDate >= @FromDate)
      AND (@ToDate   IS NULL OR st.TransferDate <= @ToDate)
    ORDER BY st.TransferDate DESC, st.TransferNumber DESC;
END;
GO

-- ============================================================
-- 4. usp_GetStockTransferById
-- ============================================================
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.usp_GetStockTransferById
    @TransferId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Result set 1: header
    SELECT
        st.TransferId,
        st.TransferNumber,
        st.BranchId,
        st.FromGodownId,
        st.ToGodownId,
        st.TransferDate,
        st.TransferType,
        st.PriceMode,
        st.Remarks,
        st.Status,
        st.TotalQty,
        st.TotalValue,
        fg.GodownName AS FromGodownName,
        tg.GodownName AS ToGodownName,
        (SELECT COUNT(*) FROM dbo.StockTransferDetails WHERE TransferId = st.TransferId) AS LineCount,
        st.CreatedAt
    FROM       dbo.StockTransfer st
    INNER JOIN dbo.Godowns fg ON fg.Id = st.FromGodownId
    INNER JOIN dbo.Godowns tg ON tg.Id = st.ToGodownId
    WHERE st.TransferId = @TransferId;

    -- Result set 2: line items
    SELECT
        td.TransferDetailId,
        td.TransferId,
        td.ItemId,
        td.UOMId,
        td.Quantity,
        td.UnitCost,
        td.Remarks,
        i.IngredientsName AS ItemName,
        u.UOMCode,
        u.UOMName
    FROM       dbo.StockTransferDetails td
    INNER JOIN dbo.Ingredients i ON i.Id     = td.ItemId
    INNER JOIN dbo.UomMaster   u ON u.UOMId  = td.UOMId
    WHERE td.TransferId = @TransferId
    ORDER BY td.TransferDetailId;
END;
GO

-- Verify
SELECT name, uses_quoted_identifier
FROM sys.sql_modules sm
JOIN sys.objects o ON o.object_id = sm.object_id
WHERE o.name IN ('usp_SaveStockTransfer','usp_PostStockTransfer',
                 'usp_GetStockTransferList','usp_GetStockTransferById');
GO
