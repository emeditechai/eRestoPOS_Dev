-- =============================================
-- Migration : create_po_number_sequence.sql
-- Purpose   : Implement new PO Number pattern: PO-<FYCode><6-digit seq>
--             e.g., PO-2526000001  (FY 2025-26, sequence 1)
--             Creates dbo.PONumberSequence table
--             Recreates usp_SavePurchaseOrder with new number logic
-- Database  : dev_Restaurant
-- Safe      : Idempotent — re-runnable
-- =============================================

USE [dev_Restaurant];
GO

SET XACT_ABORT ON;
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 1 : Create PONumberSequence tracking table
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 1: Create dbo.PONumberSequence table ===';

IF OBJECT_ID(N'dbo.PONumberSequence', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PONumberSequence (
        FYCode   NVARCHAR(4)  NOT NULL,         -- e.g., '2526' for FY 2025-26
        LastSeq  INT          NOT NULL DEFAULT 0,
        CONSTRAINT PK_PONumberSequence PRIMARY KEY (FYCode)
    );
    PRINT '  dbo.PONumberSequence created.';
END
ELSE
BEGIN
    PRINT '  dbo.PONumberSequence already exists — skipped.';
END
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 2 : Seed sequence for current FY based on existing PO count
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 2: Seed sequence for current financial year ===';

DECLARE @FYStart INT =
    CASE WHEN MONTH(GETDATE()) >= 4 THEN YEAR(GETDATE()) ELSE YEAR(GETDATE()) - 1 END;
DECLARE @FYCode NVARCHAR(4) =
    RIGHT(CAST(@FYStart       AS NVARCHAR(4)), 2) +
    RIGHT(CAST(@FYStart + 1   AS NVARCHAR(4)), 2);

IF NOT EXISTS (SELECT 1 FROM dbo.PONumberSequence WHERE FYCode = @FYCode)
BEGIN
    -- Seed LastSeq to the current PO count so existing POs are not overwritten
    DECLARE @ExistingCount INT = ISNULL(
        (SELECT COUNT(*) FROM dbo.PurchaseOrder
         WHERE PONumber LIKE 'PO-' + @FYCode + '%'), 0);

    INSERT INTO dbo.PONumberSequence (FYCode, LastSeq) VALUES (@FYCode, @ExistingCount);

    PRINT '  Seeded FYCode=' + @FYCode + ' with LastSeq=' + CAST(@ExistingCount AS VARCHAR);
END
ELSE
BEGIN
    PRINT '  Sequence for FYCode=' + @FYCode + ' already exists — skipped.';
END
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 3 : Recreate usp_SavePurchaseOrder
--          New PO Number: PO-{FYCode}{6-digit-seq}  e.g., PO-2526000001
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 3: Recreate usp_SavePurchaseOrder ===';

IF OBJECT_ID(N'dbo.usp_SavePurchaseOrder', N'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_SavePurchaseOrder;
GO

CREATE PROCEDURE dbo.usp_SavePurchaseOrder
    @POId           INT,
    @BranchId       INT,
    @GodownId       INT,
    @SupplierId     INT,
    @PODate         DATE,
    @ExpectedDate   DATE          = NULL,
    @GSTType        NVARCHAR(20)  = 'Exclusive',
    @PaymentTerms   NVARCHAR(100) = NULL,
    @Remarks        NVARCHAR(500) = NULL,
    @SubTotal       DECIMAL(18,2) = 0,
    @TotalGSTAmount DECIMAL(18,2) = 0,
    @TotalAmount    DECIMAL(18,2) = 0,
    @UserId         INT           = NULL,
    @DetailsJson    NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @ActualPOId INT = @POId;

    -- ── UPDATE path (edit existing Draft PO) ──────────────────────────────────
    IF @POId > 0
    BEGIN
        UPDATE dbo.PurchaseOrder SET
            GodownId       = @GodownId,
            SupplierId     = @SupplierId,
            PODate         = @PODate,
            ExpectedDate   = @ExpectedDate,
            GSTType        = @GSTType,
            PaymentTerms   = @PaymentTerms,
            Remarks        = @Remarks,
            SubTotal       = @SubTotal,
            TotalGSTAmount = @TotalGSTAmount,
            TotalAmount    = @TotalAmount,
            UpdatedAt      = SYSUTCDATETIME()
        WHERE POId = @POId AND Status = 'Draft';

        DELETE FROM dbo.PurchaseOrderDetails WHERE POId = @POId;
    END
    ELSE
    BEGIN
        -- ── INSERT path: generate new PO number ──────────────────────────────

        -- Compute current Financial Year code
        -- FY 2025-26 → '2526', FY 2026-27 → '2627', etc.
        DECLARE @FYStart INT =
            CASE WHEN MONTH(@PODate) >= 4 THEN YEAR(@PODate) ELSE YEAR(@PODate) - 1 END;
        DECLARE @FYCode NVARCHAR(4) =
            RIGHT(CAST(@FYStart       AS NVARCHAR(4)), 2) +
            RIGHT(CAST(@FYStart + 1   AS NVARCHAR(4)), 2);

        -- Ensure FY row exists in sequence table
        IF NOT EXISTS (SELECT 1 FROM dbo.PONumberSequence WHERE FYCode = @FYCode)
            INSERT INTO dbo.PONumberSequence (FYCode, LastSeq) VALUES (@FYCode, 0);

        -- Atomically increment and capture Next Sequence
        DECLARE @NextSeq INT;
        UPDATE dbo.PONumberSequence
        SET @NextSeq = LastSeq = LastSeq + 1
        WHERE FYCode = @FYCode;

        -- Format: PO-YYYYNNNNNN  (e.g., PO-2526000001)
        DECLARE @PONumber NVARCHAR(30) =
            'PO-' + @FYCode +
            RIGHT('000000' + CAST(@NextSeq AS NVARCHAR(6)), 6);

        INSERT INTO dbo.PurchaseOrder
            (PONumber, BranchId, GodownId, SupplierId, PODate, ExpectedDate,
             GSTType, PaymentTerms, Remarks, Status,
             SubTotal, TotalGSTAmount, TotalAmount, CreatedAt, CreatedBy)
        VALUES
            (@PONumber, @BranchId, @GodownId, @SupplierId, @PODate, @ExpectedDate,
             @GSTType, @PaymentTerms, @Remarks, 'Draft',
             @SubTotal, @TotalGSTAmount, @TotalAmount, SYSUTCDATETIME(), @UserId);

        SET @ActualPOId = SCOPE_IDENTITY();
    END

    -- ── Insert detail lines from JSON (with GST breakdown) ───────────────────
    IF @DetailsJson IS NOT NULL AND LEN(@DetailsJson) > 2
    BEGIN
        INSERT INTO dbo.PurchaseOrderDetails
            (POId, ItemId, UOMId, OrderedQty, UnitRate,
             GSTPercent, CGSTPercent, SGSTPercent, IGSTPercent,
             CGSTAmount, SGSTAmount, IGSTAmount, Remarks)
        SELECT
            @ActualPOId,
            CAST(j.itemId     AS INT),
            CAST(j.uomId      AS INT),
            CAST(j.orderedQty AS DECIMAL(18,3)),
            CAST(j.unitRate   AS DECIMAL(18,4)),
            CAST(j.gstPercent AS DECIMAL(5,2)),
            -- CGST = SGST = GSTPercent / 2  (intra-state)
            CAST(j.gstPercent / 2 AS DECIMAL(5,2)),
            CAST(j.gstPercent / 2 AS DECIMAL(5,2)),
            -- IGST = GSTPercent             (inter-state)
            CAST(j.gstPercent AS DECIMAL(5,2)),
            -- Amounts
            CAST(j.orderedQty * j.unitRate * j.gstPercent / 200 AS DECIMAL(18,2)),
            CAST(j.orderedQty * j.unitRate * j.gstPercent / 200 AS DECIMAL(18,2)),
            CAST(j.orderedQty * j.unitRate * j.gstPercent / 100 AS DECIMAL(18,2)),
            j.remarks
        FROM OPENJSON(@DetailsJson)
        WITH (
            itemId     INT            '$.itemId',
            uomId      INT            '$.uomId',
            orderedQty DECIMAL(18,3)  '$.orderedQty',
            unitRate   DECIMAL(18,4)  '$.unitRate',
            gstPercent DECIMAL(5,2)   '$.gstPercent',
            remarks    NVARCHAR(200)  '$.remarks'
        ) j
        WHERE j.orderedQty > 0;
    END

    -- ── Recompute header totals from inserted lines ───────────────────────────
    UPDATE dbo.PurchaseOrder SET
        SubTotal       = ISNULL((SELECT SUM(OrderedQty * UnitRate)
                                 FROM dbo.PurchaseOrderDetails WHERE POId = @ActualPOId), 0),
        TotalGSTAmount = ISNULL((SELECT SUM(CGSTAmount + SGSTAmount)
                                 FROM dbo.PurchaseOrderDetails WHERE POId = @ActualPOId), 0),
        TotalAmount    = ISNULL((SELECT SUM(OrderedQty * UnitRate + CGSTAmount + SGSTAmount)
                                 FROM dbo.PurchaseOrderDetails WHERE POId = @ActualPOId), 0)
    WHERE POId = @ActualPOId;

    COMMIT;
    SELECT @ActualPOId AS POId;
END
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 4 : Show current sequence state
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 4: Current sequence state ===';
SELECT FYCode, LastSeq,
       'PO-' + FYCode + RIGHT('000000' + CAST(LastSeq + 1 AS NVARCHAR(6)), 6) AS NextPONumber
FROM dbo.PONumberSequence
ORDER BY FYCode DESC;
GO

PRINT '=== Migration complete. ===';
GO
