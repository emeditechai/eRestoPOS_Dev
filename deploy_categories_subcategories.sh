#!/bin/bash

# ==============================================================================
# Script: deploy_categories_subcategories.sh
# Description: Executes Master Categories & Sub-Categories Seed Script for RMS
# ==============================================================================

echo "================================================================================"
echo "  Deploying Master Menu & BAR Categories & Sub-Categories to Database"
echo "================================================================================"
echo ""

SQL_FILE="SQL/upload_all_standard_categories_subcategories.sql"

if [ ! -f "$SQL_FILE" ]; then
    SQL_FILE="upload_all_standard_categories_subcategories.sql"
fi

if [ ! -f "$SQL_FILE" ]; then
    echo "ERROR: Could not find SQL script '$SQL_FILE'."
    exit 1
fi

echo "Target SQL Script: $SQL_FILE"
echo ""

# Check if sqlcmd is available
if command -v sqlcmd &> /dev/null; then
    echo "sqlcmd found. Please enter database connection parameters if required, or run manually in SSMS/Azure Data Studio."
    echo ""
    read -p "Enter SQL Server Host/IP [e.g. 198.38.81.123,1433 or localhost]: " DB_SERVER
    read -p "Enter Database Name [e.g. RestaurantManagementDB]: " DB_NAME
    read -p "Enter DB User [e.g. sa]: " DB_USER
    read -s -p "Enter DB Password: " DB_PASSWORD
    echo ""
    echo ""

    echo "Executing $SQL_FILE..."
    sqlcmd -S "$DB_SERVER" -d "$DB_NAME" -U "$DB_USER" -P "$DB_PASSWORD" -i "$SQL_FILE"

    if [ $? -eq 0 ]; then
        echo ""
        echo "✓ Categories & Sub-Categories successfully seeded!"
    else
        echo ""
        echo "✗ Execution failed. Please verify credentials or run the script in SQL Server Management Studio (SSMS)."
    fi
else
    echo "NOTE: sqlcmd is not detected in your PATH."
    echo "You can open and execute '$SQL_FILE' directly in:"
    echo "  - SQL Server Management Studio (SSMS)"
    echo "  - Azure Data Studio"
    echo "  - VS Code MSSQL Extension"
    echo ""
fi
