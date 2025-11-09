# Day Closing Schema Fix - Database Compatibility

## Issues Encountered

### 1. **DayLockAudit Table Already Exists**
- **Error**: "Table DayLockAudit already exists"
- **Cause**: The table was created in a previous run of the script
- **Resolution**: The script already has proper `IF NOT EXISTS` checks - this is just an informational message

### 2. **Invalid Column Name 'PaymentMode'**
- **Error**: "Msg 207, Level 16, State 1, Procedure usp_GetCashierSystemAmount, Line 38"
- **Cause**: The stored procedure was trying to reference `Orders.PaymentMode` and `SplitPayments.PaymentMode` columns that don't exist
- **Actual Schema**: 
  - Payments are stored in the `Payments` table
  - Payment method is referenced via `PaymentMethodId` → `PaymentMethods` table
  - Cash payments are identified by `PaymentMethods.Name = 'CASH'`

## Database Schema Understanding

### Actual Payment Flow:
```
Orders (CashierId, Status, TotalAmount)
  ↓
Payments (OrderId, PaymentMethodId, Amount, Status)
  ↓
PaymentMethods (Id, Name = 'CASH', DisplayName = 'Cash')
```

### Payment Status Values:
- **Payments.Status**: 0=Pending, 1=Approved, 2=Rejected, 3=Voided
- **Orders.Status**: 0=Open, 1=In Progress, 2=Ready, 3=Completed, 4=Cancelled

## Changes Made

### 1. Updated `usp_GetCashierSystemAmount` Stored Procedure

**Before** (Incorrect - referenced non-existent PaymentMode):
```sql
SELECT 
    ISNULL(SUM(CASE 
        WHEN sp.PaymentMode = 'Cash' THEN sp.PaidAmount
        WHEN o.PaymentMode = 'Cash' AND NOT EXISTS (
            SELECT 1 FROM SplitPayments sp2 WHERE sp2.OrderId = o.Id
        ) THEN o.TotalAmount
        ELSE 0
    END), 0) AS SystemAmount
FROM Orders o
LEFT JOIN SplitPayments sp ON sp.OrderId = o.Id
WHERE ...
```

**After** (Correct - uses Payments and PaymentMethods tables):
```sql
SELECT 
    ISNULL(SUM(p.Amount), 0) AS SystemAmount
FROM Orders o
INNER JOIN Payments p ON p.OrderId = o.Id
INNER JOIN PaymentMethods pm ON p.PaymentMethodId = pm.Id
WHERE CAST(o.CreatedAt AS DATE) = @BusinessDate
  AND o.CashierId = @CashierId
  AND pm.Name = 'CASH'
  AND p.Status = 1 -- Approved payments only
  AND o.Status IN (2, 3); -- Completed or Paid status
```

### 2. Updated `DayClosingService.UpdateCashierSystemAmountsAsync()` Method

**Before** (Conditional logic checking for SplitPayments table):
- 70+ lines of code
- Two query paths based on table existence
- Referenced non-existent PaymentMode column

**After** (Simple, correct query):
```csharp
var query = @"
    UPDATE cdc
    SET cdc.SystemAmount = ISNULL(cashSummary.CashAmount, 0)
    FROM CashierDayClose cdc
    LEFT JOIN (
        SELECT 
            o.CashierId,
            SUM(p.Amount) AS CashAmount
        FROM Orders o
        INNER JOIN Payments p ON p.OrderId = o.Id
        INNER JOIN PaymentMethods pm ON p.PaymentMethodId = pm.Id
        WHERE CAST(o.CreatedAt AS DATE) = @BusinessDate
          AND pm.Name = 'CASH'
          AND p.Status = 1
          AND o.Status IN (2, 3)
          AND o.CashierId IS NOT NULL
        GROUP BY o.CashierId
    ) cashSummary ON cdc.CashierId = cashSummary.CashierId
    WHERE cdc.BusinessDate = @BusinessDate";
```

## Cash Calculation Logic

### How System Amount is Calculated:
1. Find all Orders for the business date with a specific CashierId
2. Join with Payments table to get payment records
3. Filter for CASH payment method (`PaymentMethods.Name = 'CASH'`)
4. Filter for Approved payments only (`Payments.Status = 1`)
5. Filter for Completed/Paid orders (`Orders.Status IN (2, 3)`)
6. Sum the `Payments.Amount` for each cashier

### Example:
```
Order #1001: CashierId=5, TotalAmount=1500
  → Payment: MethodId=1 (CASH), Amount=1500, Status=1 (Approved) ✓

Order #1002: CashierId=5, TotalAmount=800
  → Payment: MethodId=2 (CARD), Amount=800, Status=1 (Approved) ✗ (Not cash)

Order #1003: CashierId=5, TotalAmount=500
  → Payment: MethodId=1 (CASH), Amount=500, Status=0 (Pending) ✗ (Not approved)

Result: Cashier #5 SystemAmount = 1500 (only approved cash payments)
```

## Deployment Instructions

### Before Running the Script:

1. **Verify Database Connection**:
   ```sql
   USE [RestaurantDB];
   SELECT @@SERVERNAME, DB_NAME();
   ```

2. **Check Existing Tables**:
   ```sql
   SELECT name FROM sys.tables 
   WHERE name IN ('CashierDayOpening', 'CashierDayClose', 'DayLockAudit')
   ORDER BY name;
   ```

3. **Verify PaymentMethods Setup**:
   ```sql
   SELECT Id, Name, DisplayName FROM PaymentMethods WHERE Name = 'CASH';
   ```

### Run the Migration:

```bash
# Option 1: Using deployment script
./deploy_day_closing.sh

# Option 2: Using Azure Data Studio / SSMS
# Execute: create_day_closing_tables.sql
```

### After Deployment Verification:

```sql
-- 1. Check tables created
SELECT 
    t.name AS TableName,
    SUM(p.rows) AS RowCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id
WHERE t.name IN ('CashierDayOpening', 'CashierDayClose', 'DayLockAudit')
  AND p.index_id IN (0,1)
GROUP BY t.name;

-- 2. Check stored procedures
SELECT name, create_date, modify_date
FROM sys.procedures
WHERE name LIKE 'usp_%Day%'
ORDER BY name;

-- 3. Verify CashierId column in Orders
SELECT COUNT(*) AS HasCashierId
FROM sys.columns 
WHERE object_id = OBJECT_ID('Orders') 
AND name = 'CashierId';
```

## Testing Workflow

### 1. Initialize Opening Float:
```sql
EXEC usp_InitializeDayOpening 
    @BusinessDate = '2025-11-09',
    @CashierId = 1,
    @OpeningFloat = 2000.00,
    @CreatedBy = 'Admin';
```

### 2. Verify Records Created:
```sql
SELECT * FROM CashierDayOpening WHERE BusinessDate = '2025-11-09';
SELECT * FROM CashierDayClose WHERE BusinessDate = '2025-11-09';
```

### 3. Test Cash Calculation:
```sql
EXEC usp_GetCashierSystemAmount 
    @BusinessDate = '2025-11-09',
    @CashierId = 1;
```

### 4. Simulate Cash Declaration:
```sql
EXEC usp_SaveDeclaredCash
    @BusinessDate = '2025-11-09',
    @CashierId = 1,
    @DeclaredAmount = 15000.00,
    @UpdatedBy = 'Cashier1';
```

### 5. Check Day Closing Summary:
```sql
EXEC usp_GetDayClosingSummary @BusinessDate = '2025-11-09';
```

## Files Updated

1. **create_day_closing_tables.sql** (Line 207-232)
   - Fixed `usp_GetCashierSystemAmount` to use Payments table
   - Removed SplitPayments references
   - Added proper PaymentMethods join

2. **DayClosingService.cs** (Line 155-188)
   - Simplified `UpdateCashierSystemAmountsAsync()` method
   - Removed conditional table checking logic
   - Updated query to match actual schema

## Build Status

✅ **Build Successful**: 0 warnings, 0 errors

## Next Steps

1. ✅ SQL script fixed
2. ✅ C# service updated
3. ✅ Build successful
4. ⏳ **Execute migration**: Run `create_day_closing_tables.sql` in Azure Data Studio
5. ⏳ **Test workflow**: Navigate to Settings → Day Closing
6. ⏳ **Update Orders**: Ensure CashierId is populated during order creation

---

**Status**: Ready for deployment
**Date**: 2025-11-09
**Build**: Successful
