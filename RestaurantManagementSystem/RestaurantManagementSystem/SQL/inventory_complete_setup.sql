-- =============================================
-- Script : inventory_complete_setup.sql
-- Purpose: Complete Inventory & Purchase Management Setup
--          Tables, Stored Procedures for Restaurant Inventory System
-- Run on : dev_Restaurant (or production DB)
-- Safe   : Uses IF NOT EXISTS guards; re-runnable
-- =============================================

USE [dev_Restaurant];
GO

SET XACT_ABORT ON;
GO

PRINT '=== STEP 1: GodownMaster — using existing dbo.Godowns table (Id, Code, GodownName, IsMainGodown) ===';
GO

PRINT '=== STEP 2: Create InventoryParameters table ===';
IF OBJECT_ID(N'dbo.InventoryParameters', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventoryParameters (
        ParamId                     INT             IDENTITY(1,1) NOT NULL,
        BranchId                    INT             NOT NULL,
        PurchaseOnlyFromMainGodown  BIT             NOT NULL DEFAULT 0,
        GRNMandatory                BIT             NOT NULL DEFAULT 1,
        AllowDirectPurchase         BIT             NOT NULL DEFAULT 1,
        TransferPriceMode           NVARCHAR(20)    NOT NULL DEFAULT 'AverageCost', -- AverageCost / ManualPrice
        NegativeStockAllowed        BIT             NOT NULL DEFAULT 0,
        AutoConsumptionOnSale       BIT             NOT NULL DEFAULT 1,
        UpdatedAt                   DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedBy                   INT             NULL,
        CONSTRAINT PK_InventoryParameters PRIMARY KEY (ParamId),
        CONSTRAINT UQ_InventoryParameters_Branch UNIQUE (BranchId)
    );
    PRINT '  InventoryParameters created.';
END ELSE PRINT '  InventoryParameters already exists.';
GO

PRINT '=== STEP 3: Create OpeningStock table ===';
IF OBJECT_ID(N'dbo.OpeningStock', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OpeningStock (
        OpeningStockId  INT             IDENTITY(1,1) NOT NULL,
        BranchId        INT             NOT NULL,
        GodownId        INT             NOT NULL,
        ItemId          INT             NOT NULL,
        StockDate       DATE            NOT NULL,
        Quantity        DECIMAL(18,3)   NOT NULL,
        UOMId           INT             NOT NULL,
        CostPrice       DECIMAL(18,4)   NOT NULL DEFAULT 0,
        TotalValue      AS (Quantity * CostPrice) PERSISTED,
        Remarks         NVARCHAR(300)   NULL,
        IsPosted        BIT             NOT NULL DEFAULT 0,
        CreatedAt       DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy       INT             NULL,
        UpdatedAt       DATETIME2(3)    NULL,
        CONSTRAINT PK_OpeningStock PRIMARY KEY (OpeningStockId),
        CONSTRAINT FK_OpeningStock_Godown FOREIGN KEY (GodownId) REFERENCES dbo.Godowns(Id),
        CONSTRAINT FK_OpeningStock_Item   FOREIGN KEY (ItemId)   REFERENCES dbo.Ingredients(Id)
    );
    CREATE INDEX IX_OpeningStock_BranchId  ON dbo.OpeningStock (BranchId);
    CREATE INDEX IX_OpeningStock_GodownId  ON dbo.OpeningStock (GodownId);
    CREATE INDEX IX_OpeningStock_ItemId    ON dbo.OpeningStock (ItemId);
    CREATE INDEX IX_OpeningStock_StockDate ON dbo.OpeningStock (StockDate);
    PRINT '  OpeningStock created.';
END ELSE PRINT '  OpeningStock already exists.';
GO

PRINT '=== STEP 4: PartyMaster — using existing dbo.Parties table (Id, PartyName, PartyType) ===';
GO

PRINT '=== STEP 5: Create PurchaseOrder tables ===';
IF OBJECT_ID(N'dbo.PurchaseOrder', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseOrder (
        POId            INT             IDENTITY(1,1) NOT NULL,
        PONumber        NVARCHAR(30)    NOT NULL,
        BranchId        INT             NOT NULL,
        GodownId        INT             NOT NULL,
        SupplierId      INT             NOT NULL,
        PODate          DATE            NOT NULL,
        ExpectedDate    DATE            NULL,
        GSTType         NVARCHAR(20)    NOT NULL DEFAULT 'Exclusive', -- Inclusive / Exclusive / None
        PaymentTerms    NVARCHAR(100)   NULL,
        Remarks         NVARCHAR(500)   NULL,
        Status          NVARCHAR(20)    NOT NULL DEFAULT 'Draft', -- Draft / Approved / PartialGRN / Closed / Cancelled
        SubTotal        DECIMAL(18,2)   NOT NULL DEFAULT 0,
        TotalGSTAmount  DECIMAL(18,2)   NOT NULL DEFAULT 0,
        TotalAmount     DECIMAL(18,2)   NOT NULL DEFAULT 0,
        ApprovedBy      INT             NULL,
        ApprovedAt      DATETIME2(3)    NULL,
        CreatedAt       DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy       INT             NULL,
        UpdatedAt       DATETIME2(3)    NULL,
        CONSTRAINT PK_PurchaseOrder PRIMARY KEY (POId),
        CONSTRAINT UQ_PurchaseOrder_Number UNIQUE (BranchId, PONumber),
        CONSTRAINT FK_PurchaseOrder_Godown   FOREIGN KEY (GodownId)   REFERENCES dbo.Godowns(Id),
        CONSTRAINT FK_PurchaseOrder_Supplier FOREIGN KEY (SupplierId) REFERENCES dbo.Parties(Id)
    );
    CREATE INDEX IX_PurchaseOrder_BranchId ON dbo.PurchaseOrder (BranchId);
    CREATE INDEX IX_PurchaseOrder_PODate   ON dbo.PurchaseOrder (PODate);
    CREATE INDEX IX_PurchaseOrder_Status   ON dbo.PurchaseOrder (Status);
    PRINT '  PurchaseOrder created.';
END ELSE PRINT '  PurchaseOrder already exists.';
GO

IF OBJECT_ID(N'dbo.PurchaseOrderDetails', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseOrderDetails (
        PODetailId      INT             IDENTITY(1,1) NOT NULL,
        POId            INT             NOT NULL,
        ItemId          INT             NOT NULL,
        UOMId           INT             NOT NULL,
        OrderedQty      DECIMAL(18,3)   NOT NULL,
        ReceivedQty     DECIMAL(18,3)   NOT NULL DEFAULT 0,
        PendingQty      AS (OrderedQty - ReceivedQty) PERSISTED,
        UnitRate        DECIMAL(18,4)   NOT NULL,
        GSTPercent      DECIMAL(5,2)    NOT NULL DEFAULT 0,
        GSTAmount       AS (OrderedQty * UnitRate * GSTPercent / 100) PERSISTED,
        LineAmount      AS (OrderedQty * UnitRate) PERSISTED,
        TaxableAmount   AS (OrderedQty * UnitRate) PERSISTED,
        Remarks         NVARCHAR(200)   NULL,
        CONSTRAINT PK_PODetails    PRIMARY KEY (PODetailId),
        CONSTRAINT FK_PODetails_PO FOREIGN KEY (POId)    REFERENCES dbo.PurchaseOrder(POId) ON DELETE CASCADE,
        CONSTRAINT FK_PODetails_Item FOREIGN KEY (ItemId) REFERENCES dbo.Ingredients(Id)
    );
    CREATE INDEX IX_PODetails_POId   ON dbo.PurchaseOrderDetails (POId);
    CREATE INDEX IX_PODetails_ItemId ON dbo.PurchaseOrderDetails (ItemId);
    PRINT '  PurchaseOrderDetails created.';
END ELSE PRINT '  PurchaseOrderDetails already exists.';
GO

PRINT '=== STEP 6: Create GRN tables ===';
IF OBJECT_ID(N'dbo.GRNMaster', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GRNMaster (
        GRNId           INT             IDENTITY(1,1) NOT NULL,
        GRNNumber       NVARCHAR(30)    NOT NULL,
        BranchId        INT             NOT NULL,
        POId            INT             NULL,
        GodownId        INT             NOT NULL,
        SupplierId      INT             NOT NULL,
        GRNDate         DATE            NOT NULL,
        InvoiceNo       NVARCHAR(50)    NULL,
        InvoiceDate     DATE            NULL,
        GSTType         NVARCHAR(20)    NOT NULL DEFAULT 'Exclusive',
        SubTotal        DECIMAL(18,2)   NOT NULL DEFAULT 0,
        TotalGSTAmount  DECIMAL(18,2)   NOT NULL DEFAULT 0,
        TotalAmount     DECIMAL(18,2)   NOT NULL DEFAULT 0,
        Status          NVARCHAR(20)    NOT NULL DEFAULT 'Draft', -- Draft / Posted / Cancelled
        Remarks         NVARCHAR(500)   NULL,
        CreatedAt       DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy       INT             NULL,
        UpdatedAt       DATETIME2(3)    NULL,
        CONSTRAINT PK_GRNMaster     PRIMARY KEY (GRNId),
        CONSTRAINT UQ_GRNMaster_No  UNIQUE (BranchId, GRNNumber),
        CONSTRAINT FK_GRN_Godown    FOREIGN KEY (GodownId)   REFERENCES dbo.Godowns(Id),
        CONSTRAINT FK_GRN_Supplier  FOREIGN KEY (SupplierId) REFERENCES dbo.Parties(Id)
    );
    CREATE INDEX IX_GRNMaster_BranchId ON dbo.GRNMaster (BranchId);
    CREATE INDEX IX_GRNMaster_GRNDate  ON dbo.GRNMaster (GRNDate);
    CREATE INDEX IX_GRNMaster_POId     ON dbo.GRNMaster (POId);
    PRINT '  GRNMaster created.';
END ELSE PRINT '  GRNMaster already exists.';
GO

IF OBJECT_ID(N'dbo.GRNDetails', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GRNDetails (
        GRNDetailId     INT             IDENTITY(1,1) NOT NULL,
        GRNId           INT             NOT NULL,
        PODetailId      INT             NULL,
        ItemId          INT             NOT NULL,
        UOMId           INT             NOT NULL,
        OrderedQty      DECIMAL(18,3)   NOT NULL DEFAULT 0,
        ReceivedQty     DECIMAL(18,3)   NOT NULL,
        RejectedQty     DECIMAL(18,3)   NOT NULL DEFAULT 0,
        AcceptedQty     AS (ReceivedQty - RejectedQty) PERSISTED,
        UnitRate        DECIMAL(18,4)   NOT NULL,
        GSTPercent      DECIMAL(5,2)    NOT NULL DEFAULT 0,
        GSTAmount       AS (ReceivedQty * UnitRate * GSTPercent / 100) PERSISTED,
        LineAmount      AS (ReceivedQty * UnitRate) PERSISTED,
        Remarks         NVARCHAR(200)   NULL,
        CONSTRAINT PK_GRNDetails       PRIMARY KEY (GRNDetailId),
        CONSTRAINT FK_GRNDetails_GRN   FOREIGN KEY (GRNId)   REFERENCES dbo.GRNMaster(GRNId) ON DELETE CASCADE,
        CONSTRAINT FK_GRNDetails_Item  FOREIGN KEY (ItemId)  REFERENCES dbo.Ingredients(Id)
    );
    CREATE INDEX IX_GRNDetails_GRNId  ON dbo.GRNDetails (GRNId);
    CREATE INDEX IX_GRNDetails_ItemId ON dbo.GRNDetails (ItemId);
    PRINT '  GRNDetails created.';
END ELSE PRINT '  GRNDetails already exists.';
GO

PRINT '=== STEP 7: Create StockTransfer tables ===';
IF OBJECT_ID(N'dbo.StockTransfer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockTransfer (
        TransferId      INT             IDENTITY(1,1) NOT NULL,
        TransferNumber  NVARCHAR(30)    NOT NULL,
        BranchId        INT             NOT NULL,
        FromGodownId    INT             NOT NULL,
        ToGodownId      INT             NOT NULL,
        TransferDate    DATE            NOT NULL,
        TransferType    NVARCHAR(20)    NOT NULL DEFAULT 'Internal', -- MainToBranch / InterGodown / Internal
        PriceMode       NVARCHAR(20)    NOT NULL DEFAULT 'AverageCost', -- AverageCost / ManualPrice
        Status          NVARCHAR(20)    NOT NULL DEFAULT 'Draft', -- Draft / Posted / Cancelled
        Remarks         NVARCHAR(500)   NULL,
        TotalQty        DECIMAL(18,3)   NOT NULL DEFAULT 0,
        TotalValue      DECIMAL(18,2)   NOT NULL DEFAULT 0,
        CreatedAt       DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy       INT             NULL,
        UpdatedAt       DATETIME2(3)    NULL,
        CONSTRAINT PK_StockTransfer    PRIMARY KEY (TransferId),
        CONSTRAINT UQ_StockTransfer_No UNIQUE (BranchId, TransferNumber),
        CONSTRAINT FK_Transfer_FromGodown FOREIGN KEY (FromGodownId) REFERENCES dbo.Godowns(Id),
        CONSTRAINT FK_Transfer_ToGodown   FOREIGN KEY (ToGodownId)   REFERENCES dbo.Godowns(Id)
    );
    CREATE INDEX IX_StockTransfer_BranchId      ON dbo.StockTransfer (BranchId);
    CREATE INDEX IX_StockTransfer_TransferDate  ON dbo.StockTransfer (TransferDate);
    CREATE INDEX IX_StockTransfer_FromGodownId  ON dbo.StockTransfer (FromGodownId);
    PRINT '  StockTransfer created.';
END ELSE PRINT '  StockTransfer already exists.';
GO

IF OBJECT_ID(N'dbo.StockTransferDetails', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockTransferDetails (
        TransferDetailId INT            IDENTITY(1,1) NOT NULL,
        TransferId      INT             NOT NULL,
        ItemId          INT             NOT NULL,
        UOMId           INT             NOT NULL,
        Quantity        DECIMAL(18,3)   NOT NULL,
        UnitCost        DECIMAL(18,4)   NOT NULL DEFAULT 0,
        TotalCost       AS (Quantity * UnitCost) PERSISTED,
        Remarks         NVARCHAR(200)   NULL,
        CONSTRAINT PK_TransferDetails     PRIMARY KEY (TransferDetailId),
        CONSTRAINT FK_TransferDtl_Header  FOREIGN KEY (TransferId) REFERENCES dbo.StockTransfer(TransferId) ON DELETE CASCADE,
        CONSTRAINT FK_TransferDtl_Item    FOREIGN KEY (ItemId)     REFERENCES dbo.Ingredients(Id)
    );
    CREATE INDEX IX_TransferDetails_TransferId ON dbo.StockTransferDetails (TransferId);
    CREATE INDEX IX_TransferDetails_ItemId     ON dbo.StockTransferDetails (ItemId);
    PRINT '  StockTransferDetails created.';
END ELSE PRINT '  StockTransferDetails already exists.';
GO

PRINT '=== STEP 8: Create DamageEntry tables ===';
IF OBJECT_ID(N'dbo.DamageEntry', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DamageEntry (
        DamageId        INT             IDENTITY(1,1) NOT NULL,
        DamageNumber    NVARCHAR(30)    NOT NULL,
        BranchId        INT             NOT NULL,
        GodownId        INT             NOT NULL,
        DamageDate      DATE            NOT NULL,
        DamageType      NVARCHAR(20)    NOT NULL DEFAULT 'Damage', -- Damage / Wastage / Return / Expired
        Status          NVARCHAR(20)    NOT NULL DEFAULT 'Draft',  -- Draft / Posted / Cancelled
        Remarks         NVARCHAR(500)   NULL,
        TotalQty        DECIMAL(18,3)   NOT NULL DEFAULT 0,
        TotalValue      DECIMAL(18,2)   NOT NULL DEFAULT 0,
        CreatedAt       DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy       INT             NULL,
        UpdatedAt       DATETIME2(3)    NULL,
        CONSTRAINT PK_DamageEntry    PRIMARY KEY (DamageId),
        CONSTRAINT UQ_DamageEntry_No UNIQUE (BranchId, DamageNumber),
        CONSTRAINT FK_DamageEntry_Godown FOREIGN KEY (GodownId) REFERENCES dbo.Godowns(Id)
    );
    CREATE INDEX IX_DamageEntry_BranchId  ON dbo.DamageEntry (BranchId);
    CREATE INDEX IX_DamageEntry_DamageDate ON dbo.DamageEntry (DamageDate);
    PRINT '  DamageEntry created.';
END ELSE PRINT '  DamageEntry already exists.';
GO

IF OBJECT_ID(N'dbo.DamageEntryDetails', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DamageEntryDetails (
        DamageDetailId  INT             IDENTITY(1,1) NOT NULL,
        DamageId        INT             NOT NULL,
        ItemId          INT             NOT NULL,
        UOMId           INT             NOT NULL,
        Quantity        DECIMAL(18,3)   NOT NULL,
        UnitCost        DECIMAL(18,4)   NOT NULL DEFAULT 0,
        TotalCost       AS (Quantity * UnitCost) PERSISTED,
        Reason          NVARCHAR(200)   NULL,
        CONSTRAINT PK_DamageDetails       PRIMARY KEY (DamageDetailId),
        CONSTRAINT FK_DamageDtl_Header    FOREIGN KEY (DamageId) REFERENCES dbo.DamageEntry(DamageId) ON DELETE CASCADE,
        CONSTRAINT FK_DamageDtl_Item      FOREIGN KEY (ItemId)   REFERENCES dbo.Ingredients(Id)
    );
    CREATE INDEX IX_DamageDetails_DamageId ON dbo.DamageEntryDetails (DamageId);
    CREATE INDEX IX_DamageDetails_ItemId   ON dbo.DamageEntryDetails (ItemId);
    PRINT '  DamageEntryDetails created.';
END ELSE PRINT '  DamageEntryDetails already exists.';
GO

PRINT '=== STEP 9: Create StockLedger table ===';
IF OBJECT_ID(N'dbo.StockLedger', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockLedger (
        LedgerId        INT             IDENTITY(1,1) NOT NULL,
        BranchId        INT             NOT NULL,
        GodownId        INT             NOT NULL,
        ItemId          INT             NOT NULL,
        TransactionDate DATE            NOT NULL,
        TransactionType NVARCHAR(30)    NOT NULL,
        -- OPENING / PURCHASE / GRN / TRANSFER_IN / TRANSFER_OUT
        -- SALE_CONSUMPTION / DAMAGE / RETURN / ADJUSTMENT
        ReferenceType   NVARCHAR(30)    NULL,  -- PO / GRN / TRANSFER / DAMAGE / SALE
        ReferenceId     INT             NULL,
        ReferenceNumber NVARCHAR(30)    NULL,
        InQuantity      DECIMAL(18,3)   NOT NULL DEFAULT 0,
        OutQuantity     DECIMAL(18,3)   NOT NULL DEFAULT 0,
        UnitCost        DECIMAL(18,4)   NOT NULL DEFAULT 0,
        TotalValue      AS ((InQuantity - OutQuantity) * UnitCost) PERSISTED,
        BalanceQty      DECIMAL(18,3)   NOT NULL DEFAULT 0,  -- running balance
        BalanceValue    DECIMAL(18,2)   NOT NULL DEFAULT 0,  -- running balance value (avg cost)
        AverageCost     DECIMAL(18,4)   NOT NULL DEFAULT 0,  -- weighted avg cost after this txn
        Remarks         NVARCHAR(300)   NULL,
        CreatedAt       DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy       INT             NULL,
        CONSTRAINT PK_StockLedger PRIMARY KEY (LedgerId)
    );
    CREATE INDEX IX_StockLedger_BranchId        ON dbo.StockLedger (BranchId);
    CREATE INDEX IX_StockLedger_GodownItem      ON dbo.StockLedger (GodownId, ItemId);
    CREATE INDEX IX_StockLedger_TransactionDate ON dbo.StockLedger (TransactionDate);
    CREATE INDEX IX_StockLedger_TransactionType ON dbo.StockLedger (TransactionType);
    PRINT '  StockLedger created.';
END ELSE PRINT '  StockLedger already exists.';
GO

PRINT '=== STEP 10: Create CurrentStock (materialized summary) table ===';
IF OBJECT_ID(N'dbo.CurrentStock', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CurrentStock (
        StockId         INT             IDENTITY(1,1) NOT NULL,
        BranchId        INT             NOT NULL,
        GodownId        INT             NOT NULL,
        ItemId          INT             NOT NULL,
        BalanceQty      DECIMAL(18,3)   NOT NULL DEFAULT 0,
        AverageCost     DECIMAL(18,4)   NOT NULL DEFAULT 0,
        StockValue      AS (BalanceQty * AverageCost) PERSISTED,
        LastUpdated     DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_CurrentStock PRIMARY KEY (StockId),
        CONSTRAINT UQ_CurrentStock UNIQUE (BranchId, GodownId, ItemId)
    );
    CREATE INDEX IX_CurrentStock_BranchGodown ON dbo.CurrentStock (BranchId, GodownId);
    CREATE INDEX IX_CurrentStock_ItemId       ON dbo.CurrentStock (ItemId);
    PRINT '  CurrentStock created.';
END ELSE PRINT '  CurrentStock already exists.';
GO

-- =============================================
-- STORED PROCEDURES
-- =============================================

-- ─────────────────────────────────────────────────────────────────────────────
-- GODOWN MASTER
-- ─────────────────────────────────────────────────────────────────────────────

PRINT '=== Creating usp_GetAllGodowns (uses dbo.Godowns) ===';

IF OBJECT_ID(N'dbo.usp_GetAllGodowns', N'P') IS NOT NULL DROP PROCEDURE dbo.usp_GetAllGodowns;
GO

CREATE PROCEDURE dbo.usp_GetAllGodowns
    @BranchId INT
AS
BEGIN
    SET NOCOUNT ON;
    -- Uses existing dbo.Godowns table (EF-managed)
    SELECT
        g.Id            AS GodownId,
        g.BranchId,
        g.Code          AS GodownCode,
        g.GodownName,
        CASE WHEN g.IsMainGodown = 1 THEN 'Main' ELSE 'Sub' END AS GodownType,
        g.Address,
        NULL            AS ContactPerson,
        NULL            AS ContactPhone,
        g.IsMainGodown,
        g.IsActive,
        g.CreatedAt,
        g.UpdatedAt
    FROM dbo.Godowns g
    WHERE g.BranchId = @BranchId AND g.IsActive = 1
    ORDER BY g.GodownName;
END
GO


PRINT '=== All inventory stored procedures created successfully. ===';
GO
