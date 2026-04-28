-- Migration: Add ESCPOS settings columns to RestaurantSettings
-- Safe to run multiple times (IF NOT EXISTS guards)

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'RestaurantSettings' AND COLUMN_NAME = 'ESCPOSMode'
)
BEGIN
    ALTER TABLE [dbo].[RestaurantSettings]
        ADD [ESCPOSMode] NVARCHAR(10) NOT NULL DEFAULT N'BLE';
    PRINT 'Column ESCPOSMode added.';
END
ELSE
    PRINT 'Column ESCPOSMode already exists - skipped.';

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'RestaurantSettings' AND COLUMN_NAME = 'ESCPOSPrinterIP'
)
BEGIN
    ALTER TABLE [dbo].[RestaurantSettings]
        ADD [ESCPOSPrinterIP] NVARCHAR(100) NULL;
    PRINT 'Column ESCPOSPrinterIP added.';
END
ELSE
    PRINT 'Column ESCPOSPrinterIP already exists - skipped.';

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'RestaurantSettings' AND COLUMN_NAME = 'ESCPOSPrinterPort'
)
BEGIN
    ALTER TABLE [dbo].[RestaurantSettings]
        ADD [ESCPOSPrinterPort] INT NOT NULL DEFAULT 9100;
    PRINT 'Column ESCPOSPrinterPort added.';
END
ELSE
    PRINT 'Column ESCPOSPrinterPort already exists - skipped.';

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'RestaurantSettings' AND COLUMN_NAME = 'ESCPOSPaperSize'
)
BEGIN
    ALTER TABLE [dbo].[RestaurantSettings]
        ADD [ESCPOSPaperSize] NVARCHAR(10) NOT NULL DEFAULT N'58mm';
    PRINT 'Column ESCPOSPaperSize added.';
END
ELSE
    PRINT 'Column ESCPOSPaperSize already exists - skipped.';


IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'RestaurantSettings' AND COLUMN_NAME = 'ESCPOSMode'
)
BEGIN
    ALTER TABLE [dbo].[RestaurantSettings]
        ADD [ESCPOSMode] NVARCHAR(10) NOT NULL DEFAULT N'BLE';
    PRINT 'Column ESCPOSMode added.';
END
ELSE
    PRINT 'Column ESCPOSMode already exists - skipped.';

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'RestaurantSettings' AND COLUMN_NAME = 'ESCPOSPrinterIP'
)
BEGIN
    ALTER TABLE [dbo].[RestaurantSettings]
        ADD [ESCPOSPrinterIP] NVARCHAR(100) NULL;
    PRINT 'Column ESCPOSPrinterIP added.';
END
ELSE
    PRINT 'Column ESCPOSPrinterIP already exists - skipped.';

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'RestaurantSettings' AND COLUMN_NAME = 'ESCPOSPrinterPort'
)
BEGIN
    ALTER TABLE [dbo].[RestaurantSettings]
        ADD [ESCPOSPrinterPort] INT NOT NULL DEFAULT 9100;
    PRINT 'Column ESCPOSPrinterPort added.';
END
ELSE
    PRINT 'Column ESCPOSPrinterPort already exists - skipped.';
