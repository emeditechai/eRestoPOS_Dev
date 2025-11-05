-- =============================================
-- Script: Add UOM_Id to MenuItems
-- Description: Adds UOM_Id column and foreign key to MenuItems table
-- =============================================

USE [dev_Restaurant]
GO

-- Step 1: Add UOM_Id column if it doesn't exist
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo' 
    AND TABLE_NAME = 'MenuItems' 
    AND COLUMN_NAME = 'UOM_Id'
)
BEGIN
    ALTER TABLE [dbo].[MenuItems] 
    ADD [UOM_Id] INT NULL;
    
    PRINT 'UOM_Id column added to MenuItems table successfully.';
END
ELSE
BEGIN
    PRINT 'UOM_Id column already exists in MenuItems table.';
END
GO

-- Step 2: Add foreign key constraint
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys 
    WHERE name = 'FK_MenuItems_UOM'
)
BEGIN
    -- Only add FK if tbl_mst_uom table exists
    IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[tbl_mst_uom]') AND type in (N'U'))
    BEGIN
        ALTER TABLE [dbo].[MenuItems]
        ADD CONSTRAINT [FK_MenuItems_UOM]
        FOREIGN KEY ([UOM_Id])
        REFERENCES [dbo].[tbl_mst_uom]([UOM_Id])
        ON DELETE SET NULL;  -- If UOM is deleted, set MenuItems.UOM_Id to NULL
        
        PRINT 'Foreign key constraint FK_MenuItems_UOM added successfully.';
    END
    ELSE
    BEGIN
        PRINT 'Warning: tbl_mst_uom table does not exist. Please create it first using create_uom_master_table.sql';
    END
END
ELSE
BEGIN
    PRINT 'Foreign key constraint FK_MenuItems_UOM already exists.';
END
GO

-- Step 3: Create index for better query performance
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_MenuItems_UOM_Id' 
    AND object_id = OBJECT_ID('[dbo].[MenuItems]')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_MenuItems_UOM_Id]
    ON [dbo].[MenuItems]([UOM_Id])
    INCLUDE ([Name], [Price], [IsAvailable]);
    
    PRINT 'Index IX_MenuItems_UOM_Id created successfully.';
END
ELSE
BEGIN
    PRINT 'Index IX_MenuItems_UOM_Id already exists.';
END
GO

PRINT '';
PRINT 'UOM integration with MenuItems completed successfully!';
PRINT 'You can now assign UOM to menu items, especially for BAR category items.';
GO
