# Day Closing Process - Implementation Guide

## 📋 Overview
The Day Closing Process is a comprehensive cashier cash reconciliation and day locking feature for the Restaurant Management System. It ensures accurate cash handling, variance tracking, and end-of-day closure with audit trails.

## 🎯 Business Process Flow

### Step 1: Open Day Float Entry
- **Actor:** Manager/Admin
- **Action:** Assign opening float per cashier (e.g., ₹2,000, ₹3,000)
- **System:** Creates record in `CashierDayOpening` and `CashierDayClose` tables

### Step 2: Sales Processing
- **Actor:** Cashier
- **Action:** Handle bills, receive cash throughout the day
- **System:** Updates `Orders` table with `CashierId` and payment details

### Step 3: Declared Cash Entry
- **Actor:** Cashier
- **Action:** Count physical cash and enter amount with denomination breakdown
- **System:** Updates `CashierDayClose.DeclaredAmount` and calculates variance

### Step 4: Variance Calculation
- **System Action:** Automatically calculates: `Variance = (Declared + Opening Float) - System Cash`
- **Auto-flagging:** Variances > ₹100 require manager approval

### Step 5: Manager Approval (if needed)
- **Actor:** Manager
- **Action:** Review variance, add comments, approve or reject
- **System:** Updates `ApprovedBy` and `ApprovalComment` fields

### Step 6: Day Lock
- **Actor:** Admin/Manager
- **Action:** Lock the business day after all cashiers reconciled
- **System:** 
  - Updates all `CashierDayClose` records to `LOCKED` status
  - Inserts audit record into `DayLockAudit` table
  - Prevents further modifications

### Step 7: EOD Report Generation
- **System Action:** Generate comprehensive report with:
  - Sales summary (total orders, sales by payment mode)
  - Cashier closing details with variances
  - Lock status and approval comments
  - Summary statistics

## 🗃️ Database Schema

### Tables Created

#### 1. CashierDayOpening
```sql
- Id (PK)
- BusinessDate
- CashierId (FK → Users)
- CashierName
- OpeningFloat (decimal)
- CreatedBy
- CreatedAt
- UpdatedBy
- UpdatedAt
- IsActive
```

#### 2. CashierDayClose
```sql
- Id (PK)
- BusinessDate
- CashierId (FK → Users)
- CashierName
- SystemAmount (calculated from Orders)
- DeclaredAmount (entered by cashier)
- OpeningFloat
- Variance (calculated)
- Status (PENDING/OK/CHECK/LOCKED)
- ApprovedBy
- ApprovalComment
- LockedFlag
- LockedAt
- LockedBy
- CreatedBy/UpdatedBy timestamps
```

#### 3. DayLockAudit
```sql
- LockId (PK)
- BusinessDate
- LockedBy
- LockTime
- Remarks
- Status (LOCKED/REOPENED)
- ReopenedBy
- ReopenedAt
- ReopenReason
```

### Stored Procedures Created

1. **usp_InitializeDayOpening** - Create/update opening float
2. **usp_GetCashierSystemAmount** - Calculate cash from Orders
3. **usp_SaveDeclaredCash** - Update declared cash and calculate variance
4. **usp_LockDay** - Lock day after validation
5. **usp_GetDayClosingSummary** - Retrieve dashboard data

## 🚀 Installation Steps

### 1. Database Migration
```bash
# Navigate to project root
cd /Users/abhikporel/dev/Restaurantapp

# Run deployment script
./deploy_day_closing.sh

# Or manually execute SQL
# Use SQL Server Management Studio or Azure Data Studio
# Execute: create_day_closing_tables.sql
```

### 2. Application Already Updated
The following components are already implemented:
- ✅ Models: `DayClosingModels.cs`, `DayClosingViewModels.cs`
- ✅ Service: `DayClosingService.cs` (registered in Program.cs)
- ✅ Controller: `DayClosingController.cs`
- ✅ Views: Index, OpenFloat, DeclareCash, ApproveVariance, EODReport
- ✅ Navigation: Menu item added under Settings

### 3. Verify Installation
1. Run application: `dotnet run`
2. Navigate to: **Settings → Day Closing**
3. You should see the Day Closing Dashboard

## 📱 User Guide

### For Managers (Opening Float)
1. Navigate to **Settings → Day Closing**
2. Click **"Open Float for Cashier"**
3. Select cashier from dropdown
4. Enter opening float amount (e.g., ₹2,000)
5. Click **"Initialize Opening Float"**

### For Cashiers (Declare Cash)
1. Navigate to **Settings → Day Closing**
2. Click **"Declare"** button for your name
3. Enter denomination breakdown:
   - ₹2000 notes, ₹500 notes, ₹200 notes, etc.
   - System calculates total automatically
4. Or enter total declared amount directly
5. Review variance preview
6. Click **"Submit Cash Declaration"**

### For Managers (Approve Variance)
1. If variance > ₹100, status shows "CHECK"
2. Click **"Approve"** button next to cashier name
3. Review variance details
4. Enter approval comment (required)
5. Choose:
   - **Approve Variance** - Accept the variance
   - **Reject & Request Re-verification** - Cashier must recount

### For Admins (Lock Day)
1. Ensure all cashiers have:
   - Declared cash (no PENDING status)
   - Approved variances (no CHECK status)
2. Scroll to "Lock Day" section
3. Enter optional remarks
4. Click **"Lock Day & Finalize"**
5. Confirm the lock operation

### View EOD Report
1. Click **"View EOD Report"** button
2. Review comprehensive report with:
   - Sales summary
   - Cashier details
   - Variances and approvals
   - Lock status
3. Click **"Print Report"** for physical copy

## 🔒 Security & Authorization

### Role-Based Access Control
- **Open Float:** Admin, Manager only
- **Declare Cash:** All authenticated users (cashiers)
- **Approve Variance:** Admin, Manager only
- **Lock Day:** Admin, Manager only
- **EOD Report:** Admin, Manager only

### Safety Features
- ✅ Locked days cannot be modified
- ✅ Variance threshold alerts (> ₹100)
- ✅ Critical variance warnings (> ₹500)
- ✅ Confirmation prompts for lock operations
- ✅ Audit trail for all lock operations
- ✅ Manager approval required for large variances

## 📊 Reports & Insights

### Dashboard Metrics
- **Pending Count:** Cashiers who haven't declared cash
- **OK Count:** Cashiers with acceptable variance (< ₹100)
- **Check Count:** Cashiers requiring approval (variance > ₹100)
- **Total Variance:** Overall cash over/short for the day

### EOD Report Contents
1. **Sales Summary:**
   - Total orders and customers
   - Total sales amount
   - Sales by payment mode (Cash/Card/UPI)

2. **Cashier Details:**
   - Opening float
   - System calculated cash
   - Expected cash (opening + system)
   - Declared cash
   - Variance (over/short)
   - Approval status and comments

3. **Lock Information:**
   - Locked by (user)
   - Lock timestamp
   - Lock remarks

## ⚙️ Configuration

### Variance Threshold
Current threshold: **₹100**

To modify, update in stored procedure:
```sql
-- In usp_SaveDeclaredCash
IF ABS(@Variance) > 100  -- Change this value
    SET @Status = 'CHECK';
```

And in controller:
```csharp
// DayClosingController.cs - DeclareCash action
if (Math.Abs(result.Variance) > 100)  // Change this value
```

### CashierId in Orders
The system requires `CashierId` column in `Orders` table. This is automatically added by the migration script. 

**Important:** Ensure your order creation process populates this field:
```csharp
// In OrderController or wherever orders are created
order.CashierId = currentUserId; // Set to logged-in cashier
```

## 🐛 Troubleshooting

### Issue: "No cashier opening records found"
**Solution:** Manager must initialize opening float first using "Open Float for Cashier" button

### Issue: "Cannot lock day" message
**Reasons:**
1. Some cashiers haven't declared cash (PENDING status)
2. Some variances need approval (CHECK status)

**Solution:** Complete all declarations and approve variances before locking

### Issue: System amount shows ₹0.00
**Reasons:**
1. Orders don't have CashierId populated
2. No completed orders for that day

**Solution:** 
1. Ensure CashierId is set when creating orders
2. Click "Refresh System Amounts" button to recalculate

### Issue: Variance calculation seems wrong
**Check:**
1. Opening float correctly entered
2. System amount matches actual cash sales
3. Declared amount entered correctly

**Formula:** Variance = (Declared + Opening) - System

### Issue: Cannot see "Day Closing" menu
**Solution:** 
1. Ensure you're logged in
2. Check if service is registered in Program.cs
3. Clear browser cache and reload

## 📝 Best Practices

### Daily Operations
1. **Morning:** Manager initializes opening float for all cashiers
2. **Throughout Day:** Cashiers process sales (system tracks automatically)
3. **End of Day:** Cashiers count cash and declare amounts
4. **Manager Review:** Approve any variances > ₹100
5. **Final Step:** Admin locks the day
6. **Record Keeping:** Print and file EOD report

### Variance Management
- Small variances (< ₹50): Generally acceptable, auto-approved
- Medium variances (₹50-₹500): Require manager review and comment
- Large variances (> ₹500): Require thorough investigation before approval

### Audit Trail
- All lock operations are logged in `DayLockAudit`
- All approvals are logged with manager name and comment
- Reports can be regenerated anytime for historical dates

## 🔄 Future Enhancements

Potential features for future versions:
- [ ] Denomination templates per cashier
- [ ] SMS/Email alerts for large variances
- [ ] Multi-currency support
- [ ] Automated variance investigation workflows
- [ ] Integration with accounting systems
- [ ] Mobile app for cashier declaration
- [ ] Biometric approval for lock operations
- [ ] Real-time variance monitoring dashboard

## 📞 Support

For issues or questions:
1. Check this documentation
2. Review the Troubleshooting section
3. Contact system administrator
4. Refer to application logs for technical issues

---

**Version:** 1.0  
**Last Updated:** November 9, 2025  
**Author:** Restaurant Management System Development Team  
**Status:** Production Ready ✅
