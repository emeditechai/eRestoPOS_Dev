-- =============================================
-- Script: Insert Sample UOM Data
-- Description: Inserts common UOM records for beverages and food
-- =============================================

USE [dev_Restaurant]
GO

-- Check if data already exists
IF (SELECT COUNT(*) FROM [dbo].[tbl_mst_uom]) = 0
BEGIN
    PRINT 'Inserting sample UOM data...';
    
    INSERT INTO [dbo].[tbl_mst_uom] ([UOM_Name], [UOM_Type], [Base_Quantity_ML], [IsActive], [CreatedBy], [CreatedDate])
    VALUES
        -- Spirits/Liquor Pegs
        ('30 ml', 'Peg', 30.00, 1, 1, GETDATE()),
        ('60 ml', 'Peg', 60.00, 1, 1, GETDATE()),
        ('90 ml', 'Peg', 90.00, 1, 1, GETDATE()),
        
        -- Standard Bottles
        ('180 ml', 'Bottle', 180.00, 1, 1, GETDATE()),
        ('375 ml', 'Bottle', 375.00, 1, 1, GETDATE()),
        ('750 ml', 'Bottle', 750.00, 1, 1, GETDATE()),
        
        -- Beer
        ('Pint', 'Beer', 330.00, 1, 1, GETDATE()),
        ('Pitcher', 'Beer', 1500.00, 1, 1, GETDATE()),
        
        -- Wine
        ('Glass', 'Wine', 150.00, 1, 1, GETDATE()),
        ('Bottle', 'Wine', 750.00, 1, 1, GETDATE());
    
    PRINT 'Sample UOM data inserted successfully!';
    PRINT 'Total records inserted: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
END
ELSE
BEGIN
    PRINT 'UOM data already exists. Skipping insert.';
    PRINT 'Current record count: ' + CAST((SELECT COUNT(*) FROM [dbo].[tbl_mst_uom]) AS NVARCHAR(10));
END
GO

-- Display inserted data
SELECT 
    UOM_Id,
    UOM_Name,
    UOM_Type,
    Base_Quantity_ML,
    IsActive
FROM [dbo].[tbl_mst_uom]
ORDER BY UOM_Type, UOM_Id;
GO
