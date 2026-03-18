#!/bin/bash

# =============================================================================
# Backfill RestaurantSettings for existing branches
# Copies main branch settings to all branches that have no settings row.
# Run once after deploying the branch auto-copy feature.
# =============================================================================

echo "============================================================"
echo "Backfill RestaurantSettings for Existing Branches"
echo "============================================================"
echo ""

SQL_FILE="RestaurantManagementSystem/RestaurantManagementSystem/SQL/backfill_settings_for_existing_branches.sql"
SERVER="198.38.81.123,1433"
DATABASE="RestaurantDB"

echo "Target Database: $DATABASE @ $SERVER"
echo ""

if [ ! -f "$SQL_FILE" ]; then
    echo "ERROR: SQL file '$SQL_FILE' not found!"
    exit 1
fi

echo "SQL File: $SQL_FILE"
echo ""
echo "⚠️  IMPORTANT: Please ensure you have:"
echo "   1. Backed up the database"
echo "   2. The BranchId column exists in dbo.RestaurantSettings"
echo "      (run add_branchid_to_settings_mail_upi.sql first if not)"
echo "   3. At least one settings row with a main branch assigned"
echo ""
read -p "Continue with backfill? (yes/no): " confirm

if [ "$confirm" != "yes" ]; then
    echo "Backfill cancelled."
    exit 0
fi

echo ""
echo "Please enter database credentials:"
read -p "Username: " USERNAME
read -sp "Password: " PASSWORD
echo ""
echo ""

echo "Executing backfill..."
echo "============================================================"

sqlcmd -S "$SERVER" -d "$DATABASE" -U "$USERNAME" -P "$PASSWORD" \
    -i "$SQL_FILE" \
    -o "backfill_settings_branches.log"

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Backfill completed successfully!"
    echo ""
    echo "Log saved to: backfill_settings_branches.log"
    echo ""
    echo "Next Steps:"
    echo "  1. Review the verification table printed at the end of the log"
    echo "  2. Check that HasSettings = 'Yes' for every branch"
    echo "  3. Each branch can now fine-tune its settings via Settings > Edit Settings"
    echo ""
else
    echo ""
    echo "❌ Backfill failed! Check backfill_settings_branches.log for errors."
    echo ""
    exit 1
fi

echo "============================================================"
echo "Completed at: $(date)"
echo "============================================================"
