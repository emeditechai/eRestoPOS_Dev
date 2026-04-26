-- ══════════════════════════════════════════════════════════════════════
-- PRODUCTION FIX: Purchase Register showing 0 amounts
-- Root cause: GRNMaster header totals were never persisted because
--   usp_SaveGRN tried to INSERT into computed columns (AcceptedQty,
--   GSTAmount, LineAmount) and usp_PostGRN never recalculated totals.
--
-- Run against your production database.
-- ══════════════════════════════════════════════════════════════════════

SET QUOTED_IDENTIFIER ON;
GO

-- ──────────────────────────────────────────────────────────────────────
-- STEP 1: One-time data patch
--   Recalculate SubTotal / TotalGSTAmount / TotalAmount for any
--   existing GRNMaster rows where the header shows 0 but detail
--   lines have actual data.
-- ──────────────────────────────────────────────────────────────────────
UPDATE gm SET
    SubTotal       = d.taxable,
    TotalGSTAmount = d.gstAmt,
    TotalAmount    = d.taxable + d.gstAmt
FROM dbo.GRNMaster gm
CROSS APPLY (
    SELECT
        ISNULL(SUM(LineAmount), 0) AS taxable,
        ISNULL(SUM(GSTAmount),  0) AS gstAmt
    FROM dbo.GRNDetails
    WHERE GRNId = gm.GRNId
) d
WHERE gm.SubTotal = 0
  AND d.taxable > 0;

PRINT CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' GRNMaster row(s) patched.';
GO

-- ──────────────────────────────────────────────────────────────────────
-- STEP 2: Fix usp_SaveGRN
--   - Do NOT insert into computed columns (AcceptedQty, GSTAmount,
--     LineAmount) — SQL Server calculates them automatically.
--   - After inserting detail rows, recalculate header totals from
--     the now-populated computed columns.
-- ──────────────────────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.usp_SaveGRN
    @GRNId          INT,
    @BranchId       INT,
    @POId           INT,
    @GodownId       INT,
    @SupplierId     INT,
    @GRNDate        DATE,
    @InvoiceNo      NVARCHAR(50)  = NULL,
    @InvoiceDate    DATE          = NULL,
    @GSTType        NVARCHAR(20)  = 'Exclusive',
    @Remarks        NVARCHAR(500) = NULL,
    @SubTotal       DECIMAL(18,2) = 0,
    @TotalGSTAmount DECIMAL(18,2) = 0,
    @TotalAmount    DECIMAL(18,2) = 0,
    @UserId         INT           = NULL,
    @DetailsJson    NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @ActualGRNId INT = @GRNId;

    IF @GRNId > 0
    BEGIN
        UPDATE dbo.GRNMaster SET
            GodownId   = @GodownId,
            SupplierId = @SupplierId,
            GRNDate    = @GRNDate,
            InvoiceNo  = @InvoiceNo,
            InvoiceDate= @InvoiceDate,
            GSTType    = @GSTType,
            Remarks    = @Remarks,
            UpdatedAt  = SYSUTCDATETIME()
        WHERE GRNId = @GRNId AND Status = 'Draft';

        DELETE FROM dbo.GRNDetails WHERE GRNId = @GRNId;
    END
    ELSE
    BEGIN
        DECLARE @FYStart   INT         = CASE WHEN MONTH(@GRNDate) >= 4 THEN YEAR(@GRNDate) ELSE YEAR(@GRNDate) - 1 END;
        DECLARE @FYCode    NVARCHAR(4) = RIGHT(CAST(@FYStart AS NVARCHAR(4)), 2) +
                                         RIGHT(CAST(@FYStart + 1 AS NVARCHAR(4)), 2);
        DECLARE @BranchPad NVARCHAR(3) = RIGHT('000' + CAST(@BranchId AS NVARCHAR(3)), 3);
        DECLARE @SeqNo     INT         = ISNULL((SELECT COUNT(*) + 1 FROM dbo.GRNMaster WHERE BranchId = @BranchId), 1);
        DECLARE @SeqPad    NVARCHAR(6) = RIGHT('000000' + CAST(@SeqNo AS NVARCHAR(6)), 6);
        DECLARE @GRNNumber NVARCHAR(30) = 'GRN-' + @BranchPad + '-' + @FYCode + @SeqPad;

        INSERT INTO dbo.GRNMaster
            (GRNNumber, BranchId, POId, GodownId, SupplierId, GRNDate,
             InvoiceNo, InvoiceDate, GSTType, SubTotal, TotalGSTAmount,
             TotalAmount, Status, Remarks, CreatedAt, CreatedBy)
        VALUES
            (@GRNNumber, @BranchId, NULLIF(@POId,0), @GodownId, @SupplierId, @GRNDate,
             @InvoiceNo, @InvoiceDate, @GSTType, 0, 0,
             0, 'Draft', @Remarks, SYSUTCDATETIME(), @UserId);

        SET @ActualGRNId = SCOPE_IDENTITY();
    END

    -- Insert base columns only (AcceptedQty, GSTAmount, LineAmount are PERSISTED computed columns)
    IF @DetailsJson IS NOT NULL AND LEN(@DetailsJson) > 2
    BEGIN
        INSERT INTO dbo.GRNDetails
            (GRNId, PODetailId, ItemId, UOMId, OrderedQty,
             ReceivedQty, RejectedQty, UnitRate, GSTPercent, Remarks)
        SELECT
            @ActualGRNId,
            NULLIF(CAST(j.poDetailId AS INT), 0),
            CAST(j.itemId      AS INT),
            CAST(j.uomId       AS INT),
            CAST(j.orderedQty  AS DECIMAL(18,3)),
            CAST(j.receivedQty AS DECIMAL(18,3)),
            CAST(j.rejectedQty AS DECIMAL(18,3)),
            CAST(j.unitRate    AS DECIMAL(18,4)),
            CAST(j.gstPercent  AS DECIMAL(5,2)),
            j.remarks
        FROM OPENJSON(@DetailsJson)
        WITH (
            poDetailId  INT            '$.poDetailId',
            itemId      INT            '$.itemId',
            uomId       INT            '$.uomId',
            orderedQty  DECIMAL(18,3)  '$.orderedQty',
            receivedQty DECIMAL(18,3)  '$.receivedQty',
            rejectedQty DECIMAL(18,3)  '$.rejectedQty',
            unitRate    DECIMAL(18,4)  '$.unitRate',
            gstPercent  DECIMAL(5,2)   '$.gstPercent',
            remarks     NVARCHAR(200)  '$.remarks'
        ) j
        WHERE j.receivedQty > 0;
    END

    -- Recalculate header totals from computed columns
    UPDATE dbo.GRNMaster SET
        SubTotal       = ISNULL((SELECT SUM(LineAmount)                FROM dbo.GRNDetails WHERE GRNId = @ActualGRNId), 0),
        TotalGSTAmount = ISNULL((SELECT SUM(GSTAmount)                 FROM dbo.GRNDetails WHERE GRNId = @ActualGRNId), 0),
        TotalAmount    = ISNULL((SELECT SUM(LineAmount)+SUM(GSTAmount) FROM dbo.GRNDetails WHERE GRNId = @ActualGRNId), 0)
    WHERE GRNId = @ActualGRNId;

    COMMIT;
    SELECT @ActualGRNId AS GRNId;
END
GO

-- ──────────────────────────────────────────────────────────────────────
-- STEP 3: Fix usp_PostGRN
--   Always recalculate and persist header totals from computed detail
--   columns before marking the GRN as Posted.
-- ──────────────────────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.usp_PostGRN
    @GRNId  INT,
    @UserId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @BranchId INT, @GodownId INT, @GRNDate DATE, @GRNNumber NVARCHAR(30);

    SELECT
        @BranchId  = BranchId,
        @GodownId  = GodownId,
        @GRNDate   = GRNDate,
        @GRNNumber = GRNNumber
    FROM dbo.GRNMaster
    WHERE GRNId = @GRNId AND Status = 'Draft';

    IF @BranchId IS NULL
    BEGIN
        ROLLBACK;
        RAISERROR('GRN not found or already posted.', 16, 1);
        RETURN;
    END

    DECLARE @ItemId INT, @AccQty DECIMAL(18,3), @UnitCost DECIMAL(18,4);

    DECLARE grn_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT ItemId, AcceptedQty, UnitRate
        FROM dbo.GRNDetails
        WHERE GRNId = @GRNId AND AcceptedQty > 0;

    OPEN grn_cur;
    FETCH NEXT FROM grn_cur INTO @ItemId, @AccQty, @UnitCost;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        DECLARE @PrevBal DECIMAL(18,3) = 0, @PrevAvg DECIMAL(18,4) = 0;

        SELECT @PrevBal = ISNULL(BalanceQty,0), @PrevAvg = ISNULL(AverageCost,0)
        FROM dbo.CurrentStock
        WHERE BranchId=@BranchId AND GodownId=@GodownId AND ItemId=@ItemId;

        DECLARE @NewBal DECIMAL(18,3) = @PrevBal + @AccQty;
        DECLARE @NewAvg DECIMAL(18,4) =
            CASE WHEN @NewBal > 0
                 THEN (@PrevBal*@PrevAvg + @AccQty*@UnitCost) / @NewBal
                 ELSE @UnitCost END;

        IF EXISTS (SELECT 1 FROM dbo.CurrentStock WHERE BranchId=@BranchId AND GodownId=@GodownId AND ItemId=@ItemId)
            UPDATE dbo.CurrentStock SET BalanceQty=@NewBal, AverageCost=@NewAvg, LastUpdated=SYSUTCDATETIME()
            WHERE BranchId=@BranchId AND GodownId=@GodownId AND ItemId=@ItemId;
        ELSE
            INSERT INTO dbo.CurrentStock (BranchId,GodownId,ItemId,BalanceQty,AverageCost,LastUpdated)
            VALUES (@BranchId,@GodownId,@ItemId,@NewBal,@NewAvg,SYSUTCDATETIME());

        INSERT INTO dbo.StockLedger
            (BranchId,GodownId,ItemId,TransactionDate,TransactionType,ReferenceType,
             ReferenceId,ReferenceNumber,InQuantity,OutQuantity,UnitCost,
             BalanceQty,BalanceValue,AverageCost,CreatedAt,CreatedBy)
        VALUES
            (@BranchId,@GodownId,@ItemId,@GRNDate,'GRN','GRN',
             @GRNId,@GRNNumber,@AccQty,0,@UnitCost,
             @NewBal,@NewBal*@NewAvg,@NewAvg,SYSUTCDATETIME(),@UserId);

        FETCH NEXT FROM grn_cur INTO @ItemId, @AccQty, @UnitCost;
    END
    CLOSE grn_cur; DEALLOCATE grn_cur;

    -- Update PO received quantities
    UPDATE pod SET pod.ReceivedQty = pod.ReceivedQty + gd.AcceptedQty
    FROM dbo.PurchaseOrderDetails pod
    INNER JOIN dbo.GRNDetails gd ON gd.PODetailId = pod.PODetailId
    WHERE gd.GRNId = @GRNId AND gd.PODetailId IS NOT NULL;

    -- Update PO status
    DECLARE @POId INT;
    SELECT @POId = POId FROM dbo.GRNMaster WHERE GRNId = @GRNId;
    IF @POId IS NOT NULL
    BEGIN
        DECLARE @TotalOrdered DECIMAL(18,3), @TotalReceived DECIMAL(18,3);
        SELECT @TotalOrdered=SUM(OrderedQty), @TotalReceived=SUM(ReceivedQty)
        FROM dbo.PurchaseOrderDetails WHERE POId=@POId;

        UPDATE dbo.PurchaseOrder SET
            Status = CASE
                WHEN @TotalReceived >= @TotalOrdered THEN 'Completed'
                WHEN @TotalReceived > 0              THEN 'PartialGRN'
                ELSE Status END
        WHERE POId=@POId AND Status IN ('Approved','PartialGRN');
    END

    -- Recalculate & persist header totals from computed detail columns
    UPDATE dbo.GRNMaster SET
        SubTotal       = ISNULL((SELECT SUM(LineAmount)                FROM dbo.GRNDetails WHERE GRNId=@GRNId), 0),
        TotalGSTAmount = ISNULL((SELECT SUM(GSTAmount)                 FROM dbo.GRNDetails WHERE GRNId=@GRNId), 0),
        TotalAmount    = ISNULL((SELECT SUM(LineAmount)+SUM(GSTAmount) FROM dbo.GRNDetails WHERE GRNId=@GRNId), 0),
        Status    = 'Posted',
        UpdatedAt = SYSUTCDATETIME()
    WHERE GRNId = @GRNId;

    COMMIT;
END
GO

PRINT 'Production fix complete: data patched + usp_SaveGRN + usp_PostGRN updated.';
GO
