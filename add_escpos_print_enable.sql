-- Migration: Add IsESCPOSPrintEnabled column to RestaurantSettings
-- Run this on any existing database (dev or production)

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo'
      AND TABLE_NAME   = 'RestaurantSettings'
      AND COLUMN_NAME  = 'IsESCPOSPrintEnabled'
)
BEGIN
    ALTER TABLE [dbo].[RestaurantSettings]
        ADD [IsESCPOSPrintEnabled] BIT NOT NULL DEFAULT 0;
    PRINT 'Column IsESCPOSPrintEnabled added successfully.';
END
ELSE
BEGIN
    PRINT 'Column IsESCPOSPrintEnabled already exists – skipped.';
END
