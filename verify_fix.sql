USE [dev_Restaurant];
GO
SELECT o.name AS SP_Name, o.create_date, o.modify_date, m.uses_quoted_identifier
FROM   sys.sql_modules m
JOIN   sys.objects     o ON o.object_id = m.object_id
WHERE  o.name IN ('usp_PostStockTransfer','usp_GetCurrentStockSummary','usp_GetGodownsWithStock');
GO
PRINT '=== Egg stock after fix ===';
SELECT cs.StockId, cs.BranchId, b.BranchName, g.GodownName,
       i.IngredientsName, cs.BalanceQty, cs.AverageCost
FROM   dbo.CurrentStock cs
JOIN   dbo.Godowns     g ON g.Id       = cs.GodownId
JOIN   dbo.Branches    b ON b.BranchId = g.BranchId
JOIN   dbo.Ingredients i ON i.Id       = cs.ItemId
WHERE  i.IngredientsName = 'Egg';
GO
