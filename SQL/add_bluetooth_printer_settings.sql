/*
    Create BluetoothPrinterSettings table
    - Stores one default BLE printer per user per branch
    - Used by the Printer Setup page and printer-manager.js for silent reconnect
    - DeviceId is the Chrome Web Bluetooth device.id (opaque string, browser-scoped)
*/

SET XACT_ABORT ON;
GO

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'BluetoothPrinterSettings'
)
BEGIN
    CREATE TABLE dbo.BluetoothPrinterSettings
    (
        Id                  INT             IDENTITY(1,1)   NOT NULL,
        UserId              INT             NOT NULL,
        BranchId            INT             NOT NULL,
        PrinterName         NVARCHAR(100)   NOT NULL,
        PrinterDeviceId     NVARCHAR(500)   NOT NULL DEFAULT '',
        ServiceUUID         NVARCHAR(100)   NOT NULL DEFAULT '',
        CharacteristicUUID  NVARCHAR(100)   NOT NULL DEFAULT '',
        SavedAt             DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_BluetoothPrinterSettings PRIMARY KEY (Id),
        CONSTRAINT UQ_BluetoothPrinter_UserBranch UNIQUE (UserId, BranchId)
    );

    PRINT 'Table dbo.BluetoothPrinterSettings created successfully.';
END
ELSE
BEGIN
    PRINT 'Table dbo.BluetoothPrinterSettings already exists — skipped.';
END
GO
