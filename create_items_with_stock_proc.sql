USE [dev_Restaurant];
GO

-- Returns items that have BalanceQty > 0 in the specified godown,
-- joined with ingredient name, default purchase UOM and average cost.
CREATE OR ALTER PROCEDURE dbo.usp_GetItemsWithStockByGodown
    @GodownId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        cs.ItemId,
        i.IngredientsName                     AS ItemName,
        i.Code                                AS ItemCode,
        cs.BalanceQty,
        cs.AverageCost,
        ISNULL(u.UOMId,   0)                  AS UOMId,
        ISNULL(u.UOMCode, '')                 AS UOMCode,
        ISNULL(u.UOMName, '')                 AS UOMName
    FROM  dbo.CurrentStock cs
    JOIN  dbo.Ingredients   i  ON i.Id    = cs.ItemId
    LEFT JOIN dbo.UomMaster u  ON u.UOMId = i.PurchaseUOMId
    WHERE cs.GodownId  = @GodownId
      AND cs.BalanceQty > 0
      AND i.IsActive   = 1
    ORDER BY i.IngredientsName;
END;
GO
