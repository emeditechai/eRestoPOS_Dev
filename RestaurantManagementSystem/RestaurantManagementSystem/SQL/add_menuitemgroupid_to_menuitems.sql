-- Add menuitemgroupID column to dbo.MenuItems and create FK to dbo.menuitemgroup(ID) if not exists

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'MenuItems' AND COLUMN_NAME = 'menuitemgroupID'
)
BEGIN
    ALTER TABLE [dbo].[MenuItems] ADD [menuitemgroupID] INT NULL;
END
GO


-- Create foreign key constraint if not exists
IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys fk
    WHERE fk.parent_object_id = OBJECT_ID(N'[dbo].[MenuItems]')
      AND fk.name = N'FK_MenuItems_menuitemgroup'
)
BEGIN
    ALTER TABLE [dbo].[MenuItems]
    WITH NOCHECK ADD CONSTRAINT [FK_MenuItems_menuitemgroup]
    FOREIGN KEY([menuitemgroupID])
    REFERENCES [dbo].[menuitemgroup]([ID]);
END
GO

-- Optional: create index to speed up joins
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'IX_MenuItems_menuitemgroupID' AND object_id = OBJECT_ID(N'[dbo].[MenuItems]')
)
BEGIN
    CREATE INDEX [IX_MenuItems_menuitemgroupID] ON [dbo].[MenuItems]([menuitemgroupID]);
END
GO
