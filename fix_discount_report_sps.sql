SET NOCOUNT ON;

-- Fix 1: usp_GetGSTBreakupReport
DECLARE @g NVARCHAR(MAX);
SELECT @g = definition FROM sys.sql_modules m JOIN sys.objects o ON o.object_id=m.object_id WHERE o.name='usp_GetGSTBreakupReport';
SET @g = REPLACE(@g, 'SUM(p.Amount_ExclGST) - SUM(ISNULL(p.DiscAmount,0))', 'CASE WHEN SUM(ISNULL(p.DiscAmount,0))>0 THEN SUM(p.Amount_ExclGST) ELSE SUM(p.Amount_ExclGST)-MAX(ISNULL(o.DiscountAmount,0)) END');
SET @g = REPLACE(@g, 'SUM(ISNULL(p.DiscAmount,0)) AS DiscountAmount,', 'CASE WHEN SUM(ISNULL(p.DiscAmount,0))>0 THEN SUM(ISNULL(p.DiscAmount,0)) ELSE MAX(ISNULL(o.DiscountAmount,0)) END AS DiscountAmount,');
SET @g = REPLACE(@g, 'CREATE PROCEDURE', 'ALTER PROCEDURE');
EXEC sp_executesql @g;
PRINT 'GST Breakup done';
GO

-- Fix 2: usp_GetCollectionRegister
DECLARE @c NVARCHAR(MAX);
SELECT @c = definition FROM sys.sql_modules m JOIN sys.objects o ON o.object_id=m.object_id WHERE o.name='usp_GetCollectionRegister';
SET @c = REPLACE(@c, 'ISNULL(p.DiscAmount, 0)', '(CASE WHEN ISNULL(p.DiscAmount,0)>0 THEN p.DiscAmount ELSE ISNULL(o.DiscountAmount,0) END)');
SET @c = REPLACE(@c, 'CASE WHEN p.DiscAmount > 0 THEN', 'CASE WHEN COALESCE(NULLIF(p.DiscAmount,0),o.DiscountAmount,0) > 0 THEN');
SET @c = REPLACE(@c, 'CAST(p.DiscAmount AS VARCHAR(20))', 'CAST(COALESCE(NULLIF(p.DiscAmount,0),o.DiscountAmount,0) AS VARCHAR(20))');
SET @c = REPLACE(@c, 'CREATE PROCEDURE', 'ALTER PROCEDURE');
EXEC sp_executesql @c;
PRINT 'Collection Register done';
GO
