SET NOCOUNT ON;

DECLARE @MainBranchId INT = 1;

DECLARE @Sql NVARCHAR(MAX);

/* =========================
   RestaurantSettings
   ========================= */
IF COL_LENGTH('dbo.RestaurantSettings', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.RestaurantSettings ADD BranchId INT NULL;
END

IF @MainBranchId IS NOT NULL
BEGIN
    SET @Sql = N'UPDATE dbo.RestaurantSettings
                 SET BranchId = @MainBranchId
                 WHERE BranchId IS NULL;';
    EXEC sp_executesql @Sql, N'@MainBranchId INT', @MainBranchId;
END

SET @Sql = N';WITH cte AS
(
    SELECT Id,
           ROW_NUMBER() OVER (PARTITION BY BranchId ORDER BY Id DESC) AS rn
    FROM dbo.RestaurantSettings
    WHERE BranchId IS NOT NULL
)
DELETE FROM cte WHERE rn > 1;';
EXEC sp_executesql @Sql;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.RestaurantSettings')
      AND name = 'IX_RestaurantSettings_BranchId'
)
BEGIN
    SET @Sql = N'CREATE UNIQUE INDEX IX_RestaurantSettings_BranchId
                 ON dbo.RestaurantSettings(BranchId)
                 WHERE BranchId IS NOT NULL;';
    EXEC sp_executesql @Sql;
END

IF OBJECT_ID('dbo.Branches', 'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.foreign_keys
       WHERE name = 'FK_RestaurantSettings_Branches_BranchId'
   )
BEGIN
    SET @Sql = N'ALTER TABLE dbo.RestaurantSettings WITH NOCHECK
                 ADD CONSTRAINT FK_RestaurantSettings_Branches_BranchId
                 FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId);';
    EXEC sp_executesql @Sql;
END

/* ─────────────────────────────────────────────────────────────────────────────
   Copy main-branch RestaurantSettings (BranchId = @MainBranchId) to every
   branch that does not yet have a settings row.
   Safe to re-run – skips branches that already have a row.
   ───────────────────────────────────────────────────────────────────────────── */
IF OBJECT_ID('dbo.Branches', 'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.RestaurantSettings (
        BranchId,
        RestaurantName,
        StreetAddress,
        PhoneNumber,
        Email,
        LogoPath,
        DefaultGSTPercentage,
        CurrencySymbol,
        IsActive,
        City,
        [State],
        Pincode,
        Country,
        GSTCode,
        Website,
        TakeAwayGSTPercentage,
        IsDefaultGSTRequired,
        BillFormat,
        IsTakeAwayGSTRequired,
        IsDiscountApprovalRequired,
        IsCardPaymentApprovalRequired,
        Is_TakeawayIncludedGST_Req,
        FssaiNo,
        IsKOTBillPrintRequired,
        BarGSTPerc,
        IsReqTableAvailableAfterpayment,
        isReqAutoSentbillEmail,
        SelectedOrderType,
        IsCounterRequired,
        IsSaleFromInventory,
        IsRequiredDiscountOnPOS,
        CreatedAt,
        UpdatedAt
    )
    SELECT
        b.BranchId,                   -- new branch
        src.RestaurantName,
        src.StreetAddress,
        src.PhoneNumber,
        src.Email,
        src.LogoPath,
        src.DefaultGSTPercentage,
        src.CurrencySymbol,
        src.IsActive,
        src.City,
        src.[State],
        src.Pincode,
        src.Country,
        src.GSTCode,
        src.Website,
        src.TakeAwayGSTPercentage,
        src.IsDefaultGSTRequired,
        src.BillFormat,
        src.IsTakeAwayGSTRequired,
        src.IsDiscountApprovalRequired,
        src.IsCardPaymentApprovalRequired,
        src.Is_TakeawayIncludedGST_Req,
        src.FssaiNo,
        src.IsKOTBillPrintRequired,
        src.BarGSTPerc,
        src.IsReqTableAvailableAfterpayment,
        src.isReqAutoSentbillEmail,
        src.SelectedOrderType,
        src.IsCounterRequired,
        src.IsSaleFromInventory,
        src.IsRequiredDiscountOnPOS,
        GETDATE(),
        GETDATE()
    FROM dbo.Branches b
    CROSS JOIN (
        -- Source: main-branch settings row
        SELECT TOP 1
            RestaurantName, StreetAddress, PhoneNumber, Email, LogoPath,
            DefaultGSTPercentage, CurrencySymbol, IsActive, City, [State],
            Pincode, Country, GSTCode, Website, TakeAwayGSTPercentage,
            IsDefaultGSTRequired, BillFormat, IsTakeAwayGSTRequired,
            IsDiscountApprovalRequired, IsCardPaymentApprovalRequired,
            Is_TakeawayIncludedGST_Req, FssaiNo, IsKOTBillPrintRequired,
            BarGSTPerc,
            ISNULL(IsReqTableAvailableAfterpayment, 0) AS IsReqTableAvailableAfterpayment,
            isReqAutoSentbillEmail, SelectedOrderType, IsCounterRequired,
            ISNULL(IsSaleFromInventory, 0)        AS IsSaleFromInventory,
            ISNULL(IsRequiredDiscountOnPOS, 0)    AS IsRequiredDiscountOnPOS
        FROM dbo.RestaurantSettings
        WHERE BranchId = @MainBranchId
    ) AS src
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.RestaurantSettings rs
        WHERE rs.BranchId = b.BranchId
    );

    PRINT 'RestaurantSettings rows inserted for branches that had none.';
END

/* =========================
   tbl_MailConfiguration
   ========================= */
IF COL_LENGTH('dbo.tbl_MailConfiguration', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.tbl_MailConfiguration ADD BranchId INT NULL;
END

IF @MainBranchId IS NOT NULL
BEGIN
    SET @Sql = N'UPDATE dbo.tbl_MailConfiguration
                 SET BranchId = @MainBranchId
                 WHERE BranchId IS NULL;';
    EXEC sp_executesql @Sql, N'@MainBranchId INT', @MainBranchId;
END

SET @Sql = N';WITH cte AS
(
    SELECT Id,
           ROW_NUMBER() OVER (PARTITION BY BranchId ORDER BY Id DESC) AS rn
    FROM dbo.tbl_MailConfiguration
    WHERE BranchId IS NOT NULL
)
DELETE FROM cte WHERE rn > 1;';
EXEC sp_executesql @Sql;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.tbl_MailConfiguration')
      AND name = 'IX_tbl_MailConfiguration_BranchId'
)
BEGIN
    SET @Sql = N'CREATE UNIQUE INDEX IX_tbl_MailConfiguration_BranchId
                 ON dbo.tbl_MailConfiguration(BranchId)
                 WHERE BranchId IS NOT NULL;';
    EXEC sp_executesql @Sql;
END

IF OBJECT_ID('dbo.Branches', 'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.foreign_keys
       WHERE name = 'FK_tbl_MailConfiguration_Branches_BranchId'
   )
BEGIN
    SET @Sql = N'ALTER TABLE dbo.tbl_MailConfiguration WITH NOCHECK
                 ADD CONSTRAINT FK_tbl_MailConfiguration_Branches_BranchId
                 FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId);';
    EXEC sp_executesql @Sql;
END

/* =========================
   UPISettings
   ========================= */
IF COL_LENGTH('dbo.UPISettings', 'BranchId') IS NULL
BEGIN
    ALTER TABLE dbo.UPISettings ADD BranchId INT NULL;
END

IF @MainBranchId IS NOT NULL
BEGIN
    SET @Sql = N'UPDATE dbo.UPISettings
                 SET BranchId = @MainBranchId
                 WHERE BranchId IS NULL;';
    EXEC sp_executesql @Sql, N'@MainBranchId INT', @MainBranchId;
END

SET @Sql = N';WITH cte AS
(
    SELECT Id,
           ROW_NUMBER() OVER (PARTITION BY BranchId ORDER BY Id DESC) AS rn
    FROM dbo.UPISettings
    WHERE BranchId IS NOT NULL
)
DELETE FROM cte WHERE rn > 1;';
EXEC sp_executesql @Sql;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.UPISettings')
      AND name = 'IX_UPISettings_BranchId'
)
BEGIN
    SET @Sql = N'CREATE UNIQUE INDEX IX_UPISettings_BranchId
                 ON dbo.UPISettings(BranchId)
                 WHERE BranchId IS NOT NULL;';
    EXEC sp_executesql @Sql;
END

IF OBJECT_ID('dbo.Branches', 'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.foreign_keys
       WHERE name = 'FK_UPISettings_Branches_BranchId'
   )
BEGIN
    SET @Sql = N'ALTER TABLE dbo.UPISettings WITH NOCHECK
                 ADD CONSTRAINT FK_UPISettings_Branches_BranchId
                 FOREIGN KEY (BranchId) REFERENCES dbo.Branches(BranchId);';
    EXEC sp_executesql @Sql;
END

PRINT 'Branch-wise migration completed for RestaurantSettings, tbl_MailConfiguration, and UPISettings.';
