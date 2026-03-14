IF OBJECT_ID('dbo.usp_GetPurchaseRegisterDetails','P') IS NOT NULL
    DROP PROCEDURE dbo.usp_GetPurchaseRegisterDetails;
GO

CREATE PROCEDURE dbo.usp_GetPurchaseRegisterDetails
    @BranchId   INT,
    @FromDate   DATE,
    @ToDate     DATE,
    @SupplierId INT     = NULL,
    @GrnId      INT     = NULL       -- NULL = all GRNs in the date range
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        -- GRN Header
        gm.GRNId,
        gm.GRNNumber,
        gm.GRNDate,
        ISNULL(gm.InvoiceNo, '')        AS InvoiceNo,
        gm.InvoiceDate,
        ISNULL(p.PartyName, '')         AS SupplierName,
        ISNULL(gd.GodownName, '')       AS GodownName,
        ISNULL(gm.GSTType, 'CGST_SGST') AS GSTType,
        ISNULL(po.PONumber, '')         AS PONumber,

        -- Line Item
        gdet.GRNDetailId,
        ISNULL(ing.IngredientsName, '') AS ItemName,
        ISNULL(ing.Code, '')            AS ItemCode,
        ISNULL(u.UOMCode, '')           AS UOMCode,
        ISNULL(gdet.ReceivedQty, 0)     AS ReceivedQty,
        ISNULL(gdet.AcceptedQty, 0)     AS AcceptedQty,
        ISNULL(gdet.UnitRate, 0)        AS UnitRate,
        ISNULL(gdet.ReceivedQty, 0) * ISNULL(gdet.UnitRate, 0) AS TaxableAmount,

        -- GST breakdown
        ISNULL(gdet.GSTPercent, 0)      AS GSTPercent,
        CASE WHEN ISNULL(gm.GSTType,'CGST_SGST') = 'IGST'
             THEN ISNULL(gdet.GSTPercent, 0)   ELSE 0 END AS IGSTPercent,
        CASE WHEN ISNULL(gm.GSTType,'CGST_SGST') = 'IGST'
             THEN ISNULL(gdet.GSTAmount, 0)    ELSE 0 END AS IGSTAmount,
        CASE WHEN ISNULL(gm.GSTType,'CGST_SGST') <> 'IGST'
             THEN ISNULL(gdet.GSTPercent, 0) / 2  ELSE 0 END AS CGSTPercent,
        CASE WHEN ISNULL(gm.GSTType,'CGST_SGST') <> 'IGST'
             THEN ROUND(ISNULL(gdet.GSTAmount, 0) / 2, 2)  ELSE 0 END AS CGSTAmount,
        CASE WHEN ISNULL(gm.GSTType,'CGST_SGST') <> 'IGST'
             THEN ISNULL(gdet.GSTPercent, 0) / 2  ELSE 0 END AS SGSTPercent,
        CASE WHEN ISNULL(gm.GSTType,'CGST_SGST') <> 'IGST'
             THEN ROUND(ISNULL(gdet.GSTAmount, 0) / 2, 2)  ELSE 0 END AS SGSTAmount,

        ISNULL(gdet.GSTAmount, 0)       AS TotalGSTAmount,
        ISNULL(gdet.LineAmount, 0)      AS LineAmount,
        ISNULL(gdet.Remarks, '')        AS LineRemarks
    FROM dbo.GRNDetails  gdet
    JOIN dbo.GRNMaster   gm   ON gm.GRNId      = gdet.GRNId
    LEFT JOIN dbo.Parties     p    ON p.Id          = gm.SupplierId
    LEFT JOIN dbo.Godowns     gd   ON gd.Id         = gm.GodownId
    LEFT JOIN dbo.Ingredients ing  ON ing.Id         = gdet.ItemId
    LEFT JOIN dbo.UomMaster   u    ON u.UOMId        = gdet.UOMId
    LEFT JOIN dbo.PurchaseOrder po ON po.POId        = gm.POId
    WHERE
        gm.BranchId = @BranchId
        AND gm.GRNDate BETWEEN @FromDate AND @ToDate
        AND (@SupplierId IS NULL OR gm.SupplierId = @SupplierId)
        AND (@GrnId      IS NULL OR gm.GRNId      = @GrnId)
    ORDER BY gm.GRNDate DESC, gm.GRNNumber DESC, gdet.GRNDetailId;
END
GO
