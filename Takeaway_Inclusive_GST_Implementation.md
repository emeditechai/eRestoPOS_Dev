# Takeaway / Delivery Inclusive GST Implementation

**Commit**: `2f654f2`  
**Branch**: `main`  
**Date**: 2025

---

## Overview

When the **"Takeaway Included GST"** setting is enabled in Restaurant Settings, orders of type **Takeout (1)** and **Delivery (2)** will calculate GST **inclusively** — meaning the item price already contains the GST, and it is back-calculated from the total.

This mirrors existing behaviour for **Bar orders**, which have always been inclusive.

---

## Configuration

### Setting Location
- **Page**: Restaurant Settings (Admin panel)
- **Field**: `Takeaway Included GST` (toggle / checkbox)
- **DB Column**: `dbo.RestaurantSettings.Is_TakeawayIncludedGST_Req` (bit, default `0`)

### GST Rate Used
| Order Type | GST Column in Settings |
|---|---|
| Dine-In (0), Room Service (4), Online (3) | `DefaultGSTPercentage` |
| Takeout (1), Delivery (2) | `TakeAwayGSTPercentage` |
| Bar orders | `BarGSTPerc` |

---

## Calculation Logic

### Inclusive GST (Bar, and Takeout/Delivery when setting = ON)
The item/order price **already includes GST**. GST is extracted by back-calculation:

```
TaxableBase  = SubTotal / (1 + GSTRate / 100)
GSTAmount    = SubTotal - TaxableBase
TotalAmount  = SubTotal          ← price does NOT change
```

**Example**: Item priced at ₹100, GST = 5%
- Taxable base = 100 / 1.05 = ₹95.24
- GST amount   = 100 − 95.24 = ₹4.76
- Total        = ₹100 (unchanged)

### Exclusive GST (Dine-In, and Takeout/Delivery when setting = OFF)
GST is **added on top** of the item price:

```
GSTAmount    = SubTotal × GSTRate / 100
TotalAmount  = SubTotal + GSTAmount
```

**Example**: Item priced at ₹100, GST = 5%
- GST amount = ₹5.00
- Total      = ₹105.00

---

## Affected Order Types

| OrderType | Value | Affected by setting |
|---|---|---|
| Dine-In | 0 | No (always exclusive) |
| **Takeout** | **1** | **Yes** |
| **Delivery** | **2** | **Yes** |
| Online | 3 | No (always exclusive) |
| Room Service | 4 | No (always exclusive) |
| Bar | — | Always inclusive (unchanged) |

---

## Code Changes — `OrderController.cs`

### 1. `UpdateOrderFinancials` method
- Reads `ISNULL(o.OrderType, 0) AS OrderType` from the Orders query.
- Settings query expanded to read `TakeAwayGSTPercentage` and `Is_TakeawayIncludedGST_Req` (both guarded with `COL_LENGTH` for backwards compatibility).
- Introduced flag:
  ```csharp
  bool useInclusiveGST = isBarOrder || (isTakeawayOrder && isTakeawayIncludedGSTReq);
  ```
- `if (isBarOrder)` → `if (useInclusiveGST)` to switch between inclusive / exclusive branch.

### 2. `UpdateOrderItemGstDetails` method (T-SQL block)
- Added T-SQL variables: `@isTakeawayOrder`, `@isTakeawayInclGST`, `@useInclusiveGST`.
- `@isTakeawayOrder` is set by reading `Orders.OrderType` for the current order.
- `@isTakeawayInclGST` is read from `RestaurantSettings.Is_TakeawayIncludedGST_Req` inside a TRY/CATCH (defaults to 0 if column missing).
- `@useInclusiveGST = 1` when Bar OR (Takeaway AND inclusive setting ON).
- Per-item GST CTE switches formula based on `@useInclusiveGST`.
- GST rate in CTE uses `TakeAwayGSTPercentage` for takeaway orders.

### 3. Legacy GST fallback block (~line 5920)
- Replaced single-column `gstColumn` string-interpolation approach with a full `SELECT` of all 4 GST columns (guarded with `COL_LENGTH`).
- Added `isTakeawayOrder`, `isTakeawayInclusiveGST`, and `useInclusiveGSTLegacy` flags.
- Applies inclusive back-calculation formula when `useInclusiveGSTLegacy = true`.

---

## DB Columns Required

All columns already exist in `dbo.RestaurantSettings`. **No new DB migration is needed.**

| Column | Type | Purpose |
|---|---|---|
| `DefaultGSTPercentage` | decimal | GST % for Dine-In / default |
| `TakeAwayGSTPercentage` | decimal | GST % for Takeout / Delivery |
| `BarGSTPerc` | decimal | GST % for Bar orders |
| `Is_TakeawayIncludedGST_Req` | bit | 1 = inclusive GST for Takeout/Delivery |
| `IsDefaultGSTRequired` | bit | Whether to apply GST for Dine-In |
| `IsTakeAwayGSTRequired` | bit | Whether to apply GST for Takeout/Delivery |

---

## Production Deployment Checklist

1. **No DB schema changes required** — columns already exist.
2. Deploy new publish output (contains `OrderController.cs` changes).
3. Verify Restaurant Settings → "Takeaway Included GST" toggle works as expected.
4. Test a Takeout order:
   - Setting OFF → total = item price + GST (exclusive)
   - Setting ON  → total = item price (GST is within price, broken out on bill)

---

## Testing Scenarios

| Scenario | Expected |
|---|---|
| Takeout, setting OFF, item ₹100, GST 3% | Total = ₹103.00, GST = ₹3.00 |
| Takeout, setting ON, item ₹100, GST 3% | Total = ₹100.00, GST = ₹2.91 (back-calculated) |
| Delivery, setting ON, item ₹200, GST 3% | Total = ₹200.00, GST = ₹5.83 |
| Dine-In (any setting value), item ₹100, GST 5% | Total = ₹105.00, GST = ₹5.00 |
| Bar order (always inclusive), item ₹100, GST 10% | Total = ₹100.00, GST = ₹9.09 |

---

## Related Implementations for Reference

- **Bar Order Inclusive GST**: pre-existing, in same methods — `isBarOrder` flag
- **Takeaway GST Rate** (`TakeAwayGSTPercentage`): pre-existing column, now also used in item-level GST detail calculation
- See also: `BAR_Order_GST_Display_Fix.md` for Bar GST display fixes
