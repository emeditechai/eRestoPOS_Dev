-- Fix: usp_GetStockLedger was joining UomMaster on BaseUOMId which does not exist.
-- Corrected to use i.PurchaseUOMId so that UOMCode is properly returned.
DECLARE @def NVARCHAR(MAX);
SELECT @def = m.definition
FROM sys.sql_modules m
JOIN sys.objects o ON o.object_id = m.object_id
WHERE o.name = 'usp_GetStockLedger';

SET @def = REPLACE(@def,
    '(SELECT TOP 1 BaseUOMId FROM dbo.Ingredients WHERE Id = sl.ItemId)',
    'i.PurchaseUOMId');
SET @def = REPLACE(@def, 'CREATE PROCEDURE', 'ALTER PROCEDURE');
EXEC sp_executesql @def;
PRINT 'usp_GetStockLedger UOMCode fix applied.';
