#!/bin/bash

# ==================================================
# DAY CLOSING - ONE-CLICK INSTALLER
# Just run this file and everything will be set up!
# ==================================================

echo "========================================"
echo "DAY CLOSING INSTALLER"
echo "========================================"
echo ""

# Database connection details
DB_SERVER="198.38.81.123,1433"
DB_NAME="dev_Restaurant"
DB_USER="sa"
DB_PASS="asdf@1234"

# Check if sqlcmd is available
if ! command -v sqlcmd &> /dev/null; then
    echo "❌ sqlcmd not found!"
    echo ""
    echo "MANUAL INSTALLATION REQUIRED:"
    echo "1. Open Azure Data Studio or SQL Server Management Studio"
    echo "2. Connect to: $DB_SERVER"
    echo "3. Database: $DB_NAME"
    echo "4. Username: $DB_USER"
    echo "5. Password: $DB_PASS"
    echo "6. Open file: EASY_DAY_CLOSING_SETUP.sql"
    echo "7. Click Execute (F5)"
    echo ""
    exit 1
fi

echo "Installing Day Closing system..."
echo ""

# Execute the setup script
sqlcmd -S "$DB_SERVER" -d "$DB_NAME" -U "$DB_USER" -P "$DB_PASS" -i EASY_DAY_CLOSING_SETUP.sql

if [ $? -eq 0 ]; then
    echo ""
    echo "========================================"
    echo "✓✓✓ INSTALLATION SUCCESSFUL! ✓✓✓"
    echo "========================================"
    echo ""
    echo "NEXT STEPS:"
    echo "1. Open your restaurant app"
    echo "2. Login as Administrator/Manager"
    echo "3. Go to: Settings → Day Closing"
    echo "4. Click 'Open Float for Cashier'"
    echo "5. Enter opening cash amount"
    echo "6. Start using!"
    echo ""
    echo "📖 Read DAY_CLOSING_SIMPLE_GUIDE.md for detailed instructions"
    echo ""
else
    echo ""
    echo "❌ Installation failed!"
    echo "Please run EASY_DAY_CLOSING_SETUP.sql manually in Azure Data Studio"
    echo ""
fi
