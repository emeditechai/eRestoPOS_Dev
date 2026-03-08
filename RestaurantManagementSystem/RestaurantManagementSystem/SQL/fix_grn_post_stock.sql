-- =============================================
-- Fix : fix_grn_post_stock.sql
-- Purpose : 1. Recreate usp_PostGRN with SET QUOTED_IDENTIFIER ON
--              Uses ISNULL(AcceptedQty, ReceivedQty-RejectedQty) so
--              both old and new GRN rows are handled.
--           2. Backfill AcceptedQty for existing GRNDetails rows
--           3. Update usp_SaveGRN to persist AcceptedQty on insert
-- Database : dev_Restaurant
-- Safe     : Idempotent
-- =============================================

USE [dev_Restaurant];
GO

SET QUOTED_IDENTIFIER ON;
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 1 : Backfill AcceptedQty for existing GRNDetails rows
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 1: Backfill AcceptedQty for existing rows ===';

UPDATE dbo.GRNDetails
SET AcceptedQty = ReceivedQty - RejectedQty
WHERE AcceptedQty IS NULL;

PRINT '  Backfill complete.';
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 2 : Recreate usp_SaveGRN — now saves AcceptedQty
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 2: Recreate usp_SaveGRN (add AcceptedQty) ===';

IF OBJECT_ID(N'dbo.usp_SaveGRN', N'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_SaveGRN;
GO

SET QUOTED_IDENTIFIER ON;
GO

CREATE PROCEDURE dbo.usp_SaveGRN
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
            GodownId       = @GodownId,
            SupplierId     = @SupplierId,
            GRNDate        = @GRNDate,
            InvoiceNo      = @InvoiceNo,
            InvoiceDate    = @InvoiceDate,
            GSTType        = @GSTType,
            Remarks        = @Remarks,
            SubTotal       = @SubTotal,
            TotalGSTAmount = @TotalGSTAmount,
            TotalAmount    = @TotalAmount,
            UpdatedAt      = SYSUTCDATETIME()
        WHERE GRNId = @GRNId AND Status = 'Draft';

        DELETE FROM dbo.GRNDetails WHERE GRNId = @GRNId;
    END
    ELSE
    BEGIN
        DECLARE @FYStart   INT         = CASE WHEN MONTH(@GRNDate) >= 4 THEN YEAR(@GRNDate) ELSE YEAR(@GRNDate) - 1 END;
        DECLARE @FYCode    NVARCHAR(4) = RIGHT(CAST(@FYStart AS NVARCHAR(4)), 2) +
                                         RIGHT(CAST(@FYStart + 1 AS NVARCHAR(4)), 2);
        DECLARE @GRNNumber NVARCHAR(30) =
            'GRN-' + @FYCode +
            RIGHT('000000' + CAST(
                ISNULL((SELECT COUNT(*) + 1 FROM dbo.GRNMaster WHERE BranchId = @BranchId), 1)
            AS NVARCHAR(6)), 6);

        INSERT INTO dbo.GRNMaster
            (GRNNumber, BranchId, POId, GodownId, SupplierId, GRNDate,
             InvoiceNo, InvoiceDate, GSTType, SubTotal, TotalGSTAmount,
             TotalAmount, Status, Remarks, CreatedAt, CreatedBy)
        VALUES
            (@GRNNumber, @BranchId, NULLIF(@POId, 0), @GodownId, @SupplierId, @GRNDate,
             @InvoiceNo, @InvoiceDate, @GSTType, @SubTotal, @TotalGSTAmount,
             @TotalAmount, 'Draft', @Remarks, SYSUTCDATETIME(), @UserId);

        SET @ActualGRNId = SCOPE_IDENTITY();
    END

    IF @DetailsJson IS NOT NULL AND LEN(@DetailsJson) > 2
    BEGIN
        INSERT INTO dbo.GRNDetails
            (GRNId, PODetailId, ItemId, UOMId, OrderedQty,
             ReceivedQty, RejectedQty, AcceptedQty, UnitRate, GSTPercent, Remarks)
        SELECT
            @ActualGRNId,
            NULLIF(CAST(j.poDetailId AS INT), 0),
            CAST(j.itemId      AS INT),
            CAST(j.uomId       AS INT),
            CAST(j.orderedQty  AS DECIMAL(18,3)),
            CAST(j.receivedQty AS DECIMAL(18,3)),
            CAST(j.rejectedQty AS DECIMAL(18,3)),
            -- AcceptedQty = Received - Rejected
            CAST(j.receivedQty AS DECIMAL(18,3)) - CAST(j.rejectedQty AS DECIMAL(18,3)),
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

    UPDATE dbo.GRNMaster SET
        SubTotal       = ISNULL((SELECT SUM(ReceivedQty * UnitRate)
                                 FROM dbo.GRNDetails WHERE GRNId = @ActualGRNId), 0),
        TotalGSTAmount = ISNULL((SELECT SUM(ReceivedQty * UnitRate * GSTPercent / 100)
                                 FROM dbo.GRNDetails WHERE GRNId = @ActualGRNId), 0),
        TotalAmount    = ISNULL((SELECT SUM(ReceivedQty * UnitRate * (1 + GSTPercent / 100))
                                 FROM dbo.GRNDetails WHERE GRNId = @ActualGRNId), 0)
    WHERE GRNId = @ActualGRNId;

    COMMIT;
    SELECT @ActualGRNId AS GRNId;
END
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 3 : Recreate usp_PostGRN with SET QUOTED_IDENTIFIER ON
--          Uses ISNULL(AcceptedQty, ReceivedQty-RejectedQty) to handle
--          both old rows (AcceptedQty NULL) and new rows
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 3: Recreate usp_PostGRN with QUOTED_IDENTIFIER ON ===';

IF OBJECT_ID(N'dbo.usp_PostGRN', N'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_PostGRN;
GO

SET QUOTED_IDENTIFIER ON;
GO

CREATE PROCEDURE dbo.usp_PostGRN
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

    -- ── Process each GRN line → update CurrentStock + StockLedger ────────────
    DECLARE @ItemId   INT;
    DECLARE @AccQty   DECIMAL(18,3);
    DECLARE @UnitCost DECIMAL(18,4);

    DECLARE grn_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT
            ItemId,
            ISNULL(AcceptedQty, ReceivedQty - RejectedQty) AS AccQty,
            UnitRate
        FROM dbo.GRNDetails
        WHERE GRNId = @GRNId
          AND ISNULL(AcceptedQty, ReceivedQty - RejectedQty) > 0;

    OPEN grn_cur;
    FETCH NEXT FROM grn_cur INTO @ItemId, @AccQty, @UnitCost;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        DECLARE @PrevBal DECIMAL(18,3) = 0;
        DECLARE @PrevAvg DECIMAL(18,4) = 0;

        SELECT @PrevBal = ISNULL(BalanceQty, 0), @PrevAvg = ISNULL(AverageCost, 0)
        FROM dbo.CurrentStock
        WHERE BranchId = @BranchId AND GodownId = @GodownId AND ItemId = @ItemId;

        DECLARE @NewBal DECIMAL(18,3) = @PrevBal + @AccQty;
        DECLARE @NewAvg DECIMAL(18,4) =
            CASE WHEN @NewBal > 0
                 THEN (@PrevBal * @PrevAvg + @AccQty * @UnitCost) / @NewBal
                 ELSE @UnitCost
            END;

        IF EXISTS (SELECT 1 FROM dbo.CurrentStock
                   WHERE BranchId = @BranchId AND GodownId = @GodownId AND ItemId = @ItemId)
            UPDATE dbo.CurrentStock SET
                BalanceQty  = @NewBal,
                AverageCost = @NewAvg,
                LastUpdated = SYSUTCDATETIME()
            WHERE BranchId = @BranchId AND GodownId = @GodownId AND ItemId = @ItemId;
        ELSE
            INSERT INTO dbo.CurrentStock
                (BranchId, GodownId, ItemId, BalanceQty, AverageCost, LastUpdated)
            VALUES
                (@BranchId, @GodownId, @ItemId, @NewBal, @NewAvg, SYSUTCDATETIME());

        INSERT INTO dbo.StockLedger
            (BranchId, GodownId, ItemId, TransactionDate, TransactionType,
             ReferenceType, ReferenceId, ReferenceNumber,
             InQuantity, OutQuantity, UnitCost,
             BalanceQty, BalanceValue, AverageCost, CreatedAt, CreatedBy)
        VALUES
            (@BranchId, @GodownId, @ItemId, @GRNDate, 'GRN',
             'GRN', @GRNId, @GRNNumber,
             @AccQty, 0, @UnitCost,
             @NewBal, @NewBal * @NewAvg, @NewAvg, SYSUTCDATETIME(), @UserId);

        FETCH NEXT FROM grn_cur INTO @ItemId, @AccQty, @UnitCost;
    END

    CLOSE grn_cur;
    DEALLOCATE grn_cur;

    -- ── Update PO received quantities ─────────────────────────────────────────
    UPDATE pod SET
        pod.ReceivedQty = pod.ReceivedQty +
            ISNULL(gd.AcceptedQty, gd.ReceivedQty - gd.RejectedQty)
    FROM dbo.PurchaseOrderDetails pod
    INNER JOIN dbo.GRNDetails gd ON gd.PODetailId = pod.PODetailId
    WHERE gd.GRNId = @GRNId AND gd.PODetailId IS NOT NULL;

    -- ── Update PO status based on fulfilment ──────────────────────────────────
    DECLARE @POId INT;
    SELECT @POId = POId FROM dbo.GRNMaster WHERE GRNId = @GRNId;

    IF @POId IS NOT NULL
    BEGIN
        DECLARE @TotalOrdered  DECIMAL(18,3);
        DECLARE @TotalReceived DECIMAL(18,3);

        SELECT @TotalOrdered  = SUM(OrderedQty),
               @TotalReceived = SUM(ReceivedQty)
        FROM dbo.PurchaseOrderDetails
        WHERE POId = @POId;

        UPDATE dbo.PurchaseOrder SET
            Status = CASE
                WHEN @TotalReceived >= @TotalOrdered THEN 'Completed'
                WHEN @TotalReceived > 0              THEN 'PartialGRN'
                ELSE Status
            END
        WHERE POId = @POId AND Status IN ('Approved', 'PartialGRN');
    END

    -- ── Mark GRN as Posted ────────────────────────────────────────────────────
    UPDATE dbo.GRNMaster SET
        Status    = 'Posted',
        UpdatedAt = SYSUTCDATETIME()
    WHERE GRNId = @GRNId;

    COMMIT;
END
GO

PRINT '=== All steps complete. usp_PostGRN and usp_SaveGRN updated. ===';
GO
