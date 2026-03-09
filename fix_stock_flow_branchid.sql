USE [dev_Restaurant];
GO
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

-- ============================================================
-- STEP 1 : Diagnose orphaned rows (dry-run read)
-- ============================================================
PRINT '=== Orphaned CurrentStock rows (BranchId != Godown.BranchId) ===';
SELECT cs.StockId,
       cs.BranchId        AS CS_BranchId,
       g.BranchId         AS Godown_BranchId,
       g.GodownName,
       i.IngredientsName,
       cs.BalanceQty
FROM   dbo.CurrentStock cs
JOIN   dbo.Godowns     g ON g.Id    = cs.GodownId
JOIN   dbo.Ingredients i ON i.Id    = cs.ItemId
WHERE  cs.BranchId <> g.BranchId;

PRINT '=== Orphaned StockLedger rows ===';
SELECT sl.LedgerId,
       sl.BranchId        AS SL_BranchId,
       g.BranchId         AS Godown_BranchId,
       g.GodownName,
       sl.TransactionType,
       sl.ReferenceNumber,
       i.IngredientsName,
       sl.InQuantity,
       sl.OutQuantity
FROM   dbo.StockLedger sl
JOIN   dbo.Godowns     g ON g.Id = sl.GodownId
JOIN   dbo.Ingredients i ON i.Id = sl.ItemId
WHERE  sl.BranchId <> g.BranchId;
GO

-- ============================================================
-- STEP 2 : Fix existing bad data
--   Rule: the BranchId in CurrentStock and StockLedger must
--         always equal the BranchId of the Godown row.
--   For every orphaned CurrentStock (wrong BranchId):
--     a) Merge its qty/cost INTO the correct row (correct BranchId)
--     b) Delete the orphan
--   For StockLedger: simply correct the BranchId column.
-- ============================================================
PRINT '=== Fixing CurrentStock orphans ===';
BEGIN TRANSACTION;

-- Identify orphans
DECLARE @OrphanId    INT,
        @WrongBranch INT,
        @CorrectBranch INT,
        @GodownId    INT,
        @ItemId      INT,
        @OrphanQty   DECIMAL(18,3),
        @OrphanAvg   DECIMAL(18,4);

DECLARE fix_cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT cs.StockId, cs.BranchId, g.BranchId, cs.GodownId, cs.ItemId, cs.BalanceQty, cs.AverageCost
    FROM   dbo.CurrentStock cs
    JOIN   dbo.Godowns g ON g.Id = cs.GodownId
    WHERE  cs.BranchId <> g.BranchId;

OPEN fix_cur;
FETCH NEXT FROM fix_cur INTO @OrphanId, @WrongBranch, @CorrectBranch, @GodownId, @ItemId, @OrphanQty, @OrphanAvg;

WHILE @@FETCH_STATUS = 0
BEGIN
    -- Does a correct row already exist?
    IF EXISTS (
        SELECT 1 FROM dbo.CurrentStock
        WHERE  BranchId = @CorrectBranch AND GodownId = @GodownId AND ItemId = @ItemId
    )
    BEGIN
        -- Merge: weighted average the costs, add qty
        UPDATE dbo.CurrentStock
        SET    BalanceQty  = BalanceQty + @OrphanQty,
               AverageCost = CASE
                                WHEN (BalanceQty + @OrphanQty) > 0
                                THEN (BalanceQty * AverageCost + @OrphanQty * @OrphanAvg)
                                     / (BalanceQty + @OrphanQty)
                                ELSE @OrphanAvg
                             END,
               LastUpdated = SYSUTCDATETIME()
        WHERE  BranchId = @CorrectBranch AND GodownId = @GodownId AND ItemId = @ItemId;
    END
    ELSE
    BEGIN
        -- Move: just update the BranchId on the orphan
        UPDATE dbo.CurrentStock
        SET    BranchId    = @CorrectBranch,
               LastUpdated = SYSUTCDATETIME()
        WHERE  StockId = @OrphanId;

        -- Don't delete – it's now the correct row; skip delete step
        GOTO next_orphan;
    END

    -- Delete the orphan (already merged)
    DELETE FROM dbo.CurrentStock WHERE StockId = @OrphanId;

    next_orphan:
    FETCH NEXT FROM fix_cur INTO @OrphanId, @WrongBranch, @CorrectBranch, @GodownId, @ItemId, @OrphanQty, @OrphanAvg;
END

CLOSE fix_cur; DEALLOCATE fix_cur;

-- Fix StockLedger orphans: just correct BranchId
PRINT '=== Fixing StockLedger orphans ===';
UPDATE sl
SET    sl.BranchId = g.BranchId
FROM   dbo.StockLedger sl
JOIN   dbo.Godowns g ON g.Id = sl.GodownId
WHERE  sl.BranchId <> g.BranchId;

COMMIT;
PRINT '=== Data fix complete ===';
GO

-- ============================================================
-- STEP 3 : Recreate usp_PostStockTransfer with correct
--          per-godown BranchId resolution.
--
--  KEY FIX: derive @FromBranchId and @ToBranchId from the
--           Godowns table — do NOT use the transfer's @BranchId
--           for stock writes, because the destination godown
--           may belong to a different branch.
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

    -- ── Load transfer header ──────────────────────────────────────────────
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
        RAISERROR('Transfer not found or already posted.', 16, 1);
        RETURN;
    END

    -- ── Resolve the ACTUAL branch of each godown ─────────────────────────
    --    This is the critical fix: stock always belongs to the godown's branch
    DECLARE @FromBranchId INT, @ToBranchId INT;
    SELECT @FromBranchId = BranchId FROM dbo.Godowns WHERE Id = @FromGodownId;
    SELECT @ToBranchId   = BranchId FROM dbo.Godowns WHERE Id = @ToGodownId;

    IF @FromBranchId IS NULL OR @ToBranchId IS NULL
    BEGIN
        ROLLBACK;
        RAISERROR('Godown not found.', 16, 1);
        RETURN;
    END

    -- ── Process each line ────────────────────────────────────────────────
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
        -- ── Auto average cost from source godown ──────────────────────
        IF @PriceMode = 'AverageCost'
            SELECT @UnitCost = ISNULL(AverageCost, @UnitCost)
            FROM   dbo.CurrentStock
            WHERE  BranchId = @FromBranchId AND GodownId = @FromGodownId AND ItemId = @ItemId;

        -- ── SOURCE godown: deduct stock ────────────────────────────────
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
        SET    BalanceQty   = @NewFromBal,
               LastUpdated  = SYSUTCDATETIME()
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

        -- ── DESTINATION godown: add stock ──────────────────────────────
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

-- ============================================================
-- STEP 4 : Fix usp_GetCurrentStockSummary to show stock for
--          ALL godowns attached to the given branch's transfers
--          (cross-branch godowns now visible when is_main_branch).
--          Core rule: cs.BranchId = g.BranchId always (after fix).
-- ============================================================
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.usp_GetCurrentStockSummary
    @BranchId INT,
    @GodownId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IsMainBranch BIT = 0;
    SELECT @IsMainBranch = ISNULL(Is_MainBranch, 0) FROM dbo.Branches WHERE BranchId = @BranchId;

    SELECT
        cs.StockId,
        cs.BranchId,
        cs.GodownId,
        cs.ItemId,
        cs.BalanceQty,
        cs.AverageCost,
        cs.BalanceQty * cs.AverageCost          AS StockValue,
        i.IngredientsName                        AS ItemName,
        ISNULL(i.Code,         '')               AS ItemCode,
        ISNULL(i.ItemCategory, '')               AS ItemCategory,
        ISNULL(i.ReorderLevel, 0)                AS ReorderLevel,
        ISNULL(u.UOMCode,      '')               AS BaseUOMCode,
        ISNULL(u.UOMName,      '')               AS BaseUOMName,
        g.GodownName,
        b.BranchName,
        CASE WHEN g.IsMainGodown = 1 THEN 'Main' ELSE 'Sub' END AS GodownType,
        CASE WHEN i.ReorderLevel > 0 AND cs.BalanceQty <= i.ReorderLevel
             THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END        AS IsLowStock
    FROM  dbo.CurrentStock  cs
    JOIN  dbo.Godowns       g  ON g.Id      = cs.GodownId
    JOIN  dbo.Branches      b  ON b.BranchId = g.BranchId    -- use godown's branch, not cs.BranchId
    JOIN  dbo.Ingredients   i  ON i.Id      = cs.ItemId
    LEFT JOIN dbo.UomMaster u  ON u.UOMId   = i.PurchaseUOMId
    WHERE cs.BalanceQty <> 0
      AND i.IsActive    = 1
      AND g.IsActive    = 1
      AND (
            @IsMainBranch = 1                         -- Main branch: show ALL godowns
            OR g.BranchId = @BranchId                 -- Non-main: own godowns only
          )
      AND (@GodownId IS NULL OR cs.GodownId = @GodownId)
    ORDER BY b.BranchName, g.GodownName, i.IngredientsName;
END;
GO

-- ============================================================
-- STEP 5 : Fix usp_GetGodownsWithStock for same cross-branch logic
-- ============================================================
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE dbo.usp_GetGodownsWithStock
    @BranchId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IsMainBranch BIT = 0;
    SELECT @IsMainBranch = ISNULL(Is_MainBranch, 0) FROM dbo.Branches WHERE BranchId = @BranchId;

    SELECT DISTINCT
        g.Id           AS GodownId,
        g.GodownName,
        b.BranchName,
        g.BranchId     AS GodownBranchId,
        g.IsMainGodown
    FROM  dbo.CurrentStock cs
    JOIN  dbo.Godowns  g ON g.Id        = cs.GodownId
    JOIN  dbo.Branches b ON b.BranchId  = g.BranchId
    WHERE cs.BalanceQty <> 0
      AND g.IsActive    = 1
      AND (
            @IsMainBranch = 1
            OR g.BranchId = @BranchId
          )
    ORDER BY b.BranchName, g.GodownName;
END;
GO

-- ============================================================
-- STEP 6 : Verify final state
-- ============================================================
PRINT '=== Final CurrentStock (all items) ===';
SELECT cs.StockId, cs.BranchId, b.BranchName, cs.GodownId, g.GodownName,
       i.IngredientsName, cs.BalanceQty, cs.AverageCost,
       cs.BalanceQty * cs.AverageCost AS StockValue
FROM   dbo.CurrentStock cs
JOIN   dbo.Godowns     g ON g.Id       = cs.GodownId
JOIN   dbo.Branches    b ON b.BranchId = g.BranchId
JOIN   dbo.Ingredients i ON i.Id       = cs.ItemId
ORDER  BY i.IngredientsName, g.GodownName;

PRINT '=== Orphan check (should be 0 rows) ===';
SELECT COUNT(*) AS OrphanCount
FROM   dbo.CurrentStock cs
JOIN   dbo.Godowns g ON g.Id = cs.GodownId
WHERE  cs.BranchId <> g.BranchId;
GO
