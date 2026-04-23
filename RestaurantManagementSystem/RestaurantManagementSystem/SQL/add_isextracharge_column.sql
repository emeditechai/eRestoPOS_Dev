-- Adds IsExtraCharge column to MenuItems if it does not already exist
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MenuItems' AND COLUMN_NAME = 'IsExtraCharge')
BEGIN
    ALTER TABLE MenuItems ADD IsExtraCharge BIT NOT NULL DEFAULT 0;
    PRINT 'Added IsExtraCharge column to MenuItems.';
END
ELSE
BEGIN
    PRINT 'IsExtraCharge column already exists.';
END
