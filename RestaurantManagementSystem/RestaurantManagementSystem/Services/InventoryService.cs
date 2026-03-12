using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace RestaurantManagementSystem.Services
{
    public class InventoryService
    {
        private readonly string _connectionString;

        public InventoryService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public Task EnsureInventorySchemaAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Deducts BOM-based stock from the branch's main godown when an order item is added/updated.
        /// Returns false (with stockError) if NegativeStockAllowed=false and an ingredient is out of stock.
        /// Returns true with stockAlerts populated when stock is low but NegativeStock is allowed.
        /// </summary>
        public bool ApplySaleQuantityDelta(
            SqlConnection connection,
            SqlTransaction transaction,
            int menuItemId,
            int quantityDelta,
            int orderId,
            int userId,
            out string stockError,
            out List<string> stockAlerts)
        {
            stockError = string.Empty;
            stockAlerts = new List<string>();

            // Only deduct on positive qty increases; negative delta = cancelled items (reverse not implemented here)
            if (quantityDelta <= 0) return true;

            try
            {
                // 1. Get BranchId from order
                int branchId = 0;
                using (var cmd = new SqlCommand(
                    @"SELECT ISNULL(BranchId, 0) FROM dbo.Orders WHERE Id = @OrderId",
                    connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@OrderId", orderId);
                    var val = cmd.ExecuteScalar();
                    branchId = (val != null && val != DBNull.Value) ? Convert.ToInt32(val) : 0;
                }
                if (branchId <= 0) return true; // No branch context – skip

                // 2. Check InventoryParameters for this branch
                bool negativeStockAllowed = false;
                bool autoConsumptionOnSale = false;
                using (var cmd = new SqlCommand(@"
                    IF OBJECT_ID('dbo.InventoryParameters','U') IS NOT NULL
                    BEGIN
                        SELECT
                            ISNULL(NegativeStockAllowed, 0),
                            ISNULL(AutoConsumptionOnSale, 0)
                        FROM dbo.InventoryParameters
                        WHERE BranchId = @BranchId
                    END
                    ELSE
                        SELECT CAST(0 AS bit), CAST(0 AS bit)", connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@BranchId", branchId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            negativeStockAllowed = !reader.IsDBNull(0) && Convert.ToBoolean(reader.GetValue(0));
                            autoConsumptionOnSale = !reader.IsDBNull(1) && Convert.ToBoolean(reader.GetValue(1));
                        }
                    }
                }
                // If auto consumption is not enabled for this branch, skip silently
                if (!autoConsumptionOnSale) return true;

                // 3. Get main godown for branch
                int mainGodownId = 0;
                using (var cmd = new SqlCommand(@"
                    IF OBJECT_ID('dbo.Godowns','U') IS NOT NULL
                    BEGIN
                        SELECT TOP 1 Id FROM dbo.Godowns
                        WHERE BranchId = @BranchId AND IsMainGodown = 1 AND IsActive = 1
                        ORDER BY Id
                    END
                    ELSE SELECT CAST(NULL AS INT)", connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@BranchId", branchId);
                    var val = cmd.ExecuteScalar();
                    mainGodownId = (val != null && val != DBNull.Value) ? Convert.ToInt32(val) : 0;
                }
                if (mainGodownId <= 0) return true; // No godown configured – skip

                // 4. Get menu item name (for messages)
                string menuItemName = $"Item #{menuItemId}";
                using (var cmd = new SqlCommand(
                    "SELECT TOP 1 Name FROM dbo.MenuItems WHERE Id = @Id",
                    connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@Id", menuItemId);
                    var val = cmd.ExecuteScalar();
                    if (val != null && val != DBNull.Value) menuItemName = val.ToString();
                }

                // 5. Get BOM from MenuItemIngredients
                // NOTE: BOM Quantity is in Recipe UOM (ml, grams, pcs).
                // PurchaseToRecipeFactor = how many recipe units per 1 purchase unit (e.g. 1000 ml per 1 LTR).
                // Stock is tracked in Purchase UOM, so: stockDeduction = BomQty / PurchaseToRecipeFactor
                var bomLines = new List<(int IngredientId, string IngredientName, decimal BomQty, decimal ConvFactor)>();
                using (var cmd = new SqlCommand(@"
                    IF OBJECT_ID('dbo.MenuItemIngredients','U') IS NOT NULL
                    BEGIN
                        SELECT
                            mii.IngredientId,
                            ISNULL(i.IngredientsName, 'Ingredient #' + CAST(mii.IngredientId AS NVARCHAR)),
                            ISNULL(mii.Quantity, 0),
                            ISNULL(NULLIF(i.PurchaseToRecipeFactor, 0), 1.0)
                        FROM dbo.MenuItemIngredients mii
                        INNER JOIN dbo.Ingredients i ON i.Id = mii.IngredientId
                        WHERE mii.MenuItemId = @MenuItemId
                          AND ISNULL(i.IsActive, 1) = 1
                    END", connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@MenuItemId", menuItemId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var ingId = reader.GetInt32(0);
                            var ingName = reader.IsDBNull(1) ? $"Ingredient #{ingId}" : reader.GetString(1);
                            var qty = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2);
                            var factor = reader.IsDBNull(3) ? 1m : reader.GetDecimal(3);
                            if (qty > 0) bomLines.Add((ingId, ingName, qty, factor));
                        }
                    }
                }
                if (!bomLines.Any()) return true; // No BOM for this item – skip

                // 6. Check current stock and collect deduction plan
                var deductions = new List<(int StockId, int IngredientId, string IngredientName, decimal QtyNeeded, decimal CurrentBalance, decimal AvgCost)>();
                bool hasBlocker = false;

                foreach (var bom in bomLines)
                {
                    // Convert from Recipe UOM to Purchase UOM: divide by PurchaseToRecipeFactor
                    // Example: 40 ml oil / 1000 (ml per LTR) = 0.04 LTR deducted from stock
                    decimal qtyNeeded = (bom.BomQty * quantityDelta) / bom.ConvFactor;
                    if (qtyNeeded <= 0) continue;

                    int stockId = 0;
                    decimal balanceQty = 0m;
                    decimal avgCost = 0m;

                    using (var cmd = new SqlCommand(@"
                        IF OBJECT_ID('dbo.CurrentStock','U') IS NOT NULL
                        BEGIN
                            SELECT StockId, ISNULL(BalanceQty, 0), ISNULL(AverageCost, 0)
                            FROM dbo.CurrentStock
                            WHERE ItemId = @ItemId AND GodownId = @GodownId AND BranchId = @BranchId
                        END", connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@ItemId", bom.IngredientId);
                        cmd.Parameters.AddWithValue("@GodownId", mainGodownId);
                        cmd.Parameters.AddWithValue("@BranchId", branchId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                stockId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                                balanceQty = reader.IsDBNull(1) ? 0m : reader.GetDecimal(1);
                                avgCost = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2);
                            }
                        }
                    }

                    if (balanceQty < qtyNeeded)
                    {
                        if (!negativeStockAllowed && balanceQty <= 0)
                        {
                            stockError = $"Cannot add '{menuItemName}' – '{bom.IngredientName}' is out of stock (available: {balanceQty:F3}, required: {qtyNeeded:F3}).";
                            hasBlocker = true;
                            break;
                        }
                        // Stock is low but sale is allowed – add warning
                        stockAlerts.Add($"Low stock alert: '{bom.IngredientName}' for '{menuItemName}' – available {balanceQty:F3}, required {qtyNeeded:F3}.");
                    }

                    deductions.Add((stockId, bom.IngredientId, bom.IngredientName, qtyNeeded, balanceQty, avgCost));
                }

                if (hasBlocker) return false;

                // 7. Get order number once for ledger entries
                string orderNumber = string.Empty;
                using (var cmd = new SqlCommand(
                    "SELECT TOP 1 ISNULL(OrderNumber, '') FROM dbo.Orders WHERE Id = @Id",
                    connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@Id", orderId);
                    var val = cmd.ExecuteScalar();
                    if (val != null && val != DBNull.Value) orderNumber = val.ToString();
                }

                // 8. Apply deductions – update CurrentStock + insert StockLedger
                foreach (var d in deductions)
                {
                    decimal newBalance = d.CurrentBalance - d.QtyNeeded;
                    decimal totalValue = newBalance * d.AvgCost;

                    if (d.StockId > 0)
                    {
                        // Update existing CurrentStock row
                        using (var cmd = new SqlCommand(@"
                            UPDATE dbo.CurrentStock
                            SET BalanceQty  = @BalanceQty,
                                StockValue  = @StockValue,
                                LastUpdated = GETDATE()
                            WHERE StockId = @StockId", connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@BalanceQty", newBalance);
                            cmd.Parameters.AddWithValue("@StockValue", totalValue < 0 ? 0m : totalValue);
                            cmd.Parameters.AddWithValue("@StockId", d.StockId);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        // Insert new CurrentStock row (ingredient never tracked before)
                        using (var cmd = new SqlCommand(@"
                            IF OBJECT_ID('dbo.CurrentStock','U') IS NOT NULL
                            BEGIN
                                INSERT INTO dbo.CurrentStock
                                    (BranchId, GodownId, ItemId, BalanceQty, AverageCost, StockValue, LastUpdated)
                                VALUES
                                    (@BranchId, @GodownId, @ItemId, @BalanceQty, 0, 0, GETDATE())
                            END", connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@BranchId", branchId);
                            cmd.Parameters.AddWithValue("@GodownId", mainGodownId);
                            cmd.Parameters.AddWithValue("@ItemId", d.IngredientId);
                            cmd.Parameters.AddWithValue("@BalanceQty", newBalance);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    // Insert StockLedger entry
                    // NOTE: TotalValue is a computed column — do NOT include it in INSERT
                    using (var cmd = new SqlCommand(@"
                        IF OBJECT_ID('dbo.StockLedger','U') IS NOT NULL
                        BEGIN
                            INSERT INTO dbo.StockLedger
                            (BranchId, GodownId, ItemId, TransactionDate, TransactionType,
                             ReferenceType, ReferenceId, ReferenceNumber,
                             InQuantity, OutQuantity, UnitCost,
                             BalanceQty, BalanceValue, AverageCost,
                             Remarks, CreatedAt, CreatedBy)
                            VALUES
                            (@BranchId, @GodownId, @ItemId, GETDATE(), 'SaleConsumption',
                             'Order', @OrderId, @OrderNumber,
                             0, @OutQty, @UnitCost,
                             @BalanceQty, @BalanceQty * @UnitCost, @UnitCost,
                             @Remarks, GETDATE(), @UserId)
                        END", connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@BranchId", branchId);
                        cmd.Parameters.AddWithValue("@GodownId", mainGodownId);
                        cmd.Parameters.AddWithValue("@ItemId", d.IngredientId);
                        cmd.Parameters.AddWithValue("@OrderId", orderId);
                        cmd.Parameters.AddWithValue("@OrderNumber", orderNumber);
                        cmd.Parameters.AddWithValue("@OutQty", d.QtyNeeded);
                        cmd.Parameters.AddWithValue("@UnitCost", d.AvgCost);
                        cmd.Parameters.AddWithValue("@BalanceQty", newBalance);
                        cmd.Parameters.AddWithValue("@Remarks", $"Sale consumption: {menuItemName}");
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.ExecuteNonQuery();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                // Non-fatal: log but don't block the sale
                System.Diagnostics.Debug.WriteLine($"InventoryService.ApplySaleQuantityDelta error: {ex.Message}");
                stockError = string.Empty;
                stockAlerts = new List<string>();
                return true;
            }
        }

        /// <summary>
        /// Checks stock availability for a menu item without any deduction.
        /// Returns whether the item can be sold, along with low-stock and out-of-stock warnings.
        /// </summary>
        public (bool canSell, List<string> warnings, List<string> blockedIngredients) CheckStockForMenuItem(int menuItemId, int quantity, int branchId)
        {
            var warnings = new List<string>();
            var blockedIngredients = new List<string>();

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    connection.Open();

                    // Check InventoryParameters
                    bool negativeStockAllowed = false;
                    bool autoConsumptionOnSale = false;
                    using (var cmd = new SqlCommand(@"
                        IF OBJECT_ID('dbo.InventoryParameters','U') IS NOT NULL
                        BEGIN
                            SELECT ISNULL(NegativeStockAllowed, 0), ISNULL(AutoConsumptionOnSale, 0)
                            FROM dbo.InventoryParameters WHERE BranchId = @BranchId
                        END ELSE SELECT CAST(0 AS bit), CAST(0 AS bit)", connection))
                    {
                        cmd.Parameters.AddWithValue("@BranchId", branchId);
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                negativeStockAllowed = !r.IsDBNull(0) && Convert.ToBoolean(r.GetValue(0));
                                autoConsumptionOnSale = !r.IsDBNull(1) && Convert.ToBoolean(r.GetValue(1));
                            }
                        }
                    }
                    if (!autoConsumptionOnSale) return (true, warnings, blockedIngredients);

                    // Get main godown
                    int mainGodownId = 0;
                    using (var cmd = new SqlCommand(@"
                        IF OBJECT_ID('dbo.Godowns','U') IS NOT NULL
                        BEGIN SELECT TOP 1 Id FROM dbo.Godowns WHERE BranchId=@BranchId AND IsMainGodown=1 AND IsActive=1 ORDER BY Id END
                        ELSE SELECT CAST(NULL AS INT)", connection))
                    {
                        cmd.Parameters.AddWithValue("@BranchId", branchId);
                        var v = cmd.ExecuteScalar();
                        mainGodownId = (v != null && v != DBNull.Value) ? Convert.ToInt32(v) : 0;
                    }
                    if (mainGodownId <= 0) return (true, warnings, blockedIngredients);

                    // Get menu item name
                    string menuItemName = $"Item #{menuItemId}";
                    using (var cmd = new SqlCommand("SELECT TOP 1 Name FROM dbo.MenuItems WHERE Id=@Id", connection))
                    {
                        cmd.Parameters.AddWithValue("@Id", menuItemId);
                        var v = cmd.ExecuteScalar();
                        if (v != null && v != DBNull.Value) menuItemName = v.ToString();
                    }

                    // Get BOM
                    using (var cmd = new SqlCommand(@"
                        IF OBJECT_ID('dbo.MenuItemIngredients','U') IS NOT NULL
                        BEGIN
                            SELECT mii.IngredientId,
                                   ISNULL(i.IngredientsName,'Ingredient #'+CAST(mii.IngredientId AS NVARCHAR)),
                                   ISNULL(mii.Quantity,0),
                                   ISNULL(i.PurchaseToRecipeFactor,1.0),
                                   ISNULL(cs.BalanceQty,0)
                            FROM dbo.MenuItemIngredients mii
                            INNER JOIN dbo.Ingredients i ON i.Id=mii.IngredientId
                            LEFT JOIN dbo.CurrentStock cs ON cs.ItemId=mii.IngredientId AND cs.BranchId=@BranchId AND cs.GodownId=@GodownId
                            WHERE mii.MenuItemId=@MenuItemId AND ISNULL(i.IsActive,1)=1 AND ISNULL(mii.Quantity,0)>0
                        END", connection))
                    {
                        cmd.Parameters.AddWithValue("@MenuItemId", menuItemId);
                        cmd.Parameters.AddWithValue("@BranchId", branchId);
                        cmd.Parameters.AddWithValue("@GodownId", mainGodownId);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var ingName = r.IsDBNull(1) ? "Unknown" : r.GetString(1);
                                var bomQty = r.IsDBNull(2) ? 0m : r.GetDecimal(2);
                                var factor = r.IsDBNull(3) ? 1m : r.GetDecimal(3);
                                var balance = r.IsDBNull(4) ? 0m : r.GetDecimal(4);
                                // Convert from Recipe UOM to Purchase UOM by dividing by PurchaseToRecipeFactor
                                decimal needed = (bomQty * quantity) / (factor <= 0 ? 1m : factor);
                                if (needed <= 0) continue;
                                if (balance <= 0 && !negativeStockAllowed)
                                    blockedIngredients.Add($"'{ingName}' is out of stock (available: {balance:F3})");
                                else if (balance < needed)
                                    warnings.Add($"Low stock: '{ingName}' – available {balance:F3}, required {needed:F3}");
                            }
                        }
                    }
                }
            }
            catch { /* non-fatal */ }

            bool canSell = blockedIngredients.Count == 0;
            return (canSell, warnings, blockedIngredients);
        }
    }
}
