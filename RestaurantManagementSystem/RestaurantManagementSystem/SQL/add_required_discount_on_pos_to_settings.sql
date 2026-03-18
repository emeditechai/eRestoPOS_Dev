-- Adds IsRequiredDiscountOnPOS column to dbo.RestaurantSettings
-- Run once against the target database before deploying the corresponding application version.

IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo'
      AND TABLE_NAME   = 'RestaurantSettings'
      AND COLUMN_NAME  = 'IsRequiredDiscountOnPOS'
)
BEGIN
    ALTER TABLE [dbo].[RestaurantSettings]
        ADD [IsRequiredDiscountOnPOS] BIT NOT NULL DEFAULT 0;

    PRINT 'Column IsRequiredDiscountOnPOS added to dbo.RestaurantSettings.';
END
ELSE
BEGIN
    PRINT 'Column IsRequiredDiscountOnPOS already exists – no changes made.';
END
