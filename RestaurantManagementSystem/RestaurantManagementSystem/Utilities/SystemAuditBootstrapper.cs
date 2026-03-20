using System;
using Microsoft.Data.SqlClient;

namespace RestaurantManagementSystem.Utilities
{
    // Runtime detection helper only.
    // Trigger creation/deployment must happen via SQL/create_system_audit_triggers.sql,
    // not during application startup.
    public static class SystemAuditBootstrapper
    {
        private const string MenuItemsTriggerName = "trg_SystemAudit_MenuItems";
        private const string IngredientsTriggerName = "trg_SystemAudit_Ingredients";
        private const string UomTriggerName = "trg_SystemAudit_UomMaster";
        private const string UsersTriggerName = "trg_SystemAudit_Users";
        private const string BranchesTriggerName = "trg_SystemAudit_Branches";

        public static bool HasTriggerBasedAuditForModule(SqlConnection connection, string? module)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            var triggerName = ResolveTriggerName(module);
            if (string.IsNullOrWhiteSpace(triggerName))
            {
                return false;
            }

            return HasTrigger(connection, triggerName);
        }

        private static bool HasTrigger(SqlConnection connection, string triggerName)
        {
            using var cmd = new SqlCommand("SELECT COUNT(1) FROM sys.triggers WHERE name = @TriggerName", connection);
            cmd.Parameters.AddWithValue("@TriggerName", triggerName);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        private static string? ResolveTriggerName(string? module)
        {
            return module?.Trim() switch
            {
                "MenuItem" => MenuItemsTriggerName,
                "MenuItemRate" => MenuItemsTriggerName,
                "Ingredient" => IngredientsTriggerName,
                "UOM" => UomTriggerName,
                "User" => UsersTriggerName,
                "Branch" => BranchesTriggerName,
                _ => null
            };
        }
    }
}