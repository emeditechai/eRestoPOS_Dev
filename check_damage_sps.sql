USE [dev_Restaurant];
GO
SELECT o.name, m.uses_quoted_identifier
FROM sys.sql_modules m
JOIN sys.objects o ON o.object_id = m.object_id
WHERE o.name LIKE '%Damage%'
ORDER BY o.name;
GO
EXEC sp_helptext 'usp_SaveDamageEntry';
GO
