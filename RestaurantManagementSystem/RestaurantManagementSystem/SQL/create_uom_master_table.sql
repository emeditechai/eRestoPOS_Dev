-- =============================================
-- Script: Create UOM Master Table
-- Description: Creates tbl_mst_uom table for Unit of Measurement master data
-- Author: Restaurant Management System
-- Date: November 2025
-- =============================================

USE [dev_Restaurant]
GO

-- Create UOM Master Table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[tbl_mst_uom]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[tbl_mst_uom] (
        [UOM_Id] INT IDENTITY(1,1) NOT NULL,
        [UOM_Name] NVARCHAR(50) NOT NULL,
        [UOM_Type] NVARCHAR(50) NOT NULL,
        [Base_Quantity_ML] DECIMAL(10,2) NOT NULL,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CreatedBy] INT NULL,
        [CreatedDate] DATETIME NOT NULL DEFAULT GETDATE(),
        [UpdatedDate] DATETIME NULL,
        [UpdatedBy] INT NULL,
        
        CONSTRAINT [PK_tbl_mst_uom] PRIMARY KEY CLUSTERED ([UOM_Id] ASC),
        CONSTRAINT [UQ_tbl_mst_uom_Name] UNIQUE ([UOM_Name]),
        CONSTRAINT [CHK_tbl_mst_uom_BaseQuantity] CHECK ([Base_Quantity_ML] > 0),
        CONSTRAINT [CHK_tbl_mst_uom_Type] CHECK ([UOM_Type] IN ('Peg', 'Bottle', 'Beer', 'Wine', 'Cocktail', 'SoftDrink', 'Food', 'Other'))
    );
    
    PRINT 'tbl_mst_uom table created successfully!';
END
ELSE
BEGIN
    PRINT 'tbl_mst_uom table already exists.';
END
GO
