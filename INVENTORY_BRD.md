# INVENTORY MANAGEMENT SYSTEM — BRD & Implementation Reference
**Project:** Restaurant Management System — Stock & Inventory Module  
**Version:** 1.0  
**Last Updated:** 2025  
**Technology:** ASP.NET Core MVC (.NET 9) · SQL Server · Bootstrap 5 · DataTables · FontAwesome

---

## 1. Business Objectives

| # | Objective |
|---|-----------|
| 1 | Maintain accurate real-time stock balances across all godowns |
| 2 | Manage procurement through a structured PO → GRN workflow |
| 3 | Record inter-godown stock movements via transfers |
| 4 | Write off damaged / expired / wasted stock with audit trail |
| 5 | Compute item valuations using **Weighted Average Cost (WAVG)** |
| 6 | Generate actionable reports: ledger, closing stock, valuation, registers |
| 7 | Enforce business rules: main-godown purchase policy, negative stock control |

---

## 2. Module List

```
Stocks (Navigation Parent)
├── Inventory Dashboard       → Inventory/Index
├── Supplier Master           → Inventory/Suppliers
├── Inventory Parameters      → Inventory/Parameters
├── Opening Stock             → Inventory/OpeningStock
├── Purchase Orders           → PurchaseOrder/Index
├── Goods Receipt (GRN)       → GRN/Index
├── Stock Transfer            → StockTransfer/Index
├── Damage / Wastage          → DamageEntry/Index
├── Stock Ledger              → Inventory/StockLedger
├── Current Stock Summary     → Inventory/StockSummary
├── Closing Stock Report      → Inventory/ClosingStock
├── Stock Valuation Report    → Inventory/StockValuation
├── Purchase Register         → Inventory/PurchaseRegister
├── Transfer Register         → Inventory/TransferRegister
└── Damage Register           → Inventory/DamageRegister
```

---

## 3. Key Entities / Database Tables

| Table | Purpose |
|-------|---------|
| `GodownMaster` | Warehouse/storage locations. `IsMainGodown=1` identifies the primary godown. |
| `InventoryParameters` | Branch-level settings: `AllowNegativeStock`, `PurchaseOnlyFromMainGodown`, `DefaultValuationMethod` |
| `PartyMaster` | Supplier master. Used as `PartyType='Supplier'`. |
| `OpeningStock` | One-time opening balances per item per godown. Must be Posted before affecting `StockLedger`. |
| `PurchaseOrder` / `PurchaseOrderDetails` | Header + line items. Status: Draft → Approved → PartialGRN → FullGRN / Cancelled |
| `GRNMaster` / `GRNDetails` | Goods received against approved POs. `IsPosted=1` updates `StockLedger` and `CurrentStock`. |
| `StockTransfer` / `StockTransferDetails` | Inter-godown movements. `IsPosted=1` updates ledger for both godowns. |
| `DamageEntry` / `DamageEntryDetails` | Stock written off. `IsPosted=1` reduces `CurrentStock`. |
| `StockLedger` | Perpetual item-level ledger: every IN/OUT with running balance and avg cost. |
| `CurrentStock` | Aggregated position per item per godown (updated on each Post). |

---

## 4. Stored Procedures Reference

### 4.1 Godown & Parameters
| SP | Parameters | Purpose |
|----|-----------|---------|
| `usp_GetAllGodowns` | `@BranchId` | Returns all active godowns for branch |
| `usp_GetInventoryParameters` | `@BranchId` | Returns/creates branch inventory settings |
| `usp_SaveInventoryParameters` | All parameter columns | Upserts parameters |

### 4.2 Supplier (Party Master)
| SP | Parameters | Purpose |
|----|-----------|---------|
| `usp_GetAllParties` | `@BranchId, @PartyType='Supplier'` | List suppliers |
| `usp_GetPartyById` | `@PartyId` | Get single supplier |
| `usp_SaveParty` | All party columns | Insert/Update supplier |
| `usp_DeleteParty` | `@PartyId` | Soft delete |

### 4.3 Opening Stock
| SP | Parameters | Purpose |
|----|-----------|---------|
| `usp_GetOpeningStockList` | `@BranchId` | List all opening entries |
| `usp_GetOpeningStockById` | `@OpeningStockId` | Single row |
| `usp_SaveOpeningStock` | All cols | Insert/Update (Draft only) |
| `usp_PostOpeningStock` | `@OpeningStockId, @PostedBy` | Pushes to `StockLedger` |
| `usp_DeleteOpeningStock` | `@OpeningStockId` | Delete (Draft only) |

### 4.4 Purchase Orders
| SP | Parameters | Purpose |
|----|-----------|---------|
| `usp_GetNextPONumber` | `@BranchId` | Generates next sequential PO number |
| `usp_GetPurchaseOrderList` | `@BranchId, @Status=NULL` | Filtered list |
| `usp_GetPurchaseOrderById` | `@POId` | Header + lines |
| `usp_SavePurchaseOrder` | Header fields + `@LinesJson` | Upsert PO with lines |
| `usp_ApprovePurchaseOrder` | `@POId, @ApprovedBy` | Draft → Approved |
| `usp_CancelPurchaseOrder` | `@POId, @CancelledBy` | Any → Cancelled |

### 4.5 GRN
| SP | Parameters | Purpose |
|----|-----------|---------|
| `usp_GetGRNList` | `@BranchId` | All GRNs |
| `usp_GetGRNById` | `@GRNId` | Header + lines |
| `usp_GetPOForGRN` | `@BranchId` | Approved/PartialGRN POs |
| `usp_GetPODetailsForGRN` | `@POId` | PO lines with pending qty |
| `usp_SaveGRN` | Header + `@LinesJson` | Upsert GRN |
| `usp_PostGRN` | `@GRNId, @PostedBy` | Updates stock ledger |

### 4.6 Stock Transfer
| SP | Parameters | Purpose |
|----|-----------|---------|
| `usp_GetStockTransferList` | `@BranchId` | All transfers |
| `usp_GetStockTransferById` | `@TransferId` | Header + lines |
| `usp_SaveStockTransfer` | Header + `@LinesJson` | Upsert |
| `usp_PostStockTransfer` | `@TransferId, @PostedBy` | Creates OUT in from-godown, IN in to-godown |

### 4.7 Damage Entry
| SP | Parameters | Purpose |
|----|-----------|---------|
| `usp_GetDamageEntryList` | `@BranchId` | List |
| `usp_GetDamageEntryById` | `@DamageId` | Header + lines |
| `usp_SaveDamageEntry` | Header + `@LinesJson` | Upsert |
| `usp_PostDamageEntry` | `@DamageId, @PostedBy` | OUT from stock |

### 4.8 Reports & Ledger
| SP | Parameters | Purpose |
|----|-----------|---------|
| `usp_GetStockLedger` | `@BranchId, @GodownId, @ItemId, @TxnType, @FromDate, @ToDate` | Filtered ledger |
| `usp_GetCurrentStockSummary` | `@BranchId, @GodownId` | Current positions |
| `usp_GetClosingStockReport` | `@BranchId, @GodownId, @AsAtDate` | Balance sheet as at date |
| `usp_GetStockValuationReport` | `@BranchId, @GodownId, @ValuationMethod` | WAVG or FIFO values |
| `usp_GetPurchaseRegister` | `@BranchId, @SupplierId, @FromDate, @ToDate` | Purchase register |
| `usp_GetTransferRegister` | `@BranchId, @GodownId, @FromDate, @ToDate` | Transfer register |
| `usp_GetDamageRegister` | `@BranchId, @GodownId, @FromDate, @ToDate` | Damage register |
| `usp_GetInventoryDashboardStats` | `@BranchId` | KPI stats for dashboard |
| `usp_GetItemAverageCost` | `@ItemId, @GodownId` | Returns average cost for auto-fill |

---

## 5. Controllers

### 5.1 InventoryController.cs
Handles: Dashboard, Parameters, Suppliers CRUD, Opening Stock CRUD + Post, all report views, JSON API endpoints.

**JSON Endpoints (GET):**
- `/Inventory/GetGodownsJson?branchId=X` — all godowns
- `/Inventory/GetMainGodownJson?branchId=X` — main godown only
- `/Inventory/GetSuppliersJson?branchId=X` — suppliers
- `/Inventory/GetItemsJson?branchId=X` — items (from Ingredients table)
- `/Inventory/GetUOMsJson` — UOM list
- `/Inventory/GetItemAverageCost?itemId=X&godownId=Y` — avg cost

### 5.2 PurchaseOrderController.cs
Handles: PO CRUD, Approve, Cancel. Enforces `PurchaseOnlyFromMainGodown` flag.

**Main Godown Logic:**
```csharp
// In Form() action:
bool purchaseOnlyMain = GetPurchaseOnlyFromMainFlag(branchId);
if (purchaseOnlyMain) {
    int mainGodownId = GetMainGodownId(branchId);
    model.GodownId = mainGodownId;
    // ViewBag.Godowns = only main godown (readonly in view)
}
ViewBag.PurchaseOnlyMain = purchaseOnlyMain;
```

### 5.3 GRNController.cs
Handles: GRN CRUD, Post. Provides JSON endpoints for PO selection.

**AJAX Flow:**
1. `GET /GRN/GetPOList?branchId=X` → Returns approved/partial POs
2. User selects PO → `GET /GRN/GetPODetails?poId=Y` → Returns PO lines with pending qty
3. User fills received qty → Submit → `usp_SaveGRN` with `@LinesJson`
4. Post button → `usp_PostGRN(grnId, userId)` → Updates `StockLedger`, `CurrentStock`, PO status

### 5.4 StockTransferController.cs
Handles: Transfer CRUD, Post. Validates from ≠ to godown.

**Cost Mode:** When "Weighted Average" is selected, AJAX calls `/Inventory/GetItemAverageCost` to auto-fill unit cost per item.

### 5.5 DamageEntryController.cs
Handles: Damage CRUD, Post. Auto-fills unit cost via avg cost AJAX.

---

## 6. JSON Line Item Formats

### Purchase Order (`linesJson`)
```json
[
  {
    "itemId": 1,
    "uomId": 2,
    "qty": 100.0,
    "rate": 45.50,
    "gstPct": 5.0,
    "remarks": ""
  }
]
```

### GRN (`linesJson`)
```json
[
  {
    "poDetailId": 10,
    "itemId": 1,
    "uomId": 2,
    "receivedQty": 95.0,
    "rejectedQty": 5.0,
    "unitRate": 45.50,
    "gstPct": 5.0
  }
]
```

### Stock Transfer (`linesJson`)
```json
[
  {
    "itemId": 1,
    "uomId": 2,
    "quantity": 20.0,
    "unitCost": 45.50
  }
]
```

### Damage Entry (`linesJson`)
```json
[
  {
    "itemId": 1,
    "uomId": 2,
    "quantity": 5.0,
    "unitCost": 45.50,
    "reason": "Expired"
  }
]
```

---

## 7. Business Rules

| # | Rule | Enforcement |
|---|------|-------------|
| BR-01 | All purchases must go to the **Main Godown** when `PurchaseOnlyFromMainGodown = true` | `PurchaseOrderController.Form()` — GodownId forced from `GetMainGodownId()` |
| BR-02 | Transfer cannot be from and to the **same godown** | Validated in `StockTransferController.Form()` on submit |
| BR-03 | An Opening Stock entry must be **Posted** before affecting stock balance | `usp_PostOpeningStock` sets `IsPosted = 1` and writes to `StockLedger` |
| BR-04 | A GRN can only link to an **Approved** or **PartialGRN** PO | `usp_GetPOForGRN` filters status |
| BR-05 | Once a document is **Posted**, it cannot be edited or deleted | Views hide edit/delete buttons when `IsPosted = true` |
| BR-06 | A PO moves to `PartialGRN` when some qty received, `FullGRN` when all received | Handled inside `usp_PostGRN` |
| BR-07 | Valuation uses **Weighted Average Cost** by default | `usp_GetStockValuationReport @ValuationMethod='WAVG'` |
| BR-08 | Negative stock is controlled by `AllowNegativeStock` parameter | `usp_PostTransfer/usp_PostDamage` checks parameter |

---

## 8. Workflow Diagrams

### Purchase Flow
```
Supplier Master → Purchase Order (Draft)
                       ↓ [Approve]
               Purchase Order (Approved)
                       ↓ [Create GRN]
             GRN (Draft) → [Post to Stock]
                       ↓
              Stock Ledger (IN entry)
              Current Stock (QTY ↑)
              PO Status → PartialGRN / FullGRN
```

### Transfer Flow
```
From Godown Stock → Stock Transfer (Draft)
                         ↓ [Post]
          From Godown: Stock Ledger (OUT)
          To Godown:   Stock Ledger (IN)
          Current Stock updated for both
```

### Damage Flow
```
Godown Stock → Damage Entry (Draft)
                    ↓ [Post]
          Stock Ledger (OUT - DAMAGE)
          Current Stock (QTY ↓)
```

---

## 9. View Files

| View Path | Purpose |
|-----------|---------|
| `Views/Inventory/Index.cshtml` | Dashboard: KPI cards, quick access, low stock alerts |
| `Views/Inventory/Parameters.cshtml` | Inventory Parameters toggle form |
| `Views/Inventory/Suppliers.cshtml` | Supplier list (DataTable) |
| `Views/Inventory/SupplierForm.cshtml` | Add/Edit supplier |
| `Views/Inventory/OpeningStock.cshtml` | Opening stock list with Post action |
| `Views/Inventory/OpeningStockForm.cshtml` | Single-item opening stock entry |
| `Views/Inventory/StockLedger.cshtml` | Filtered stock ledger with date range |
| `Views/Inventory/StockSummary.cshtml` | Current stock positions with reorder indicators |
| `Views/Inventory/ClosingStock.cshtml` | Stock as at a date: opening + in - out |
| `Views/Inventory/StockValuation.cshtml` | WAVG/FIFO valuation report |
| `Views/Inventory/PurchaseRegister.cshtml` | All GRN purchase transactions |
| `Views/Inventory/TransferRegister.cshtml` | Inter-godown transfer history |
| `Views/Inventory/DamageRegister.cshtml` | Damage/wastage history |
| `Views/PurchaseOrder/Index.cshtml` | PO list with status badge filter tabs |
| `Views/PurchaseOrder/Form.cshtml` | Dynamic line-item PO form (with JS grid) |
| `Views/PurchaseOrder/Details.cshtml` | PO details with Approve/Cancel/GRN buttons |
| `Views/GRN/Index.cshtml` | GRN list |
| `Views/GRN/Form.cshtml` | GRN form with PO-driven line auto-load (AJAX) |
| `Views/GRN/Details.cshtml` | GRN details with Post to Stock button |
| `Views/StockTransfer/Index.cshtml` | Transfer list |
| `Views/StockTransfer/Form.cshtml` | Transfer form with WAVG cost auto-fill |
| `Views/StockTransfer/Details.cshtml` | Transfer details with Post button |
| `Views/DamageEntry/Index.cshtml` | Damage entry list |
| `Views/DamageEntry/Form.cshtml` | Damage form with avg cost auto-fill + reason |
| `Views/DamageEntry/Details.cshtml` | Damage entry details with Post button |

---

## 10. SQL Scripts

| File | Purpose |
|------|---------|
| `SQL/inventory_complete_setup.sql` | ALL tables and stored procedures (2092 lines) |
| `SQL/inventory_navigation_stocks.sql` | Navigation menu MERGE script for Stocks section |

### Deployment Order
```
1. Run: SQL/inventory_complete_setup.sql      (creates all tables + SPs)
2. Run: SQL/inventory_navigation_stocks.sql   (adds Stocks nav menu items)
3. Build: dotnet build
4. Run application
```

---

## 11. GodownMaster Duality

> **Important**: There are **two** separate godown entities. Do not confuse them.

| | EF Entity (`Godown`) | SQL SP Entity (`GodownMaster`) |
|-|----------------------|-------------------------------|
| Table | `Godowns` | `GodownMaster` |
| Columns | `Id`, `Code`, `GodownName`, `IsMainGodown` | `GodownId`, `GodownCode`, `GodownName`, `GodownType`, `IsMainGodown` |
| Used by | `MasterController` (EF DbContext) | All Inventory SPs via raw SQL |
| Managed via | EF CRUD | `usp_GetAllGodowns`, `usp_SaveGodown` |

The `Master/GodownList` and `Master/GodownForm` pages manage the EF `Godowns` table.  
All inventory SPs (PO, GRN, Transfer, etc.) use `GodownMaster` table.

---

## 12. Dependencies on Existing Modules

| Module | How Used |
|--------|---------|
| **Item Master** (`Ingredients` table) | Items are sourced via `/Inventory/GetItemsJson` — reads from `Ingredients` |
| **UOM Master** (`UomMasters` table) | Units via `/Inventory/GetUOMsJson` |
| **Godown Master** (`GodownMaster` SQL table) | All inventory SPs use this. Do NOT recreate. |
| **Branch Session** | `User.GetActiveBranchId()` used in every controller action |
| **Navigation** | Dynamic DB-driven via `NavigationMenus` table |

---

## 13. Testing Checklist

- [ ] Run `inventory_complete_setup.sql` on target database
- [ ] Run `inventory_navigation_stocks.sql`
- [ ] Set `PurchaseOnlyFromMainGodown = true` in Parameters — verify PO form locks godown
- [ ] Create PO → Approve → Create GRN → Post GRN → Verify `CurrentStock` updated
- [ ] Create Stock Transfer → Post → Verify both godowns updated in `StockLedger`
- [ ] Create Damage Entry → Post → Verify stock reduced
- [ ] View Stock Ledger — check running balance column
- [ ] View Stock Valuation — verify WAVG = total value / total qty
- [ ] Verify Closing Stock = Opening + GRN - Transfers-Out - Damage
- [ ] Verify navigation menu shows all 15 items under "Stocks"
