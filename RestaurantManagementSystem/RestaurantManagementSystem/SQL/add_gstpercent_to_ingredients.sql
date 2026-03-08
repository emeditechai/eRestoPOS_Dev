-- =============================================
-- Migration : add_gstpercent_to_ingredients.sql
-- Purpose   : Add GSTPercent column to dbo.Ingredients
--             CGST = GSTPercent / 2  (intra-state)
--             SGST = GSTPercent / 2  (intra-state)
--             IGST = GSTPercent      (inter-state)
-- Database  : dev_Restaurant
-- Safe      : Idempotent — uses COL_LENGTH guard; re-runnable
-- =============================================

USE [dev_Restaurant];
GO

PRINT '=== Adding GSTPercent column to dbo.Ingredients ===';

IF COL_LENGTH('dbo.Ingredients', 'GSTPercent') IS NULL
BEGIN
    ALTER TABLE dbo.Ingredients
        ADD GSTPercent DECIMAL(5,2) NOT NULL DEFAULT 0;

    PRINT '  GSTPercent column added (DECIMAL(5,2), default 0).';
END
ELSE
BEGIN
    PRINT '  GSTPercent column already exists — skipped.';
END
GO

PRINT '=== Migration complete. ===';
GO
