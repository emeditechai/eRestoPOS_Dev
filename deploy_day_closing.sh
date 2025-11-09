#!/bin/bash

# =================================================================
# Day Closing Process - Deployment Script
# This script executes the SQL migration on the production database
# =================================================================

echo "=============================================="
echo "Day Closing Process - Database Migration"
echo "=============================================="
echo ""

# Configuration
SQL_FILE="create_day_closing_tables.sql"
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

# Execute SQL script using sqlcmd
# Note: Requires sqlcmd to be installed
# Install on macOS: brew install sqlcmd
# Install on Linux: apt-get install mssql-tools

sqlcmd -S "$SERVER" -d "$DATABASE" -U "$USERNAME" -P "$PASSWORD" -i "$SQL_FILE" -o "migration_output.log"

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Migration completed successfully!"
    echo ""
    echo "Migration log saved to: migration_output.log"
    echo ""
    echo "Next Steps:"
    echo "1. Verify tables created: CashierDayOpening, CashierDayClose, DayLockAudit"
    echo "2. Verify stored procedures created successfully"
    echo "3. Run the application and navigate to Settings > Day Closing"
    echo "4. Test the workflow: Open Float → Declare Cash → Lock Day"
    echo ""
else
    echo ""
    echo "❌ Migration failed! Check migration_output.log for errors."
    echo ""
    exit 1
fi

echo "=============================================="
echo "Migration completed at: $(date)"
echo "=============================================="
