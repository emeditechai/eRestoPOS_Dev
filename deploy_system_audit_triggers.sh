#!/bin/bash

# System Audit Trigger Deployment Script
# Deploys SystemAuditLog table and trigger-based audit capture

set -e

echo "========================================="
echo "System Audit Trigger Deployment"
echo "========================================="
echo ""

SQL_FILE="RestaurantManagementSystem/RestaurantManagementSystem/SQL/create_system_audit_triggers.sql"

if [ ! -f "$SQL_FILE" ]; then
    echo "✗ SQL file not found: $SQL_FILE"
    exit 1
fi

DB_SERVER="localhost"
DB_NAME="RestaurantDB"
DB_USER="sa"

echo "Enter SQL Server password for user '$DB_USER':"
read -s DB_PASSWORD

echo ""
echo "Deploying system audit triggers..."
echo ""

sqlcmd -S "$DB_SERVER" -d "$DB_NAME" -U "$DB_USER" -P "$DB_PASSWORD" -i "$SQL_FILE"

if [ $? -eq 0 ]; then
    echo ""
    echo "✓ System audit triggers deployed successfully"
    echo ""
    echo "Applied objects:"
    echo "  • dbo.SystemAuditLog"
    echo "  • dbo.trg_SystemAudit_MenuItems"
    echo "  • dbo.trg_SystemAudit_Ingredients"
    echo "  • dbo.trg_SystemAudit_UomMaster"
    echo "  • dbo.trg_SystemAudit_Users"
    echo "  • dbo.trg_SystemAudit_Branches"
    echo ""
else
    echo ""
    echo "✗ Deployment failed"
    exit 1
fi
