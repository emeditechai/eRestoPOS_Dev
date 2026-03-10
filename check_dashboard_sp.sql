USE [dev_Restaurant];
GO
-- Show live SP definition
EXEC sp_helptext 'usp_GetInventoryDashboardStats';
GO
-- Show what TransactionTypes are actually in StockLedger
SELECT TransactionType, COUNT(*) AS Cnt, SUM(OutQuantity) AS TotalOut
FROM dbo.StockLedger
GROUP BY TransactionType
ORDER BY TransactionType;
GO
