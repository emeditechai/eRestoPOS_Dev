-- =============================================
-- Fix : fix_save_grn_sp_v2.sql
-- Purpose : Recreate usp_SaveGRN with QUOTED_IDENTIFIER ON.
--           AcceptedQty is a COMPUTED column (ReceivedQty - RejectedQty)
--           so it is NOT listed in the INSERT – SQL Server calculates it.
-- Database : dev_Restaurant
-- =============================================

USE [dev_Restaurant];
GO

SET QUOTED_IDENTIFIER ON;
GO

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

    -- AcceptedQty is a COMPUTED column; do NOT include it in the insert list
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

PRINT '=== usp_SaveGRN recreated successfully. ===';
GO
