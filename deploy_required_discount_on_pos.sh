#!/bin/bash

# =================================================================
# Required Discount on POS - Database Migration Script
# Adds IsRequiredDiscountOnPOS column to dbo.RestaurantSettings
# =================================================================

echo "=============================================="
echo "Required Discount on POS - Database Migration"
echo "=============================================="
echo ""

# Configuration
SQL_FILE="RestaurantManagementSystem/RestaurantManagementSystem/SQL/add_required_discount_on_pos_to_settings.sql"
SERVER="198.38.81.123,1433"
DATABASE="RestaurantDB"

echo "Target Database: $DATABASE @ $SERVER"
echo ""

# Check if SQL file exists
if [ ! -f "$SQL_FILE" ]; then
    echo "ERROR: SQL file '$SQL_FILE' not found!"
    exit 1
fi

echo "SQL File: $SQL_FILE"
echo ""
echo "⚠️  IMPORTANT: Please ensure you have:"
echo "   1. Backed up the database"
echo "   2. Reviewed the SQL script"
echo "   3. Have proper permissions"
echo ""
read -p "Continue with migration? (yes/no): " confirm

if [ "$confirm" != "yes" ]; then
    echo "Migration cancelled."
    exit 0
fi

echo ""
echo "Please enter database credentials:"
read -p "Username: " USERNAME
read -sp "Password: " PASSWORD
echo ""
echo ""

echo "Executing SQL migration..."
echo "=============================================="

sqlcmd -S "$SERVER" -d "$DATABASE" -U "$USERNAME" -P "$PASSWORD" \
    -i "$SQL_FILE" \
    -o "migration_required_discount_on_pos.log"

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Migration completed successfully!"
    echo ""
    echo "Migration log saved to: migration_required_discount_on_pos.log"
    echo ""
    echo "Next Steps:"
    echo "  1. Deploy the updated application build to production"
    echo "  2. Navigate to Settings → Edit Settings"
    echo "  3. Verify 'Required Discount on POS' toggle appears in Parameter Setup"
    echo "  4. Navigate to Settings (view) and confirm the new row in Parameters list"
    echo ""
else
    echo ""
    echo "❌ Migration failed! Check migration_required_discount_on_pos.log for errors."
    echo ""
    exit 1
fi

echo "=============================================="
echo "Migration completed at: $(date)"
echo "=============================================="
