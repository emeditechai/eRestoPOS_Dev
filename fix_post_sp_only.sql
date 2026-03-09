USE [dev_Restaurant];
GO
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

    DECLARE @BranchId       INT,
            @FromGodownId   INT,
            @ToGodownId     INT,
            @TxDate         DATE,
            @PriceMode      NVARCHAR(20),
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
        THROW 50000, 'Transfer not found or already posted.', 1;
    END

    -- KEY FIX: derive BranchId from each godown, not from the transfer header
    DECLARE @FromBranchId INT, @ToBranchId INT;
    SELECT @FromBranchId = BranchId FROM dbo.Godowns WHERE Id = @FromGodownId;
    SELECT @ToBranchId   = BranchId FROM dbo.Godowns WHERE Id = @ToGodownId;

    IF @FromBranchId IS NULL OR @ToBranchId IS NULL
    BEGIN
        ROLLBACK;
        THROW 50000, 'Godown not found.', 1;
    END

    DECLARE @ItemId   INT,
            @Qty      DECIMAL(18,3),
            @UnitCost DECIMAL(18,4);

    DECLARE tr_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT ItemId, Quantity, UnitCost
        FROM   dbo.StockTransferDetails
        WHERE  TransferId = @TransferId;

    OPEN tr_cur;
    FETCH NEXT FROM tr_cur INTO @ItemId, @Qty, @UnitCost;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Optional: use average cost from source godown
        IF @PriceMode = 'AverageCost'
            SELECT @UnitCost = ISNULL(AverageCost, @UnitCost)
            FROM   dbo.CurrentStock
            WHERE  BranchId = @FromBranchId AND GodownId = @FromGodownId AND ItemId = @ItemId;

        -- ── SOURCE: deduct ────────────────────────────────────────────
        DECLARE @FromBal DECIMAL(18,3) = 0,
                @FromAvg DECIMAL(18,4) = 0;

        SELECT @FromBal = BalanceQty, @FromAvg = AverageCost
        FROM   dbo.CurrentStock
        WHERE  BranchId = @FromBranchId AND GodownId = @FromGodownId AND ItemId = @ItemId;

        IF @FromBal < @Qty
        BEGIN
            ROLLBACK;
            DECLARE @ErrMsg NVARCHAR(400) = CONCAT(
                'Insufficient stock in source godown for ItemId=', @ItemId,
                ' (available: ', CAST(@FromBal AS VARCHAR(30)), ').');
            THROW 50001, @ErrMsg, 1;
        END

        DECLARE @NewFromBal DECIMAL(18,3) = @FromBal - @Qty;

        UPDATE dbo.CurrentStock
        SET    BalanceQty  = @NewFromBal,
               LastUpdated = SYSUTCDATETIME()
        WHERE  BranchId = @FromBranchId AND GodownId = @FromGodownId AND ItemId = @ItemId;

        INSERT INTO dbo.StockLedger
            (BranchId, GodownId, ItemId, TransactionDate, TransactionType,
             ReferenceType, ReferenceId, ReferenceNumber,
             InQuantity, OutQuantity, UnitCost,
             BalanceQty, BalanceValue, AverageCost, CreatedAt, CreatedBy)
        VALUES
            (@FromBranchId, @FromGodownId, @ItemId, @TxDate, 'TRANSFER_OUT',
             'TRANSFER', @TransferId, @TransferNumber,
             0, @Qty, @UnitCost,
             @NewFromBal, @NewFromBal * @FromAvg, @FromAvg,
             SYSUTCDATETIME(), @UserId);

        -- ── DESTINATION: add ──────────────────────────────────────────
        DECLARE @ToBal  DECIMAL(18,3) = 0,
                @ToAvg  DECIMAL(18,4) = 0;

        SELECT @ToBal = ISNULL(BalanceQty, 0), @ToAvg = ISNULL(AverageCost, 0)
        FROM   dbo.CurrentStock
        WHERE  BranchId = @ToBranchId AND GodownId = @ToGodownId AND ItemId = @ItemId;

        DECLARE @NewToBal DECIMAL(18,3) = @ToBal + @Qty;
        DECLARE @NewToAvg DECIMAL(18,4) =
            CASE WHEN @NewToBal > 0
                 THEN (@ToBal * @ToAvg + @Qty * @UnitCost) / @NewToBal
                 ELSE @UnitCost END;

        IF EXISTS (
            SELECT 1 FROM dbo.CurrentStock
            WHERE  BranchId = @ToBranchId AND GodownId = @ToGodownId AND ItemId = @ItemId
        )
            UPDATE dbo.CurrentStock
            SET    BalanceQty  = @NewToBal,
                   AverageCost = @NewToAvg,
                   LastUpdated = SYSUTCDATETIME()
            WHERE  BranchId = @ToBranchId AND GodownId = @ToGodownId AND ItemId = @ItemId;
        ELSE
            INSERT INTO dbo.CurrentStock
                (BranchId, GodownId, ItemId, BalanceQty, AverageCost, LastUpdated)
            VALUES
                (@ToBranchId, @ToGodownId, @ItemId, @NewToBal, @NewToAvg, SYSUTCDATETIME());

        INSERT INTO dbo.StockLedger
            (BranchId, GodownId, ItemId, TransactionDate, TransactionType,
             ReferenceType, ReferenceId, ReferenceNumber,
             InQuantity, OutQuantity, UnitCost,
             BalanceQty, BalanceValue, AverageCost, CreatedAt, CreatedBy)
        VALUES
            (@ToBranchId, @ToGodownId, @ItemId, @TxDate, 'TRANSFER_IN',
             'TRANSFER', @TransferId, @TransferNumber,
             @Qty, 0, @UnitCost,
             @NewToBal, @NewToBal * @NewToAvg, @NewToAvg,
             SYSUTCDATETIME(), @UserId);

        FETCH NEXT FROM tr_cur INTO @ItemId, @Qty, @UnitCost;
    END

    CLOSE tr_cur;
    DEALLOCATE tr_cur;

    UPDATE dbo.StockTransfer
    SET    Status    = 'Posted',
           UpdatedAt = SYSUTCDATETIME()
    WHERE  TransferId = @TransferId;

    COMMIT;
END;
GO

PRINT 'usp_PostStockTransfer recreated successfully.';
GO
