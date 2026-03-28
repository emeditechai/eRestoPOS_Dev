-- Adds IsTableMarkedAvailableAfterBillCompletion column to dbo.RestaurantSettings if it does not exist

IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo'
      AND TABLE_NAME = 'RestaurantSettings'
      AND COLUMN_NAME = 'IsTableMarkedAvailableAfterBillCompletion'
)
BEGIN
    ALTER TABLE [dbo].[RestaurantSettings]
    ADD [IsTableMarkedAvailableAfterBillCompletion] BIT NOT NULL
        CONSTRAINT [DF_RestaurantSettings_IsTableMarkedAvailableAfterBillCompletion] DEFAULT (0);

    PRINT 'Column IsTableMarkedAvailableAfterBillCompletion added to dbo.RestaurantSettings.';
END
ELSE
BEGIN
    PRINT 'Column IsTableMarkedAvailableAfterBillCompletion already exists in dbo.RestaurantSettings.';
END

IF EXISTS (SELECT 1 FROM [dbo].[RestaurantSettings])
BEGIN
    EXEC(N'
        UPDATE [dbo].[RestaurantSettings]
        SET [IsTableMarkedAvailableAfterBillCompletion] = ISNULL([IsTableMarkedAvailableAfterBillCompletion], 0);
    ');

    PRINT 'Existing RestaurantSettings rows normalized for IsTableMarkedAvailableAfterBillCompletion.';
END
GO
