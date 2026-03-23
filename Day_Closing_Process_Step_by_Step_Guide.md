# Day Closing Process - Complete Step-by-Step Guide

## 📋 Overview

The Day Closing Process helps restaurant managers track and reconcile cash collected by each cashier at the end of the day. This guide provides a simplified, easy-to-follow workflow.

---

## 🎯 Key Concepts

### What is Day Closing?
- **Opening Float**: Starting cash given to cashier at beginning of day (e.g., ₹2000 for making change)
- **System Amount**: Total cash sales collected by cashier (automatically calculated from POS)
- **Expected Cash**: Opening Float + System Amount = What cashier should have
- **Declared Amount**: Actual cash counted by cashier at end of day
- **Variance**: Difference between Expected Cash and Declared Amount
  - **Cash Over**: Cashier has more than expected (positive variance)
  - **Cash Short**: Cashier has less than expected (negative variance)

### Variance Rules
- ✅ **Variance ≤ ₹100**: Automatically approved (Status: OK)
- ⚠️ **Variance > ₹100**: Requires manager approval (Status: CHECK)

---

## 📝 Step-by-Step Workflow

### **STEP 1: Database Setup (ONE-TIME ONLY)**

Before using Day Closing feature, execute the database migration:

1. Open **Azure Data Studio** or **SQL Server Management Studio**
2. Connect to: `198.38.81.123,1433`
   - Database: `dev_Restaurant`
   - User: `sa`
   - Password: `Ehospit@lity@#1926`
3. Open file: `create_day_closing_tables.sql`
4. Execute the script
5. Verify tables created:
   ```sql
   SELECT name FROM sys.tables 
   WHERE name IN ('CashierDayOpening', 'CashierDayClose', 'DayLockAudit')
   ORDER BY name;
   ```
   Expected: 3 tables returned

---

### **STEP 2: Assign Opening Float (Manager - Start of Day)**

**When**: Beginning of business day (e.g., 9:00 AM)  
**Who**: Restaurant Manager/Administrator

1. Login to application
2. Navigate: **Settings → Day Closing**
3. Click **"Open Float for Cashier"** button
4. Select cashier from dropdown
5. Enter opening float amount (e.g., ₹2000)
6. Click **Submit**

**What Happens**:
- System creates opening record for cashier
- Status: **PENDING** (waiting for cash declaration)
- Opening Float: ₹2000
- System Amount: ₹0.00 (will update as sales happen)

---

### **STEP 3: Process Sales During the Day (Cashier)**

**When**: Throughout business day  
**Who**: Cashier

**IMPORTANT**: Orders must be linked to cashier for tracking!

**Current Issue**: The system is NOT tracking which cashier processes each order.

**Solution Required**: 
- Update order creation flow to capture `CashierId`
- When order is created/paid, set: `Orders.CashierId = [Current Logged-in User Id]`

**Temporary Workaround** (for testing):
```sql
-- Manually assign existing orders to cashiers for testing
UPDATE Orders 
SET CashierId = 2  -- Replace with actual cashier user ID
WHERE CAST(CreatedAt AS DATE) = '2025-11-09'
  AND CashierId IS NULL;
```

---

### **STEP 4: Refresh System Amounts (Manager - During/End of Day)**

**When**: Anytime during day or before cash declaration  
**Who**: Manager/Administrator

1. Go to **Day Closing** dashboard
2. Click **"Refresh System Amounts"** button

**What Happens**:
- System calculates total CASH sales for each cashier
- Updates "System ₹" column
- Formula: Sum of all cash payments where:
  - `Orders.CashierId` = cashier
  - `PaymentMethods.Name` = 'CASH'
  - `Payments.Status` = 1 (Approved)
  - `Orders.Status` IN (2, 3) (Completed/Paid)

**Expected Result**: 
- System ₹ column shows actual cash collected
- Expected ₹ = Opening Float + System ₹

---

### **STEP 5: Declare Cash (Cashier - End of Day)**

**When**: End of business day (e.g., 10:00 PM)  
**Who**: Cashier

1. Count all physical cash in register
2. Go to **Day Closing** dashboard
3. Click **"Declare"** button for your name
4. Enter denomination breakdown:
   - ₹2000 notes: [count]
   - ₹500 notes: [count]
   - ₹200 notes: [count]
   - ₹100 notes: [count]
   - ₹50 notes: [count]
   - ₹20 notes: [count]
   - ₹10 notes/coins: [count]
   - Coins: [total amount]
5. Verify **Total Amount** matches your count
6. Click **Submit**

**What Happens**:
- System calculates: Variance = (Declared + Opening) - System
- If variance ≤ ₹100: Status = **OK** ✅
- If variance > ₹100: Status = **CHECK** ⚠️ (needs approval)

---

### **STEP 6: Approve Variance (Manager - If Needed)**

**When**: After cashier declares cash (only if variance > ₹100)  
**Who**: Manager/Administrator

1. Review cashiers with Status: **CHECK**
2. Click **"Approve"** button
3. Review variance details:
   - Opening Float
   - System Amount  
   - Expected Cash
   - Declared Cash
   - Variance amount
4. Enter approval comment (optional)
5. Click **Approve** or **Reject**

**If Approved**: Status changes to **OK**  
**If Rejected**: Cashier must recount and re-declare

---

### **STEP 7: Lock the Day (Manager - End of Day)**

**When**: After ALL cashiers declared and approved  
**Who**: Manager/Administrator

**Pre-requisites**:
- All cashiers must have Status: **OK**
- No cashier should have Status: **PENDING** or **CHECK**

1. Verify all statuses are **OK**
2. Click **"Lock Day"** button
3. Enter remarks (optional)
4. Confirm lock

**What Happens**:
- All cashier records locked (Status: **LOCKED**)
- Day lock audit entry created
- No further changes allowed for this business date

---

### **STEP 8: Generate EOD Report (Manager)**

**When**: After day is locked  
**Who**: Manager/Administrator/Accountant

1. Click **"View EOD Report"** button
2. Review report sections:
   - **Sales Summary**: Total orders, sales, cash breakdown
   - **Cashier Details**: Each cashier's opening, system, declared, variance
   - **Summary Statistics**: Total float, system amount, variance
3. Click **Print** button to print/save PDF

---

## 🔧 Troubleshooting

### Issue: System ₹ showing ₹0.00 even with sales

**Cause**: Orders not linked to cashier (`Orders.CashierId` is NULL)

**Solution**:
1. Check orders:
   ```sql
   SELECT Id, OrderNumber, CashierId, TotalAmount, CreatedAt 
   FROM Orders 
   WHERE CAST(CreatedAt AS DATE) = '2025-11-09'
   ORDER BY CreatedAt DESC;
   ```

2. If CashierId is NULL, need to:
   - **Option A**: Update order creation code to capture cashier
   - **Option B**: Manually assign for testing:
     ```sql
     UPDATE Orders SET CashierId = [YourCashierUserId]
     WHERE CAST(CreatedAt AS DATE) = '2025-11-09';
     ```

3. Click "Refresh System Amounts" button

### Issue: Cannot lock day

**Error**: "Cannot lock day: X cashier(s) have unresolved variances"

**Solution**: 
- Review cashiers with Status: **CHECK**
- Manager must approve/reject each variance
- All statuses must be **OK** before locking

### Issue: Variance calculation seems wrong

**Formula Check**:
```
Expected Cash = Opening Float + System Amount
Variance = Declared Amount - Expected Cash

Example:
Opening Float: ₹2000
System Amount: ₹15000 (cash sales)
Expected Cash: ₹2000 + ₹15000 = ₹17000
Declared Amount: ₹16900 (counted by cashier)
Variance: ₹16900 - ₹17000 = -₹100 (Short)
```

---

## 📊 Quick Reference Table

| Step | Action | Who | When | Status Change |
|------|--------|-----|------|---------------|
| 1 | Assign Opening Float | Manager | Start of Day | → PENDING |
| 2 | Process Sales | Cashier | Throughout Day | PENDING |
| 3 | Refresh System Amounts | Manager | Anytime | PENDING |
| 4 | Declare Cash | Cashier | End of Day | → OK or CHECK |
| 5 | Approve Variance | Manager | If variance > ₹100 | CHECK → OK |
| 6 | Lock Day | Manager | End of Day | OK → LOCKED |
| 7 | View EOD Report | Manager | After Lock | LOCKED |

---

## 🎓 Example Scenario

**Cashier: Purojit**  
**Date: 2025-11-09**

1. **9:00 AM** - Manager assigns ₹2000 opening float
   - Opening Float: ₹2000
   - System: ₹0
   - Status: PENDING

2. **Throughout Day** - Purojit processes 50 orders
   - 30 orders paid CASH (₹15000)
   - 20 orders paid CARD (₹12000)

3. **8:00 PM** - Manager clicks "Refresh System Amounts"
   - Opening Float: ₹2000
   - System: ₹15000 (cash sales only)
   - Expected: ₹17000
   - Status: PENDING

4. **9:30 PM** - Purojit counts cash and declares
   - Counted cash: ₹17050
   - Declared: ₹17050
   - Variance: ₹17050 - ₹17000 = +₹50 (Over)
   - Status: OK ✅ (variance ≤ ₹100)

5. **10:00 PM** - Manager locks the day
   - All cashiers: OK
   - Day Status: LOCKED

6. **10:05 PM** - Manager prints EOD report
   - Total Cash Sales: ₹15000
   - Total Variance: +₹50 Over

---

## 💡 Best Practices

1. **Assign opening float at start of shift** - Don't wait until end of day
2. **Refresh system amounts before declaration** - Ensures accurate expected cash
3. **Count cash carefully** - Use denomination calculator for accuracy
4. **Document large variances** - Manager should add comments when approving
5. **Lock day promptly** - Don't leave previous day unlocked
6. **Print EOD reports** - Keep physical records for accounting
7. **Update order flow** - Ensure CashierId is captured on every order

---

## 🚨 Critical Next Step

**YOU MUST FIX**: Orders are not tracking which cashier processed them!

**Update Required**: Modify order creation/payment code to set `Orders.CashierId`

**Location to Check**:
- `OrderController.cs` - CreateOrder/CompleteOrder methods
- Look for: Order creation or payment completion
- Add: `order.CashierId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value)`

Without this fix, System Amount will ALWAYS show ₹0.00!

---

## 📞 Support

If you encounter issues:
1. Check this guide first
2. Verify database migration executed
3. Confirm CashierId is being set on orders
4. Use "Refresh System Amounts" button
5. Check variance calculation manually

---

**Document Version**: 1.0  
**Last Updated**: 2025-11-09  
**Author**: Restaurant Management System
