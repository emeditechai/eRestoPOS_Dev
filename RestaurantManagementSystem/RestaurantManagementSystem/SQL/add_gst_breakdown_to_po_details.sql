-- =============================================
-- Migration : add_gst_breakdown_to_po_details.sql
-- Purpose   : Add CGST / SGST / IGST columns to PurchaseOrderDetails
--             Update usp_SavePurchaseOrder  to store breakdown
--             Update usp_GetPurchaseOrderById to return breakdown
-- Database  : dev_Restaurant
-- Safe      : Idempotent — re-runnable
-- =============================================

USE [dev_Restaurant];
GO

SET XACT_ABORT ON;
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 1 : Add GST breakdown columns to PurchaseOrderDetails
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 1: Add GST breakdown columns to PurchaseOrderDetails ===';

IF COL_LENGTH('dbo.PurchaseOrderDetails', 'CGSTPercent') IS NULL
BEGIN
    ALTER TABLE dbo.PurchaseOrderDetails ADD CGSTPercent DECIMAL(5,2) NOT NULL DEFAULT 0;
    PRINT '  CGSTPercent added.';
END ELSE PRINT '  CGSTPercent already exists.';

IF COL_LENGTH('dbo.PurchaseOrderDetails', 'SGSTPercent') IS NULL
BEGIN
    ALTER TABLE dbo.PurchaseOrderDetails ADD SGSTPercent DECIMAL(5,2) NOT NULL DEFAULT 0;
    PRINT '  SGSTPercent added.';
END ELSE PRINT '  SGSTPercent already exists.';

IF COL_LENGTH('dbo.PurchaseOrderDetails', 'IGSTPercent') IS NULL
BEGIN
    ALTER TABLE dbo.PurchaseOrderDetails ADD IGSTPercent DECIMAL(5,2) NOT NULL DEFAULT 0;
    PRINT '  IGSTPercent added.';
END ELSE PRINT '  IGSTPercent already exists.';

IF COL_LENGTH('dbo.PurchaseOrderDetails', 'CGSTAmount') IS NULL
BEGIN
    ALTER TABLE dbo.PurchaseOrderDetails ADD CGSTAmount DECIMAL(18,2) NOT NULL DEFAULT 0;
    PRINT '  CGSTAmount added.';
END ELSE PRINT '  CGSTAmount already exists.';

IF COL_LENGTH('dbo.PurchaseOrderDetails', 'SGSTAmount') IS NULL
BEGIN
    ALTER TABLE dbo.PurchaseOrderDetails ADD SGSTAmount DECIMAL(18,2) NOT NULL DEFAULT 0;
    PRINT '  SGSTAmount added.';
END ELSE PRINT '  SGSTAmount already exists.';

IF COL_LENGTH('dbo.PurchaseOrderDetails', 'IGSTAmount') IS NULL
BEGIN
    ALTER TABLE dbo.PurchaseOrderDetails ADD IGSTAmount DECIMAL(18,2) NOT NULL DEFAULT 0;
    PRINT '  IGSTAmount added.';
END ELSE PRINT '  IGSTAmount already exists.';
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 2 : Back-fill CGST/SGST/IGST for existing rows
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 2: Back-fill existing PurchaseOrderDetails rows ===';

SET QUOTED_IDENTIFIER ON;  -- required when table has persisted computed columns
GO

UPDATE dbo.PurchaseOrderDetails
SET
    CGSTPercent = GSTPercent / 2,
    SGSTPercent = GSTPercent / 2,
    IGSTPercent = GSTPercent,
    CGSTAmount  = CAST(OrderedQty * UnitRate * GSTPercent / 200 AS DECIMAL(18,2)),
    SGSTAmount  = CAST(OrderedQty * UnitRate * GSTPercent / 200 AS DECIMAL(18,2)),
    IGSTAmount  = CAST(OrderedQty * UnitRate * GSTPercent / 100 AS DECIMAL(18,2))
WHERE CGSTPercent = 0 AND GSTPercent > 0;

PRINT '  Back-fill complete.';
GO

-- ─────────────────────────────────────────────────────────────────────────────
-- STEP 3 : Recreate usp_SavePurchaseOrder (store GST breakdown)
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
    @ExpectedDate   DATE        = NULL,
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
        DECLARE @PONumber NVARCHAR(30) =
            'PO-' + RIGHT('000' + CAST(@BranchId AS NVARCHAR), 3) + '-' +
            FORMAT(GETDATE(), 'yyyyMMdd') + '-' +
            RIGHT('0000' + CAST(ISNULL((
                SELECT COUNT(*) + 1 FROM dbo.PurchaseOrder WHERE BranchId = @BranchId
            ), 1) AS NVARCHAR), 4);

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

    -- Insert detail lines from JSON (with GST breakdown)
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
            -- CGST = SGST = GSTPercent / 2 (intra-state)
            CAST(j.gstPercent / 2 AS DECIMAL(5,2)),
            CAST(j.gstPercent / 2 AS DECIMAL(5,2)),
            -- IGST = GSTPercent (inter-state)
            CAST(j.gstPercent AS DECIMAL(5,2)),
            -- Amounts: Qty × Rate × %
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

    -- Recompute header totals from inserted lines
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
-- STEP 4 : Recreate usp_GetPurchaseOrderById (return GST breakdown)
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '=== STEP 4: Recreate usp_GetPurchaseOrderById ===';

IF OBJECT_ID(N'dbo.usp_GetPurchaseOrderById', N'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_GetPurchaseOrderById;
GO

CREATE PROCEDURE dbo.usp_GetPurchaseOrderById
    @POId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- RS1: header
    SELECT
        po.POId, po.PONumber, po.BranchId, po.GodownId, po.SupplierId,
        po.PODate, po.ExpectedDate, po.GSTType, po.PaymentTerms, po.Remarks,
        po.Status, po.SubTotal, po.TotalGSTAmount, po.TotalAmount,
        g.GodownName, p.PartyName AS SupplierName,
        (SELECT COUNT(*) FROM dbo.PurchaseOrderDetails WHERE POId = po.POId) AS LineCount,
        po.CreatedAt
    FROM dbo.PurchaseOrder po
    INNER JOIN dbo.Godowns g ON g.Id   = po.GodownId
    INNER JOIN dbo.Parties  p ON p.Id  = po.SupplierId
    WHERE po.POId = @POId;

    -- RS2: lines with CGST/SGST/IGST breakdown
    SELECT
        pd.PODetailId, pd.POId, pd.ItemId, pd.UOMId,
        pd.OrderedQty, pd.ReceivedQty, pd.UnitRate,
        pd.GSTPercent, pd.CGSTPercent, pd.SGSTPercent, pd.IGSTPercent,
        CAST(pd.OrderedQty * pd.UnitRate AS DECIMAL(18,2))          AS TaxableAmount,
        pd.CGSTAmount, pd.SGSTAmount, pd.IGSTAmount,
        pd.Remarks,
        i.IngredientsName AS ItemName, ISNULL(i.Code,'') AS ItemCode,
        u.UOMCode, u.UOMName
    FROM dbo.PurchaseOrderDetails pd
    INNER JOIN dbo.Ingredients i ON i.Id    = pd.ItemId
    INNER JOIN dbo.UomMaster   u ON u.UOMId = pd.UOMId
    WHERE pd.POId = @POId
    ORDER BY pd.PODetailId;
END
GO

PRINT '=== Migration complete. ===';
GO
