-- =============================================
-- Script  : inventory_stored_procedures.sql
-- Purpose : All 35 stored procedures for the Inventory module
-- Run on  : dev_Restaurant
-- Safe    : Uses DROP IF EXISTS + CREATE; fully re-runnable
-- Prereq  : Run inventory_complete_setup.sql first (creates tables)
-- =============================================

USE [dev_Restaurant];
GO

SET XACT_ABORT ON;
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 1. usp_GetInventoryParameters
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetInventoryParameters', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetInventoryParameters;
GO
CREATE PROCEDURE dbo.usp_GetInventoryParameters
    @BranchId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        ParamId,
        BranchId,
        PurchaseOnlyFromMainGodown,
        GRNMandatory,
        AllowDirectPurchase,
        TransferPriceMode,
        NegativeStockAllowed,
        AutoConsumptionOnSale,
        UpdatedAt
    FROM dbo.InventoryParameters
    WHERE BranchId = @BranchId;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 2. usp_SaveInventoryParameters
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_SaveInventoryParameters', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_SaveInventoryParameters;
GO
CREATE PROCEDURE dbo.usp_SaveInventoryParameters
    @BranchId                   INT,
    @PurchaseOnlyFromMainGodown BIT,
    @GRNMandatory               BIT,
    @AllowDirectPurchase        BIT,
    @TransferPriceMode          NVARCHAR(20),
    @NegativeStockAllowed       BIT,
    @AutoConsumptionOnSale      BIT,
    @UserId                     INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.InventoryParameters WHERE BranchId = @BranchId)
        UPDATE dbo.InventoryParameters SET
            PurchaseOnlyFromMainGodown  = @PurchaseOnlyFromMainGodown,
            GRNMandatory               = @GRNMandatory,
            AllowDirectPurchase        = @AllowDirectPurchase,
            TransferPriceMode          = @TransferPriceMode,
            NegativeStockAllowed       = @NegativeStockAllowed,
            AutoConsumptionOnSale      = @AutoConsumptionOnSale,
            UpdatedAt                  = SYSUTCDATETIME(),
            UpdatedBy                  = @UserId
        WHERE BranchId = @BranchId;
    ELSE
        INSERT INTO dbo.InventoryParameters
            (BranchId, PurchaseOnlyFromMainGodown, GRNMandatory, AllowDirectPurchase,
             TransferPriceMode, NegativeStockAllowed, AutoConsumptionOnSale, UpdatedAt, UpdatedBy)
        VALUES
            (@BranchId, @PurchaseOnlyFromMainGodown, @GRNMandatory, @AllowDirectPurchase,
             @TransferPriceMode, @NegativeStockAllowed, @AutoConsumptionOnSale, SYSUTCDATETIME(), @UserId);
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 3. usp_GetOpeningStockList
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetOpeningStockList', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetOpeningStockList;
GO
CREATE PROCEDURE dbo.usp_GetOpeningStockList
    @BranchId INT,
    @GodownId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        os.OpeningStockId,
        os.BranchId,
        os.GodownId,
        os.ItemId,
        os.StockDate,
        os.Quantity,
        os.UOMId,
        os.CostPrice,
        os.TotalValue,
        os.Remarks,
        os.IsPosted,
        os.CreatedAt,
        i.IngredientsName   AS ItemName,
        u.UOMCode,
        g.GodownName
    FROM dbo.OpeningStock os
    INNER JOIN dbo.Ingredients i    ON i.Id    = os.ItemId
    INNER JOIN dbo.UomMaster u      ON u.UOMId = os.UOMId
    INNER JOIN dbo.Godowns g        ON g.Id    = os.GodownId
    WHERE os.BranchId = @BranchId
      AND (@GodownId IS NULL OR os.GodownId = @GodownId)
    ORDER BY os.StockDate DESC, i.IngredientsName;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 4. usp_GetOpeningStockById
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetOpeningStockById', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetOpeningStockById;
GO
CREATE PROCEDURE dbo.usp_GetOpeningStockById
    @OpeningStockId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        os.OpeningStockId,
        os.BranchId,
        os.GodownId,
        os.ItemId,
        os.StockDate,
        os.Quantity,
        os.UOMId,
        os.CostPrice,
        os.TotalValue,
        os.Remarks,
        os.IsPosted,
        i.IngredientsName   AS ItemName,
        u.UOMCode,
        g.GodownName
    FROM dbo.OpeningStock os
    INNER JOIN dbo.Ingredients i    ON i.Id    = os.ItemId
    INNER JOIN dbo.UomMaster u      ON u.UOMId = os.UOMId
    INNER JOIN dbo.Godowns g        ON g.Id    = os.GodownId
    WHERE os.OpeningStockId = @OpeningStockId;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 5. usp_SaveOpeningStock
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_SaveOpeningStock', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_SaveOpeningStock;
GO
CREATE PROCEDURE dbo.usp_SaveOpeningStock
    @OpeningStockId INT,
    @BranchId       INT,
    @GodownId       INT,
    @ItemId         INT,
    @StockDate      DATE,
    @Quantity       DECIMAL(18,3),
    @UOMId          INT,
    @CostPrice      DECIMAL(18,4),
    @Remarks        NVARCHAR(300) = NULL,
    @UserId         INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @OpeningStockId > 0
    BEGIN
        UPDATE dbo.OpeningStock SET
            GodownId   = @GodownId,
            ItemId     = @ItemId,
            StockDate  = @StockDate,
            Quantity   = @Quantity,
            UOMId      = @UOMId,
            CostPrice  = @CostPrice,
            Remarks    = @Remarks,
            UpdatedAt  = SYSUTCDATETIME()
        WHERE OpeningStockId = @OpeningStockId AND IsPosted = 0;
        SELECT @OpeningStockId AS OpeningStockId;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.OpeningStock
            (BranchId, GodownId, ItemId, StockDate, Quantity, UOMId, CostPrice, Remarks, IsPosted, CreatedAt, CreatedBy)
        VALUES
            (@BranchId, @GodownId, @ItemId, @StockDate, @Quantity, @UOMId, @CostPrice, @Remarks, 0, SYSUTCDATETIME(), @UserId);
        SELECT SCOPE_IDENTITY() AS OpeningStockId;
    END
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 6. usp_PostOpeningStock
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_PostOpeningStock', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_PostOpeningStock;
GO
CREATE PROCEDURE dbo.usp_PostOpeningStock
    @OpeningStockId INT,
    @UserId         INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @BranchId INT, @GodownId INT, @ItemId INT, @StockDate DATE,
            @Qty DECIMAL(18,3), @UOMId INT, @Cost DECIMAL(18,4);

    SELECT @BranchId = BranchId, @GodownId = GodownId, @ItemId = ItemId,
           @StockDate = StockDate, @Qty = Quantity, @UOMId = UOMId, @Cost = CostPrice
    FROM dbo.OpeningStock
    WHERE OpeningStockId = @OpeningStockId AND IsPosted = 0;

    IF @BranchId IS NULL
    BEGIN
        ROLLBACK;
        RAISERROR('Opening stock not found or already posted.', 16, 1);
        RETURN;
    END

    -- Compute new balance
    DECLARE @PrevBalance DECIMAL(18,3) = 0, @PrevAvgCost DECIMAL(18,4) = 0,
            @PrevValue DECIMAL(18,2) = 0;
    SELECT @PrevBalance = BalanceQty, @PrevAvgCost = AverageCost,
           @PrevValue = BalanceQty * AverageCost
    FROM dbo.CurrentStock
    WHERE BranchId = @BranchId AND GodownId = @GodownId AND ItemId = @ItemId;

    DECLARE @NewBalance DECIMAL(18,3) = @PrevBalance + @Qty;
    DECLARE @NewAvgCost DECIMAL(18,4) =
        CASE WHEN @NewBalance > 0 THEN (@PrevValue + @Qty * @Cost) / @NewBalance ELSE @Cost END;

    -- Update CurrentStock
    IF EXISTS (SELECT 1 FROM dbo.CurrentStock WHERE BranchId=@BranchId AND GodownId=@GodownId AND ItemId=@ItemId)
        UPDATE dbo.CurrentStock SET BalanceQty = @NewBalance, AverageCost = @NewAvgCost, LastUpdated = SYSUTCDATETIME()
        WHERE BranchId=@BranchId AND GodownId=@GodownId AND ItemId=@ItemId;
    ELSE
        INSERT INTO dbo.CurrentStock (BranchId, GodownId, ItemId, BalanceQty, AverageCost, LastUpdated)
        VALUES (@BranchId, @GodownId, @ItemId, @NewBalance, @NewAvgCost, SYSUTCDATETIME());

    -- Write ledger entry
    INSERT INTO dbo.StockLedger
        (BranchId, GodownId, ItemId, TransactionDate, TransactionType, ReferenceType,
         ReferenceId, InQuantity, OutQuantity, UnitCost, BalanceQty, BalanceValue, AverageCost, CreatedAt, CreatedBy)
    VALUES
        (@BranchId, @GodownId, @ItemId, @StockDate, 'OPENING', 'OPENING',
         @OpeningStockId, @Qty, 0, @Cost, @NewBalance, @NewBalance * @NewAvgCost, @NewAvgCost,
         SYSUTCDATETIME(), @UserId);

    -- Mark as posted
    UPDATE dbo.OpeningStock SET IsPosted = 1, UpdatedAt = SYSUTCDATETIME()
    WHERE OpeningStockId = @OpeningStockId;

    COMMIT;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 7. usp_DeleteOpeningStock
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_DeleteOpeningStock', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_DeleteOpeningStock;
GO
CREATE PROCEDURE dbo.usp_DeleteOpeningStock
    @OpeningStockId INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.OpeningStock WHERE OpeningStockId = @OpeningStockId AND IsPosted = 0;
    IF @@ROWCOUNT = 0
        RAISERROR('Cannot delete: record not found or already posted.', 16, 1);
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 8. usp_GetStockLedger
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetStockLedger', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetStockLedger;
GO
CREATE PROCEDURE dbo.usp_GetStockLedger
    @BranchId  INT,
    @GodownId  INT     = NULL,
    @ItemId    INT     = NULL,
    @TxnType   NVARCHAR(30) = NULL,
    @FromDate  DATE    = NULL,
    @ToDate    DATE    = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        sl.LedgerId,
        sl.BranchId,
        sl.GodownId,
        sl.ItemId,
        sl.TransactionDate,
        sl.TransactionType,
        sl.ReferenceNumber,
        sl.InQuantity,
        sl.OutQuantity,
        sl.UnitCost,
        sl.BalanceQty,
        sl.AverageCost,
        sl.Remarks,
        i.IngredientsName   AS ItemName,
        ISNULL(u.UOMCode,'') AS UOMCode,
        g.GodownName
    FROM dbo.StockLedger sl
    INNER JOIN dbo.Ingredients i    ON i.Id    = sl.ItemId
    INNER JOIN dbo.Godowns g        ON g.Id    = sl.GodownId
    LEFT  JOIN dbo.UomMaster u      ON u.UOMId = (SELECT TOP 1 BaseUOMId FROM dbo.Ingredients WHERE Id = sl.ItemId)
    WHERE sl.BranchId = @BranchId
      AND (@GodownId IS NULL OR sl.GodownId = @GodownId)
      AND (@ItemId   IS NULL OR sl.ItemId   = @ItemId)
      AND (@TxnType  IS NULL OR sl.TransactionType = @TxnType)
      AND (@FromDate IS NULL OR sl.TransactionDate >= @FromDate)
      AND (@ToDate   IS NULL OR sl.TransactionDate <= @ToDate)
    ORDER BY sl.TransactionDate DESC, sl.LedgerId DESC;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 9. usp_GetCurrentStockSummary
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetCurrentStockSummary', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetCurrentStockSummary;
GO
CREATE PROCEDURE dbo.usp_GetCurrentStockSummary
    @BranchId INT,
    @GodownId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        cs.StockId,
        cs.BranchId,
        cs.GodownId,
        cs.ItemId,
        cs.BalanceQty,
        cs.AverageCost,
        i.IngredientsName   AS ItemName,
        ISNULL(i.Code,'')   AS ItemCode,
        ISNULL(u.UOMCode,'') AS BaseUOMCode,
        g.GodownName,
        CASE WHEN g.IsMainGodown = 1 THEN 'Main' ELSE 'Sub' END AS GodownType,
        CAST(0 AS BIT)       AS IsLowStock
    FROM dbo.CurrentStock cs
    INNER JOIN dbo.Ingredients i    ON i.Id    = cs.ItemId
    INNER JOIN dbo.Godowns g        ON g.Id    = cs.GodownId
    LEFT  JOIN dbo.UomMaster u      ON u.UOMId = i.PurchaseUOMId
    WHERE cs.BranchId = @BranchId
      AND (@GodownId IS NULL OR cs.GodownId = @GodownId)
      AND cs.BalanceQty <> 0
    ORDER BY g.GodownName, i.IngredientsName;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 10. usp_GetClosingStockReport
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetClosingStockReport', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetClosingStockReport;
GO
CREATE PROCEDURE dbo.usp_GetClosingStockReport
    @BranchId INT,
    @AsOfDate DATE,
    @GodownId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        i.IngredientsName               AS ItemName,
        ISNULL(i.Code,'')               AS ItemCode,
        g.GodownName,
        ISNULL(SUM(CASE WHEN sl.TransactionType = 'OPENING' THEN sl.InQuantity ELSE 0 END), 0)        AS OpeningQty,
        ISNULL(SUM(CASE WHEN sl.TransactionType IN ('GRN','PURCHASE')  THEN sl.InQuantity ELSE 0 END), 0) AS PurchaseQty,
        ISNULL(SUM(CASE WHEN sl.TransactionType = 'TRANSFER_IN'  THEN sl.InQuantity  ELSE 0 END), 0)  AS TransferInQty,
        ISNULL(SUM(CASE WHEN sl.TransactionType = 'TRANSFER_OUT' THEN sl.OutQuantity ELSE 0 END), 0)  AS TransferOutQty,
        ISNULL(SUM(CASE WHEN sl.TransactionType = 'DAMAGE'       THEN sl.OutQuantity ELSE 0 END), 0)  AS DamageQty,
        ISNULL(SUM(CASE WHEN sl.TransactionType = 'SALE'         THEN sl.OutQuantity ELSE 0 END), 0)  AS SaleQty,
        ISNULL(
            SUM(sl.InQuantity - sl.OutQuantity), 0
        )                               AS ClosingQty,
        ISNULL(MAX(sl.AverageCost), 0)  AS AverageCost,
        ISNULL(SUM(sl.InQuantity - sl.OutQuantity), 0)
            * ISNULL(MAX(sl.AverageCost), 0) AS ClosingValue
    FROM dbo.StockLedger sl
    INNER JOIN dbo.Ingredients i    ON i.Id  = sl.ItemId
    INNER JOIN dbo.Godowns g        ON g.Id  = sl.GodownId
    WHERE sl.BranchId       = @BranchId
      AND sl.TransactionDate <= @AsOfDate
      AND (@GodownId IS NULL  OR sl.GodownId = @GodownId)
    GROUP BY i.IngredientsName, i.Code, g.GodownName
    HAVING ISNULL(SUM(sl.InQuantity - sl.OutQuantity), 0) <> 0
    ORDER BY g.GodownName, i.IngredientsName;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 11. usp_GetStockValuationReport
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetStockValuationReport', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetStockValuationReport;
GO
CREATE PROCEDURE dbo.usp_GetStockValuationReport
    @BranchId INT,
    @GodownId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        cs.GodownId,
        g.GodownName,
        cs.ItemId,
        i.IngredientsName   AS ItemName,
        ISNULL(i.Code,'')   AS ItemCode,
        ISNULL(u.UOMCode,'') AS UOMCode,
        cs.BalanceQty,
        cs.AverageCost,
        (cs.BalanceQty * cs.AverageCost) AS StockValue
    FROM dbo.CurrentStock cs
    INNER JOIN dbo.Ingredients i    ON i.Id    = cs.ItemId
    INNER JOIN dbo.Godowns g        ON g.Id    = cs.GodownId
    LEFT  JOIN dbo.UomMaster u      ON u.UOMId = i.PurchaseUOMId
    WHERE cs.BranchId = @BranchId
      AND (@GodownId IS NULL OR cs.GodownId = @GodownId)
      AND cs.BalanceQty <> 0
    ORDER BY g.GodownName, i.IngredientsName;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 12. usp_GetPurchaseRegister
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetPurchaseRegister', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetPurchaseRegister;
GO
CREATE PROCEDURE dbo.usp_GetPurchaseRegister
    @BranchId   INT,
    @FromDate   DATE,
    @ToDate     DATE,
    @SupplierId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        g.GRNId,
        g.GRNNumber,
        g.GRNDate,
        ISNULL(g.InvoiceNo,'')  AS InvoiceNo,
        p.PartyName             AS SupplierName,
        gd.GodownName,
        g.SubTotal,
        g.TotalGSTAmount,
        g.TotalAmount,
        ISNULL(po.PONumber,'')  AS PONumber
    FROM dbo.GRNMaster g
    INNER JOIN dbo.Parties p        ON p.Id  = g.SupplierId
    INNER JOIN dbo.Godowns gd       ON gd.Id = g.GodownId
    LEFT  JOIN dbo.PurchaseOrder po ON po.POId = g.POId
    WHERE g.BranchId    = @BranchId
      AND g.GRNDate    >= @FromDate
      AND g.GRNDate    <= @ToDate
      AND g.Status      = 'Posted'
      AND (@SupplierId IS NULL OR g.SupplierId = @SupplierId)
    ORDER BY g.GRNDate DESC, g.GRNNumber;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 13. usp_GetTransferRegister
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetTransferRegister', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetTransferRegister;
GO
CREATE PROCEDURE dbo.usp_GetTransferRegister
    @BranchId INT,
    @FromDate DATE,
    @ToDate   DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        st.TransferId,
        st.TransferNumber,
        st.TransferDate,
        st.TransferType,
        fg.GodownName   AS FromGodownName,
        tg.GodownName   AS ToGodownName,
        st.TotalQty,
        st.TotalValue,
        st.Status,
        ISNULL(st.Remarks,'') AS Remarks
    FROM dbo.StockTransfer st
    INNER JOIN dbo.Godowns fg ON fg.Id = st.FromGodownId
    INNER JOIN dbo.Godowns tg ON tg.Id = st.ToGodownId
    WHERE st.BranchId      = @BranchId
      AND st.TransferDate >= @FromDate
      AND st.TransferDate <= @ToDate
      AND st.Status        = 'Posted'
    ORDER BY st.TransferDate DESC, st.TransferNumber;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 14. usp_GetDamageRegister
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetDamageRegister', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetDamageRegister;
GO
CREATE PROCEDURE dbo.usp_GetDamageRegister
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
        ISNULL(de.Remarks,'') AS Remarks,
        de.Status
    FROM dbo.DamageEntry de
    INNER JOIN dbo.Godowns g ON g.Id = de.GodownId
    WHERE de.BranchId     = @BranchId
      AND de.DamageDate  >= @FromDate
      AND de.DamageDate  <= @ToDate
      AND de.Status       = 'Posted'
    ORDER BY de.DamageDate DESC, de.DamageNumber;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 15. usp_GetInventoryDashboardStats  (3 result sets)
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetInventoryDashboardStats', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetInventoryDashboardStats;
GO
CREATE PROCEDURE dbo.usp_GetInventoryDashboardStats
    @BranchId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- RS1: Scalar stats
    SELECT
        ISNULL(SUM(cs.BalanceQty * cs.AverageCost), 0)                         AS TotalStockValue,
        ISNULL((SELECT COUNT(*) FROM dbo.CurrentStock cs2
                WHERE cs2.BranchId = @BranchId AND cs2.BalanceQty <= 0
               ), 0)                                                            AS LowStockItems,
        ISNULL((SELECT COUNT(*) FROM dbo.GRNMaster
                WHERE BranchId = @BranchId AND Status = 'Draft'), 0)           AS PendingGRN,
        ISNULL((SELECT SUM(TotalAmount) FROM dbo.GRNMaster
                WHERE BranchId = @BranchId AND Status = 'Posted'
                  AND GRNDate = CAST(GETDATE() AS DATE)), 0)                   AS TodayPurchase,
        CAST(0 AS DECIMAL(18,2))                                               AS TodayConsumption,
        ISNULL((SELECT COUNT(*) FROM dbo.Godowns
                WHERE BranchId = @BranchId AND IsActive = 1), 0)              AS ActiveGodowns,
        ISNULL((SELECT COUNT(*) FROM dbo.DamageEntry
                WHERE BranchId = @BranchId
                  AND DamageDate = CAST(GETDATE() AS DATE)), 0)               AS TodayDamageCount
    FROM dbo.CurrentStock cs
    WHERE cs.BranchId = @BranchId;

    -- RS2: Top 10 consumed items (by outward ledger qty)
    SELECT TOP 10
        i.IngredientsName   AS ItemName,
        ISNULL(i.Code,'')   AS ItemCode,
        ISNULL(SUM(sl.OutQuantity), 0) AS TotalConsumed,
        ISNULL(u.UOMCode,'') AS UOMCode
    FROM dbo.StockLedger sl
    INNER JOIN dbo.Ingredients i    ON i.Id    = sl.ItemId
    LEFT  JOIN dbo.UomMaster u      ON u.UOMId = i.PurchaseUOMId
    WHERE sl.BranchId           = @BranchId
      AND sl.TransactionDate   >= DATEADD(DAY, -30, CAST(GETDATE() AS DATE))
      AND sl.TransactionType NOT IN ('OPENING','TRANSFER_IN','GRN','PURCHASE')
    GROUP BY i.IngredientsName, i.Code, u.UOMCode
    ORDER BY SUM(sl.OutQuantity) DESC;

    -- RS3: Low stock alerts
    SELECT
        i.IngredientsName   AS ItemName,
        ISNULL(i.Code,'')   AS ItemCode,
        cs.BalanceQty,
        ISNULL(u.UOMCode,'') AS UOMCode,
        g.GodownName
    FROM dbo.CurrentStock cs
    INNER JOIN dbo.Ingredients i    ON i.Id    = cs.ItemId
    INNER JOIN dbo.Godowns g        ON g.Id    = cs.GodownId
    LEFT  JOIN dbo.UomMaster u      ON u.UOMId = i.PurchaseUOMId
    WHERE cs.BranchId = @BranchId
      AND cs.BalanceQty <= 0
    ORDER BY i.IngredientsName;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 16. usp_GetItemAverageCost
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetItemAverageCost', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetItemAverageCost;
GO
CREATE PROCEDURE dbo.usp_GetItemAverageCost
    @ItemId   INT,
    @GodownId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ISNULL(AverageCost, 0) AS AverageCost
    FROM dbo.CurrentStock
    WHERE ItemId = @ItemId AND GodownId = @GodownId;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 17. usp_GetPurchaseOrderList
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetPurchaseOrderList', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetPurchaseOrderList;
GO
CREATE PROCEDURE dbo.usp_GetPurchaseOrderList
    @BranchId INT,
    @Status   NVARCHAR(20) = NULL,
    @FromDate DATE = NULL,
    @ToDate   DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        po.POId,
        po.PONumber,
        po.BranchId,
        po.GodownId,
        po.SupplierId,
        po.PODate,
        po.ExpectedDate,
        po.GSTType,
        po.PaymentTerms,
        po.Remarks,
        po.Status,
        po.SubTotal,
        po.TotalGSTAmount,
        po.TotalAmount,
        g.GodownName,
        p.PartyName     AS SupplierName,
        (SELECT COUNT(*) FROM dbo.PurchaseOrderDetails WHERE POId = po.POId) AS LineCount,
        po.CreatedAt
    FROM dbo.PurchaseOrder po
    INNER JOIN dbo.Godowns g  ON g.Id = po.GodownId
    INNER JOIN dbo.Parties p  ON p.Id = po.SupplierId
    WHERE po.BranchId = @BranchId
      AND (@Status   IS NULL OR po.Status   = @Status)
      AND (@FromDate IS NULL OR po.PODate  >= @FromDate)
      AND (@ToDate   IS NULL OR po.PODate  <= @ToDate)
    ORDER BY po.PODate DESC, po.PONumber DESC;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 18. usp_GetPurchaseOrderById  (RS1=header, RS2=lines)
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetPurchaseOrderById', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetPurchaseOrderById;
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
    INNER JOIN dbo.Godowns g ON g.Id = po.GodownId
    INNER JOIN dbo.Parties  p ON p.Id = po.SupplierId
    WHERE po.POId = @POId;

    -- RS2: lines
    SELECT
        pd.PODetailId, pd.POId, pd.ItemId, pd.UOMId,
        pd.OrderedQty, pd.ReceivedQty, pd.UnitRate, pd.GSTPercent, pd.Remarks,
        i.IngredientsName AS ItemName, ISNULL(i.Code,'') AS ItemCode,
        u.UOMCode, u.UOMName
    FROM dbo.PurchaseOrderDetails pd
    INNER JOIN dbo.Ingredients i ON i.Id    = pd.ItemId
    INNER JOIN dbo.UomMaster u   ON u.UOMId = pd.UOMId
    WHERE pd.POId = @POId
    ORDER BY pd.PODetailId;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 19. usp_SavePurchaseOrder
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_SavePurchaseOrder', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_SavePurchaseOrder;
GO
CREATE PROCEDURE dbo.usp_SavePurchaseOrder
    @POId           INT,
    @BranchId       INT,
    @GodownId       INT,
    @SupplierId     INT,
    @PODate         DATE,
    @ExpectedDate   DATE = NULL,
    @GSTType        NVARCHAR(20) = 'Exclusive',
    @PaymentTerms   NVARCHAR(100) = NULL,
    @Remarks        NVARCHAR(500) = NULL,
    @SubTotal       DECIMAL(18,2) = 0,
    @TotalGSTAmount DECIMAL(18,2) = 0,
    @TotalAmount    DECIMAL(18,2) = 0,
    @UserId         INT = NULL,
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
            RIGHT('0000' + CAST(ISNULL((SELECT COUNT(*)+1 FROM dbo.PurchaseOrder WHERE BranchId=@BranchId),1) AS NVARCHAR),4);

        INSERT INTO dbo.PurchaseOrder
            (PONumber, BranchId, GodownId, SupplierId, PODate, ExpectedDate, GSTType,
             PaymentTerms, Remarks, Status, SubTotal, TotalGSTAmount, TotalAmount, CreatedAt, CreatedBy)
        VALUES
            (@PONumber, @BranchId, @GodownId, @SupplierId, @PODate, @ExpectedDate, @GSTType,
             @PaymentTerms, @Remarks, 'Draft', @SubTotal, @TotalGSTAmount, @TotalAmount,
             SYSUTCDATETIME(), @UserId);

        SET @ActualPOId = SCOPE_IDENTITY();
    END

    -- Insert detail lines from JSON
    IF @DetailsJson IS NOT NULL AND LEN(@DetailsJson) > 2
    BEGIN
        INSERT INTO dbo.PurchaseOrderDetails (POId, ItemId, UOMId, OrderedQty, UnitRate, GSTPercent, Remarks)
        SELECT
            @ActualPOId,
            CAST(j.itemId     AS INT),
            CAST(j.uomId      AS INT),
            CAST(j.orderedQty AS DECIMAL(18,3)),
            CAST(j.unitRate   AS DECIMAL(18,4)),
            CAST(j.gstPercent AS DECIMAL(5,2)),
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

    COMMIT;
    SELECT @ActualPOId AS POId;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 20. usp_ApprovePurchaseOrder
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_ApprovePurchaseOrder', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_ApprovePurchaseOrder;
GO
CREATE PROCEDURE dbo.usp_ApprovePurchaseOrder
    @POId   INT,
    @UserId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.PurchaseOrder SET
        Status     = 'Approved',
        ApprovedBy = @UserId,
        ApprovedAt = SYSUTCDATETIME(),
        UpdatedAt  = SYSUTCDATETIME()
    WHERE POId = @POId AND Status = 'Draft';
    IF @@ROWCOUNT = 0
        RAISERROR('Purchase order not found or not in Draft status.', 16, 1);
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 21. usp_CancelPurchaseOrder
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_CancelPurchaseOrder', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_CancelPurchaseOrder;
GO
CREATE PROCEDURE dbo.usp_CancelPurchaseOrder
    @POId   INT,
    @UserId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.PurchaseOrder SET
        Status    = 'Cancelled',
        UpdatedAt = SYSUTCDATETIME()
    WHERE POId = @POId AND Status IN ('Draft', 'Approved');
    IF @@ROWCOUNT = 0
        RAISERROR('Purchase order not found or cannot be cancelled.', 16, 1);
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 22. usp_GetGRNList
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetGRNList', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetGRNList;
GO
CREATE PROCEDURE dbo.usp_GetGRNList
    @BranchId INT,
    @Status   NVARCHAR(20) = NULL,
    @FromDate DATE = NULL,
    @ToDate   DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        gm.GRNId, gm.GRNNumber, gm.BranchId, gm.POId,
        gm.GodownId, gm.SupplierId, gm.GRNDate, gm.InvoiceNo,
        gm.GSTType, gm.Remarks, gm.SubTotal, gm.TotalGSTAmount,
        gm.TotalAmount, gm.Status,
        g.GodownName, p.PartyName AS SupplierName,
        ISNULL(po.PONumber,'') AS PONumber,
        (SELECT COUNT(*) FROM dbo.GRNDetails WHERE GRNId = gm.GRNId) AS LineCount,
        gm.CreatedAt
    FROM dbo.GRNMaster gm
    INNER JOIN dbo.Godowns g        ON g.Id  = gm.GodownId
    INNER JOIN dbo.Parties p        ON p.Id  = gm.SupplierId
    LEFT  JOIN dbo.PurchaseOrder po ON po.POId = gm.POId
    WHERE gm.BranchId = @BranchId
      AND (@Status   IS NULL OR gm.Status   = @Status)
      AND (@FromDate IS NULL OR gm.GRNDate >= @FromDate)
      AND (@ToDate   IS NULL OR gm.GRNDate <= @ToDate)
    ORDER BY gm.GRNDate DESC, gm.GRNNumber DESC;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 23. usp_GetGRNById  (RS1=header, RS2=lines)
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetGRNById', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetGRNById;
GO
CREATE PROCEDURE dbo.usp_GetGRNById
    @GRNId INT
AS
BEGIN
    SET NOCOUNT ON;
    -- RS1: header
    SELECT
        gm.GRNId, gm.GRNNumber, gm.BranchId, gm.POId,
        gm.GodownId, gm.SupplierId, gm.GRNDate, gm.InvoiceNo, gm.InvoiceDate,
        gm.GSTType, gm.Remarks, gm.SubTotal, gm.TotalGSTAmount,
        gm.TotalAmount, gm.Status,
        g.GodownName, p.PartyName AS SupplierName,
        ISNULL(po.PONumber,'') AS PONumber,
        (SELECT COUNT(*) FROM dbo.GRNDetails WHERE GRNId = gm.GRNId) AS LineCount,
        gm.CreatedAt
    FROM dbo.GRNMaster gm
    INNER JOIN dbo.Godowns g        ON g.Id    = gm.GodownId
    INNER JOIN dbo.Parties p        ON p.Id    = gm.SupplierId
    LEFT  JOIN dbo.PurchaseOrder po ON po.POId = gm.POId
    WHERE gm.GRNId = @GRNId;

    -- RS2: lines
    SELECT
        gd.GRNDetailId, gd.GRNId, gd.PODetailId, gd.ItemId, gd.UOMId,
        gd.OrderedQty, gd.ReceivedQty, gd.RejectedQty, gd.AcceptedQty,
        gd.UnitRate, gd.GSTPercent, gd.Remarks,
        i.IngredientsName AS ItemName, u.UOMCode, u.UOMName
    FROM dbo.GRNDetails gd
    INNER JOIN dbo.Ingredients i ON i.Id    = gd.ItemId
    INNER JOIN dbo.UomMaster u   ON u.UOMId = gd.UOMId
    WHERE gd.GRNId = @GRNId
    ORDER BY gd.GRNDetailId;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 24. usp_GetPOForGRN  (returns Approved POs eligible for GRN)
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetPOForGRN', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetPOForGRN;
GO
CREATE PROCEDURE dbo.usp_GetPOForGRN
    @BranchId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        po.POId,
        po.PONumber,
        p.PartyName     AS SupplierName,
        g.GodownName,
        po.GodownId,
        po.SupplierId
    FROM dbo.PurchaseOrder po
    INNER JOIN dbo.Godowns g ON g.Id = po.GodownId
    INNER JOIN dbo.Parties  p ON p.Id = po.SupplierId
    WHERE po.BranchId = @BranchId
      AND po.Status IN ('Approved', 'PartialGRN')
    ORDER BY po.PODate DESC, po.PONumber;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 25. usp_GetPODetailsForGRN
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetPODetailsForGRN', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetPODetailsForGRN;
GO
CREATE PROCEDURE dbo.usp_GetPODetailsForGRN
    @POId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        pd.PODetailId,
        pd.ItemId,
        pd.UOMId,
        pd.OrderedQty,
        pd.ReceivedQty,
        pd.PendingQty,
        pd.UnitRate,
        pd.GSTPercent,
        pd.Remarks,
        i.IngredientsName AS ItemName,
        ISNULL(i.Code,'') AS ItemCode,
        u.UOMCode,
        u.UOMName
    FROM dbo.PurchaseOrderDetails pd
    INNER JOIN dbo.Ingredients i ON i.Id    = pd.ItemId
    INNER JOIN dbo.UomMaster u   ON u.UOMId = pd.UOMId
    WHERE pd.POId = @POId
      AND pd.PendingQty > 0
    ORDER BY pd.PODetailId;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 26. usp_SaveGRN
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_SaveGRN', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_SaveGRN;
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
        DECLARE @GRNNumber NVARCHAR(30) =
            'GRN-' + RIGHT('000' + CAST(@BranchId AS NVARCHAR), 3) + '-' +
            FORMAT(GETDATE(), 'yyyyMMdd') + '-' +
            RIGHT('0000' + CAST(ISNULL((SELECT COUNT(*)+1 FROM dbo.GRNMaster WHERE BranchId=@BranchId),1) AS NVARCHAR),4);

        INSERT INTO dbo.GRNMaster
            (GRNNumber, BranchId, POId, GodownId, SupplierId, GRNDate, InvoiceNo, InvoiceDate,
             GSTType, SubTotal, TotalGSTAmount, TotalAmount, Status, Remarks, CreatedAt, CreatedBy)
        VALUES
            (@GRNNumber, @BranchId, NULLIF(@POId,0), @GodownId, @SupplierId, @GRNDate, @InvoiceNo, @InvoiceDate,
             @GSTType, @SubTotal, @TotalGSTAmount, @TotalAmount, 'Draft', @Remarks, SYSUTCDATETIME(), @UserId);

        SET @ActualGRNId = SCOPE_IDENTITY();
    END

    IF @DetailsJson IS NOT NULL AND LEN(@DetailsJson) > 2
    BEGIN
        INSERT INTO dbo.GRNDetails (GRNId, PODetailId, ItemId, UOMId, OrderedQty, ReceivedQty, RejectedQty, UnitRate, GSTPercent, Remarks)
        SELECT
            @ActualGRNId,
            NULLIF(CAST(j.poDetailId   AS INT), 0),
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

    COMMIT;
    SELECT @ActualGRNId AS GRNId;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 27. usp_PostGRN
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_PostGRN', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_PostGRN;
GO
CREATE PROCEDURE dbo.usp_PostGRN
    @GRNId  INT,
    @UserId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @BranchId INT, @GodownId INT, @GRNDate DATE;
    SELECT @BranchId = BranchId, @GodownId = GodownId, @GRNDate = GRNDate
    FROM dbo.GRNMaster WHERE GRNId = @GRNId AND Status = 'Draft';

    IF @BranchId IS NULL
    BEGIN
        ROLLBACK;
        RAISERROR('GRN not found or already posted.', 16, 1);
        RETURN;
    END

    DECLARE @GRNNumber NVARCHAR(30);
    SELECT @GRNNumber = GRNNumber FROM dbo.GRNMaster WHERE GRNId = @GRNId;

    -- Process each GRN line
    DECLARE @ItemId INT, @Qty DECIMAL(18,3), @UnitCost DECIMAL(18,4), @AccQty DECIMAL(18,3);
    DECLARE grn_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT ItemId, AcceptedQty, UnitRate FROM dbo.GRNDetails WHERE GRNId = @GRNId AND AcceptedQty > 0;

    OPEN grn_cur;
    FETCH NEXT FROM grn_cur INTO @ItemId, @AccQty, @UnitCost;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        DECLARE @PrevBal DECIMAL(18,3) = 0, @PrevAvg DECIMAL(18,4) = 0;
        SELECT @PrevBal = BalanceQty, @PrevAvg = AverageCost
        FROM dbo.CurrentStock WHERE BranchId=@BranchId AND GodownId=@GodownId AND ItemId=@ItemId;

        DECLARE @NewBal DECIMAL(18,3) = @PrevBal + @AccQty;
        DECLARE @NewAvg DECIMAL(18,4) =
            CASE WHEN @NewBal > 0 THEN (@PrevBal*@PrevAvg + @AccQty*@UnitCost) / @NewBal ELSE @UnitCost END;

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
    WHERE gd.GRNId = @GRNId;

    -- Update PO status
    DECLARE @POId INT; SELECT @POId = POId FROM dbo.GRNMaster WHERE GRNId = @GRNId;
    IF @POId IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.PurchaseOrderDetails WHERE POId=@POId AND PendingQty > 0)
            UPDATE dbo.PurchaseOrder SET Status='Closed', UpdatedAt=SYSUTCDATETIME() WHERE POId=@POId;
        ELSE
            UPDATE dbo.PurchaseOrder SET Status='PartialGRN', UpdatedAt=SYSUTCDATETIME()
            WHERE POId=@POId AND Status='Approved';
    END

    UPDATE dbo.GRNMaster SET Status='Posted', UpdatedAt=SYSUTCDATETIME() WHERE GRNId=@GRNId;

    COMMIT;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 28. usp_GetStockTransferList
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetStockTransferList', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetStockTransferList;
GO
CREATE PROCEDURE dbo.usp_GetStockTransferList
    @BranchId INT,
    @Status   NVARCHAR(20) = NULL,
    @FromDate DATE = NULL,
    @ToDate   DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        st.TransferId, st.TransferNumber, st.BranchId,
        st.FromGodownId, st.ToGodownId, st.TransferDate,
        st.TransferType, st.PriceMode, st.Remarks, st.Status,
        st.TotalQty, st.TotalValue,
        fg.GodownName AS FromGodownName,
        tg.GodownName AS ToGodownName,
        (SELECT COUNT(*) FROM dbo.StockTransferDetails WHERE TransferId = st.TransferId) AS LineCount,
        st.CreatedAt
    FROM dbo.StockTransfer st
    INNER JOIN dbo.Godowns fg ON fg.Id = st.FromGodownId
    INNER JOIN dbo.Godowns tg ON tg.Id = st.ToGodownId
    WHERE st.BranchId = @BranchId
      AND (@Status   IS NULL OR st.Status        = @Status)
      AND (@FromDate IS NULL OR st.TransferDate >= @FromDate)
      AND (@ToDate   IS NULL OR st.TransferDate <= @ToDate)
    ORDER BY st.TransferDate DESC, st.TransferNumber DESC;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 29. usp_GetStockTransferById  (RS1=header, RS2=lines)
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetStockTransferById', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetStockTransferById;
GO
CREATE PROCEDURE dbo.usp_GetStockTransferById
    @TransferId INT
AS
BEGIN
    SET NOCOUNT ON;
    -- RS1: header
    SELECT
        st.TransferId, st.TransferNumber, st.BranchId,
        st.FromGodownId, st.ToGodownId, st.TransferDate,
        st.TransferType, st.PriceMode, st.Remarks, st.Status,
        st.TotalQty, st.TotalValue,
        fg.GodownName AS FromGodownName,
        tg.GodownName AS ToGodownName,
        (SELECT COUNT(*) FROM dbo.StockTransferDetails WHERE TransferId = st.TransferId) AS LineCount,
        st.CreatedAt
    FROM dbo.StockTransfer st
    INNER JOIN dbo.Godowns fg ON fg.Id = st.FromGodownId
    INNER JOIN dbo.Godowns tg ON tg.Id = st.ToGodownId
    WHERE st.TransferId = @TransferId;

    -- RS2: lines
    SELECT
        td.TransferDetailId, td.TransferId, td.ItemId, td.UOMId,
        td.Quantity, td.UnitCost, td.Remarks,
        i.IngredientsName AS ItemName, u.UOMCode, u.UOMName
    FROM dbo.StockTransferDetails td
    INNER JOIN dbo.Ingredients i ON i.Id    = td.ItemId
    INNER JOIN dbo.UomMaster u   ON u.UOMId = td.UOMId
    WHERE td.TransferId = @TransferId
    ORDER BY td.TransferDetailId;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 30. usp_SaveStockTransfer
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_SaveStockTransfer', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_SaveStockTransfer;
GO
CREATE PROCEDURE dbo.usp_SaveStockTransfer
    @TransferId    INT,
    @BranchId      INT,
    @FromGodownId  INT,
    @ToGodownId    INT,
    @TransferDate  DATE,
    @TransferType  NVARCHAR(20) = 'Internal',
    @PriceMode     NVARCHAR(20) = 'AverageCost',
    @Remarks       NVARCHAR(500) = NULL,
    @UserId        INT = NULL,
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
            RIGHT('0000' + CAST(ISNULL((SELECT COUNT(*)+1 FROM dbo.StockTransfer WHERE BranchId=@BranchId),1) AS NVARCHAR),4);

        INSERT INTO dbo.StockTransfer
            (TransferNumber, BranchId, FromGodownId, ToGodownId, TransferDate,
             TransferType, PriceMode, Status, Remarks, TotalQty, TotalValue, CreatedAt, CreatedBy)
        VALUES
            (@TransferNumber, @BranchId, @FromGodownId, @ToGodownId, @TransferDate,
             @TransferType, @PriceMode, 'Draft', @Remarks, 0, 0, SYSUTCDATETIME(), @UserId);

        SET @ActualId = SCOPE_IDENTITY();
    END

    IF @DetailsJson IS NOT NULL AND LEN(@DetailsJson) > 2
    BEGIN
        INSERT INTO dbo.StockTransferDetails (TransferId, ItemId, UOMId, Quantity, UnitCost, Remarks)
        SELECT
            @ActualId,
            CAST(j.itemId    AS INT),
            CAST(j.uomId     AS INT),
            CAST(j.quantity  AS DECIMAL(18,3)),
            CAST(j.unitCost  AS DECIMAL(18,4)),
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

        -- Update totals
        UPDATE dbo.StockTransfer SET
            TotalQty   = (SELECT ISNULL(SUM(Quantity),0) FROM dbo.StockTransferDetails WHERE TransferId=@ActualId),
            TotalValue = (SELECT ISNULL(SUM(Quantity*UnitCost),0) FROM dbo.StockTransferDetails WHERE TransferId=@ActualId)
        WHERE TransferId = @ActualId;
    END

    COMMIT;
    SELECT @ActualId AS TransferId;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 31. usp_PostStockTransfer
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_PostStockTransfer', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_PostStockTransfer;
GO
CREATE PROCEDURE dbo.usp_PostStockTransfer
    @TransferId INT,
    @UserId     INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @BranchId INT, @FromGodownId INT, @ToGodownId INT,
            @TxDate DATE, @PriceMode NVARCHAR(20), @TransferNumber NVARCHAR(30);

    SELECT @BranchId=BranchId, @FromGodownId=FromGodownId, @ToGodownId=ToGodownId,
           @TxDate=TransferDate, @PriceMode=PriceMode, @TransferNumber=TransferNumber
    FROM dbo.StockTransfer WHERE TransferId=@TransferId AND Status='Draft';

    IF @BranchId IS NULL
    BEGIN
        ROLLBACK;
        RAISERROR('Transfer not found or already posted.', 16, 1);
        RETURN;
    END

    DECLARE @ItemId INT, @Qty DECIMAL(18,3), @UnitCost DECIMAL(18,4);
    DECLARE tr_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT ItemId, Quantity, UnitCost FROM dbo.StockTransferDetails WHERE TransferId=@TransferId;

    OPEN tr_cur;
    FETCH NEXT FROM tr_cur INTO @ItemId, @Qty, @UnitCost;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Use average cost if PriceMode = AverageCost
        IF @PriceMode = 'AverageCost'
        BEGIN
            SELECT @UnitCost = ISNULL(AverageCost, @UnitCost)
            FROM dbo.CurrentStock WHERE BranchId=@BranchId AND GodownId=@FromGodownId AND ItemId=@ItemId;
        END

        -- OUT from source godown
        DECLARE @FromBal DECIMAL(18,3) = 0, @FromAvg DECIMAL(18,4) = 0;
        SELECT @FromBal=BalanceQty, @FromAvg=AverageCost
        FROM dbo.CurrentStock WHERE BranchId=@BranchId AND GodownId=@FromGodownId AND ItemId=@ItemId;
        DECLARE @NewFromBal DECIMAL(18,3) = @FromBal - @Qty;

        IF EXISTS (SELECT 1 FROM dbo.CurrentStock WHERE BranchId=@BranchId AND GodownId=@FromGodownId AND ItemId=@ItemId)
            UPDATE dbo.CurrentStock SET BalanceQty=@NewFromBal, LastUpdated=SYSUTCDATETIME()
            WHERE BranchId=@BranchId AND GodownId=@FromGodownId AND ItemId=@ItemId;

        INSERT INTO dbo.StockLedger
            (BranchId,GodownId,ItemId,TransactionDate,TransactionType,ReferenceType,ReferenceId,ReferenceNumber,
             InQuantity,OutQuantity,UnitCost,BalanceQty,BalanceValue,AverageCost,CreatedAt,CreatedBy)
        VALUES
            (@BranchId,@FromGodownId,@ItemId,@TxDate,'TRANSFER_OUT','TRANSFER',@TransferId,@TransferNumber,
             0,@Qty,@UnitCost,@NewFromBal,@NewFromBal*@FromAvg,@FromAvg,SYSUTCDATETIME(),@UserId);

        -- IN to destination godown
        DECLARE @ToBal DECIMAL(18,3) = 0, @ToAvg DECIMAL(18,4) = 0;
        SELECT @ToBal=BalanceQty, @ToAvg=AverageCost
        FROM dbo.CurrentStock WHERE BranchId=@BranchId AND GodownId=@ToGodownId AND ItemId=@ItemId;
        DECLARE @NewToBal DECIMAL(18,3) = @ToBal + @Qty;
        DECLARE @NewToAvg DECIMAL(18,4) =
            CASE WHEN @NewToBal>0 THEN (@ToBal*@ToAvg + @Qty*@UnitCost)/@NewToBal ELSE @UnitCost END;

        IF EXISTS (SELECT 1 FROM dbo.CurrentStock WHERE BranchId=@BranchId AND GodownId=@ToGodownId AND ItemId=@ItemId)
            UPDATE dbo.CurrentStock SET BalanceQty=@NewToBal, AverageCost=@NewToAvg, LastUpdated=SYSUTCDATETIME()
            WHERE BranchId=@BranchId AND GodownId=@ToGodownId AND ItemId=@ItemId;
        ELSE
            INSERT INTO dbo.CurrentStock (BranchId,GodownId,ItemId,BalanceQty,AverageCost,LastUpdated)
            VALUES (@BranchId,@ToGodownId,@ItemId,@NewToBal,@NewToAvg,SYSUTCDATETIME());

        INSERT INTO dbo.StockLedger
            (BranchId,GodownId,ItemId,TransactionDate,TransactionType,ReferenceType,ReferenceId,ReferenceNumber,
             InQuantity,OutQuantity,UnitCost,BalanceQty,BalanceValue,AverageCost,CreatedAt,CreatedBy)
        VALUES
            (@BranchId,@ToGodownId,@ItemId,@TxDate,'TRANSFER_IN','TRANSFER',@TransferId,@TransferNumber,
             @Qty,0,@UnitCost,@NewToBal,@NewToBal*@NewToAvg,@NewToAvg,SYSUTCDATETIME(),@UserId);

        FETCH NEXT FROM tr_cur INTO @ItemId, @Qty, @UnitCost;
    END
    CLOSE tr_cur; DEALLOCATE tr_cur;

    UPDATE dbo.StockTransfer SET Status='Posted', UpdatedAt=SYSUTCDATETIME() WHERE TransferId=@TransferId;

    COMMIT;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 32. usp_GetDamageEntryList
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetDamageEntryList', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetDamageEntryList;
GO
CREATE PROCEDURE dbo.usp_GetDamageEntryList
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
    WHERE de.BranchId = @BranchId
      AND (@Status   IS NULL OR de.Status     = @Status)
      AND (@FromDate IS NULL OR de.DamageDate >= @FromDate)
      AND (@ToDate   IS NULL OR de.DamageDate <= @ToDate)
    ORDER BY de.DamageDate DESC, de.DamageNumber DESC;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 33. usp_GetDamageEntryById  (RS1=header, RS2=lines)
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_GetDamageEntryById', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetDamageEntryById;
GO
CREATE PROCEDURE dbo.usp_GetDamageEntryById
    @DamageId INT
AS
BEGIN
    SET NOCOUNT ON;
    -- RS1
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
    INNER JOIN dbo.UomMaster u   ON u.UOMId = dd.UOMId
    WHERE dd.DamageId = @DamageId
    ORDER BY dd.DamageDetailId;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 34. usp_SaveDamageEntry
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_SaveDamageEntry', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_SaveDamageEntry;
GO
CREATE PROCEDURE dbo.usp_SaveDamageEntry
    @DamageId    INT,
    @BranchId    INT,
    @GodownId    INT,
    @DamageDate  DATE,
    @DamageType  NVARCHAR(20) = 'Damage',
    @Remarks     NVARCHAR(500) = NULL,
    @UserId      INT = NULL,
    @DetailsJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @ActualId INT = @DamageId;

    IF @DamageId > 0
    BEGIN
        UPDATE dbo.DamageEntry SET
            GodownId   = @GodownId,
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
            'DMG-' + RIGHT('000' + CAST(@BranchId AS NVARCHAR), 3) + '-' +
            FORMAT(GETDATE(), 'yyyyMMdd') + '-' +
            RIGHT('0000' + CAST(ISNULL((SELECT COUNT(*)+1 FROM dbo.DamageEntry WHERE BranchId=@BranchId),1) AS NVARCHAR),4);

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

        UPDATE dbo.DamageEntry SET
            TotalQty   = (SELECT ISNULL(SUM(Quantity),0) FROM dbo.DamageEntryDetails WHERE DamageId=@ActualId),
            TotalValue = (SELECT ISNULL(SUM(Quantity*UnitCost),0) FROM dbo.DamageEntryDetails WHERE DamageId=@ActualId)
        WHERE DamageId = @ActualId;
    END

    COMMIT;
    SELECT @ActualId AS DamageId;
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- 35. usp_PostDamageEntry
-- ═══════════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.usp_PostDamageEntry', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_PostDamageEntry;
GO
CREATE PROCEDURE dbo.usp_PostDamageEntry
    @DamageId INT,
    @UserId   INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @BranchId INT, @GodownId INT, @DamageDate DATE, @DamageNumber NVARCHAR(30);
    SELECT @BranchId=BranchId, @GodownId=GodownId, @DamageDate=DamageDate, @DamageNumber=DamageNumber
    FROM dbo.DamageEntry WHERE DamageId=@DamageId AND Status='Draft';

    IF @BranchId IS NULL
    BEGIN
        ROLLBACK;
        RAISERROR('Damage entry not found or already posted.', 16, 1);
        RETURN;
    END

    DECLARE @ItemId INT, @Qty DECIMAL(18,3), @UnitCost DECIMAL(18,4);
    DECLARE dmg_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT ItemId, Quantity, UnitCost FROM dbo.DamageEntryDetails WHERE DamageId=@DamageId;

    OPEN dmg_cur;
    FETCH NEXT FROM dmg_cur INTO @ItemId, @Qty, @UnitCost;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        DECLARE @CurBal DECIMAL(18,3) = 0, @CurAvg DECIMAL(18,4) = 0;
        SELECT @CurBal=BalanceQty, @CurAvg=AverageCost
        FROM dbo.CurrentStock WHERE BranchId=@BranchId AND GodownId=@GodownId AND ItemId=@ItemId;

        -- Use average cost for damage valuation
        SET @UnitCost = ISNULL(NULLIF(@UnitCost, 0), @CurAvg);
        DECLARE @NewBal2 DECIMAL(18,3) = @CurBal - @Qty;

        IF EXISTS (SELECT 1 FROM dbo.CurrentStock WHERE BranchId=@BranchId AND GodownId=@GodownId AND ItemId=@ItemId)
            UPDATE dbo.CurrentStock SET BalanceQty=@NewBal2, LastUpdated=SYSUTCDATETIME()
            WHERE BranchId=@BranchId AND GodownId=@GodownId AND ItemId=@ItemId;

        INSERT INTO dbo.StockLedger
            (BranchId,GodownId,ItemId,TransactionDate,TransactionType,ReferenceType,ReferenceId,ReferenceNumber,
             InQuantity,OutQuantity,UnitCost,BalanceQty,BalanceValue,AverageCost,CreatedAt,CreatedBy)
        VALUES
            (@BranchId,@GodownId,@ItemId,@DamageDate,'DAMAGE','DAMAGE',@DamageId,@DamageNumber,
             0,@Qty,@UnitCost,@NewBal2,@NewBal2*@CurAvg,@CurAvg,SYSUTCDATETIME(),@UserId);

        FETCH NEXT FROM dmg_cur INTO @ItemId, @Qty, @UnitCost;
    END
    CLOSE dmg_cur; DEALLOCATE dmg_cur;

    UPDATE dbo.DamageEntry SET Status='Posted', UpdatedAt=SYSUTCDATETIME() WHERE DamageId=@DamageId;

    COMMIT;
END
GO

PRINT '=== All 35 inventory stored procedures created successfully. ===';
GO
