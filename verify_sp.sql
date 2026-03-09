USE [dev_Restaurant];
GO
SELECT o.name AS SP_Name, o.create_date, o.modify_date, m.uses_quoted_identifier
FROM sys.sql_modules m
JOIN sys.objects o ON o.object_id = m.object_id
WHERE o.name = 'usp_PostStockTransfer';
GO
