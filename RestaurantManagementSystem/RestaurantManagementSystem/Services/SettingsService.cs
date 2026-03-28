using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using RestaurantManagementSystem.Data;
using RestaurantManagementSystem.Models;
using Microsoft.Data.SqlClient;

namespace RestaurantManagementSystem.Services
{
    public class SettingsService
    {
        private readonly RestaurantDbContext _dbContext;
        private readonly string _connectionString;

        public SettingsService(RestaurantDbContext dbContext, string connectionString)
        {
            _dbContext = dbContext;
            _connectionString = connectionString;
        }

        public async Task<RestaurantSettings> GetSettingsAsync(int? branchId)
        {
            try
            {
                // Fallback: read using a null-safe SqlDataReader in case EF mapping hits unexpected NULLs
                var fallback = await ReadSettingsFromSqlAsync(branchId);
                if (fallback != null) return fallback;

                // If still null, create default settings and persist
                var defaultSettings = new RestaurantSettings
                {
                    RestaurantName = "My Restaurant",
                    StreetAddress = "123 Main Street",
                    City = "Mumbai",
                    State = "Maharashtra",
                    Pincode = "400001",
                    Country = "India",
                    GSTCode = "27AAPFU0939F1ZV",
                    PhoneNumber = "+919876543210",
                    Email = "info@myrestaurant.com",
                    Website = "https://www.myrestaurant.com",
                    CurrencySymbol = "₹",
                    DefaultGSTPercentage = 5.00m,
                    TakeAwayGSTPercentage = 5.00m,
                    IsDefaultGSTRequired = true,
                    IsCounterRequired = false,
                    IsTableMarkedAvailableAfterBillCompletion = false,
                    BillFormat = "A4"
                };
                await EnsureSettingsForBranchExistsAsync(branchId, defaultSettings);
                return defaultSettings;
            }
            catch (Exception ex)
            {
                // Don't throw to the controller UI. Attempt to recover by reading via SQL fallback
                try
                {
                    var fallback = await ReadSettingsFromSqlAsync(branchId);
                    if (fallback != null) return fallback;
                }
                catch
                {
                    // swallow
                }

                // Last resort: create default settings and persist
                try
                {
                    var defaultSettings = new RestaurantSettings
                    {
                        RestaurantName = "My Restaurant",
                        StreetAddress = "123 Main Street",
                        City = "Mumbai",
                        State = "Maharashtra",
                        Pincode = "400001",
                        Country = "India",
                        GSTCode = "27AAPFU0939F1ZV",
                        PhoneNumber = "+919876543210",
                        Email = "info@myrestaurant.com",
                        Website = "https://www.myrestaurant.com",
                        CurrencySymbol = "₹",
                        DefaultGSTPercentage = 5.00m,
                        TakeAwayGSTPercentage = 5.00m,
                        IsDefaultGSTRequired = true,
                        IsCounterRequired = false,
                        IsTableMarkedAvailableAfterBillCompletion = false,
                        BillFormat = "A4"
                    };
                    await EnsureSettingsForBranchExistsAsync(branchId, defaultSettings);
                    return defaultSettings;
                }
                catch
                {
                    // As a last fallback, return an in-memory default without persisting
                    return new RestaurantSettings
                    {
                        RestaurantName = "My Restaurant",
                        StreetAddress = "123 Main Street",
                        City = "Mumbai",
                        State = "Maharashtra",
                        Pincode = "400001",
                        Country = "India",
                        GSTCode = "27AAPFU0939F1ZV",
                        CurrencySymbol = "₹",
                        DefaultGSTPercentage = 5.00m,
                        TakeAwayGSTPercentage = 5.00m,
                        IsDefaultGSTRequired = true,
                        IsCounterRequired = false,
                        IsTableMarkedAvailableAfterBillCompletion = false,
                        BillFormat = "A4"
                    };
                }
            }
        }

        // Null-safe read using SqlDataReader when EF fails
        private async Task<RestaurantSettings?> ReadSettingsFromSqlAsync(int? branchId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var hasBranchColumn = await HasColumnAsync(connection, "RestaurantSettings", "BranchId");
                    string sql = hasBranchColumn && branchId.HasValue
                        ? "SELECT TOP 1 * FROM dbo.RestaurantSettings WHERE (BranchId = @BranchId OR BranchId IS NULL) ORDER BY CASE WHEN BranchId = @BranchId THEN 0 ELSE 1 END, Id DESC"
                        : "SELECT TOP 1 * FROM dbo.RestaurantSettings ORDER BY Id DESC";

                    using (var command = new SqlCommand(sql, connection))
                    {
                        if (hasBranchColumn && branchId.HasValue)
                        {
                            command.Parameters.AddWithValue("@BranchId", branchId.Value);
                        }

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var s = new RestaurantSettings();
                                int ord;

                                ord = reader.GetOrdinal("Id");
                                s.Id = reader.IsDBNull(ord) ? 0 : reader.GetInt32(ord);

                                ord = reader.GetOrdinal("RestaurantName");
                                s.RestaurantName = reader.IsDBNull(ord) ? string.Empty : reader.GetString(ord);

                                ord = reader.GetOrdinal("StreetAddress");
                                s.StreetAddress = reader.IsDBNull(ord) ? string.Empty : reader.GetString(ord);

                                ord = reader.GetOrdinal("City");
                                s.City = reader.IsDBNull(ord) ? string.Empty : reader.GetString(ord);

                                ord = reader.GetOrdinal("State");
                                s.State = reader.IsDBNull(ord) ? string.Empty : reader.GetString(ord);

                                ord = reader.GetOrdinal("Pincode");
                                s.Pincode = reader.IsDBNull(ord) ? string.Empty : reader.GetString(ord);

                                ord = reader.GetOrdinal("Country");
                                s.Country = reader.IsDBNull(ord) ? string.Empty : reader.GetString(ord);

                                ord = reader.GetOrdinal("GSTCode");
                                s.GSTCode = reader.IsDBNull(ord) ? string.Empty : reader.GetString(ord);

                                ord = reader.GetOrdinal("PhoneNumber");
                                s.PhoneNumber = reader.IsDBNull(ord) ? null : reader.GetString(ord);

                                ord = reader.GetOrdinal("Email");
                                s.Email = reader.IsDBNull(ord) ? null : reader.GetString(ord);

                                ord = reader.GetOrdinal("Website");
                                s.Website = reader.IsDBNull(ord) ? null : reader.GetString(ord);

                                ord = reader.GetOrdinal("LogoPath");
                                s.LogoPath = reader.IsDBNull(ord) ? null : reader.GetString(ord);

                                    // FSSAI column (new)
                                    if (ColumnExists(reader, "FssaiNo"))
                                    {
                                        ord = reader.GetOrdinal("FssaiNo");
                                        s.FssaiNo = reader.IsDBNull(ord) ? string.Empty : reader.GetString(ord);
                                    }

                                ord = reader.GetOrdinal("CurrencySymbol");
                                s.CurrencySymbol = reader.IsDBNull(ord) ? "₹" : reader.GetString(ord);

                                ord = reader.GetOrdinal("DefaultGSTPercentage");
                                s.DefaultGSTPercentage = reader.IsDBNull(ord) ? 0m : reader.GetDecimal(ord);

                                ord = reader.GetOrdinal("TakeAwayGSTPercentage");
                                s.TakeAwayGSTPercentage = reader.IsDBNull(ord) ? 0m : reader.GetDecimal(ord);

                                // BarGSTPerc column (new)
                                if (ColumnExists(reader, "BarGSTPerc"))
                                {
                                    ord = reader.GetOrdinal("BarGSTPerc");
                                    s.BarGSTPerc = reader.IsDBNull(ord) ? 5.00m : reader.GetDecimal(ord);
                                }

                                // SelectedOrderType column (CSV of IDs)
                                if (ColumnExists(reader, "SelectedOrderType"))
                                {
                                    ord = reader.GetOrdinal("SelectedOrderType");
                                    s.SelectedOrderType = reader.IsDBNull(ord) ? string.Empty : reader.GetString(ord);
                                }

                                ord = reader.GetOrdinal("IsDefaultGSTRequired");
                                s.IsDefaultGSTRequired = !reader.IsDBNull(ord) && reader.GetBoolean(ord);

                                ord = reader.GetOrdinal("IsTakeAwayGSTRequired");
                                s.IsTakeAwayGSTRequired = !reader.IsDBNull(ord) && reader.GetBoolean(ord);

                                // New column
                                if (ColumnExists(reader, "Is_TakeawayIncludedGST_Req"))
                                {
                                    ord = reader.GetOrdinal("Is_TakeawayIncludedGST_Req");
                                    s.IsTakeawayIncludedGSTReq = !reader.IsDBNull(ord) && reader.GetBoolean(ord);
                                }

                                ord = reader.GetOrdinal("IsDiscountApprovalRequired");
                                s.IsDiscountApprovalRequired = !reader.IsDBNull(ord) && reader.GetBoolean(ord);

                                ord = reader.GetOrdinal("IsCardPaymentApprovalRequired");
                                s.IsCardPaymentApprovalRequired = !reader.IsDBNull(ord) && reader.GetBoolean(ord);

                                // New KOT print flag
                                if (ColumnExists(reader, "IsKOTBillPrintRequired"))
                                {
                                    ord = reader.GetOrdinal("IsKOTBillPrintRequired");
                                    s.IsKOTBillPrintRequired = !reader.IsDBNull(ord) && reader.GetBoolean(ord);
                                }

                                // Counter Required flag
                                if (ColumnExists(reader, "IsCounterRequired"))
                                {
                                    ord = reader.GetOrdinal("IsCounterRequired");
                                    s.IsCounterRequired = !reader.IsDBNull(ord) && reader.GetBoolean(ord);
                                }

                                // New Auto Send Bill Email flag
                                if (ColumnExists(reader, "isReqAutoSentbillEmail"))
                                {
                                    ord = reader.GetOrdinal("isReqAutoSentbillEmail");
                                    s.IsReqAutoSentbillEmail = !reader.IsDBNull(ord) && reader.GetBoolean(ord);
                                }

                                if (ColumnExists(reader, "IsTableMarkedAvailableAfterBillCompletion"))
                                {
                                    ord = reader.GetOrdinal("IsTableMarkedAvailableAfterBillCompletion");
                                    s.IsTableMarkedAvailableAfterBillCompletion = !reader.IsDBNull(ord) && reader.GetBoolean(ord);
                                }

                                // Required Discount on POS flag
                                if (ColumnExists(reader, "IsRequiredDiscountOnPOS"))
                                {
                                    ord = reader.GetOrdinal("IsRequiredDiscountOnPOS");
                                    s.IsRequiredDiscountOnPOS = !reader.IsDBNull(ord) && reader.GetBoolean(ord);
                                }

                                // Restrict Menu Edit from Non-Main Branch flag
                                if (ColumnExists(reader, "IsRestrictMenuEditNonMainBranch"))
                                {
                                    ord = reader.GetOrdinal("IsRestrictMenuEditNonMainBranch");
                                    s.IsRestrictMenuEditNonMainBranch = !reader.IsDBNull(ord) && reader.GetBoolean(ord);
                                }

                                ord = reader.GetOrdinal("BillFormat");
                                s.BillFormat = reader.IsDBNull(ord) ? "A4" : reader.GetString(ord);

                                ord = reader.GetOrdinal("CreatedAt");
                                s.CreatedAt = reader.IsDBNull(ord) ? DateTime.Now : reader.GetDateTime(ord);

                                ord = reader.GetOrdinal("UpdatedAt");
                                s.UpdatedAt = reader.IsDBNull(ord) ? DateTime.Now : reader.GetDateTime(ord);

                                return s;
                            }
                        }
                    }
                }
            }
            catch
            {
                // If anything goes wrong, return null to allow caller to create defaults
            }
            return null;
        }

        // Ensure the RestaurantSettings table exists. Returns true if the table was created by this call.
        public async Task<bool> EnsureSettingsTableExistsAsync()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var checkTable = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'RestaurantSettings'", connection);

                var exists = (int)await checkTable.ExecuteScalarAsync() > 0;
                if (exists)
                {
                    // Ensure columns exist and return false (not created now)
                    await EnsureParameterColumnsExistAsync();
                    return false;
                }

                var createSql = @"
CREATE TABLE [dbo].[RestaurantSettings](
    [Id] INT IDENTITY(1,1) PRIMARY KEY,
    [RestaurantName] NVARCHAR(200) NOT NULL,
    [StreetAddress] NVARCHAR(500) NULL,
    [City] NVARCHAR(100) NULL,
    [State] NVARCHAR(100) NULL,
    [Pincode] NVARCHAR(20) NULL,
    [Country] NVARCHAR(100) NULL,
    [GSTCode] NVARCHAR(50) NULL,
    [PhoneNumber] NVARCHAR(50) NULL,
    [Email] NVARCHAR(200) NULL,
    [Website] NVARCHAR(200) NULL,
    [LogoPath] NVARCHAR(500) NULL,
    [CurrencySymbol] NVARCHAR(50) NOT NULL DEFAULT N'₹',
    [DefaultGSTPercentage] DECIMAL(5,2) NOT NULL DEFAULT 5.00,
    [TakeAwayGSTPercentage] DECIMAL(5,2) NOT NULL DEFAULT 5.00,
    [BarGSTPerc] DECIMAL(5,2) NOT NULL DEFAULT 5.00,
    [SelectedOrderType] NVARCHAR(200) NULL,
    [IsDefaultGSTRequired] BIT NOT NULL DEFAULT 1,
    [IsTakeAwayGSTRequired] BIT NOT NULL DEFAULT 1,
    [Is_TakeawayIncludedGST_Req] BIT NOT NULL DEFAULT 0,
    [IsDiscountApprovalRequired] BIT NOT NULL DEFAULT 0,
    [IsCardPaymentApprovalRequired] BIT NOT NULL DEFAULT 0,
    [IsCounterRequired] BIT NOT NULL DEFAULT 0,
    [IsTableMarkedAvailableAfterBillCompletion] BIT NOT NULL DEFAULT 0,
    [IsRequiredDiscountOnPOS] BIT NOT NULL DEFAULT 0,
    [IsRestrictMenuEditNonMainBranch] BIT NOT NULL DEFAULT 0,
    [BillFormat] NVARCHAR(10) NOT NULL DEFAULT N'A4',
    [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
    [UpdatedAt] DATETIME NOT NULL DEFAULT GETDATE()
);";

                using (var createCmd = new SqlCommand(createSql, connection))
                {
                    await createCmd.ExecuteNonQueryAsync();
                }

                // Insert a default row
                var insertSql = @"
INSERT INTO dbo.RestaurantSettings (
    RestaurantName, StreetAddress, City, State, Pincode, Country, GSTCode, PhoneNumber, Email, Website, LogoPath, CurrencySymbol, DefaultGSTPercentage, TakeAwayGSTPercentage, BarGSTPerc, SelectedOrderType, IsDefaultGSTRequired, IsTakeAwayGSTRequired, Is_TakeawayIncludedGST_Req, IsDiscountApprovalRequired, IsCardPaymentApprovalRequired, IsCounterRequired, IsTableMarkedAvailableAfterBillCompletion, IsRequiredDiscountOnPOS, IsRestrictMenuEditNonMainBranch, BillFormat, CreatedAt, UpdatedAt
) VALUES (
    @RestaurantName, @StreetAddress, @City, @State, @Pincode, @Country, @GSTCode, @PhoneNumber, @Email, @Website, @LogoPath, @CurrencySymbol, @DefaultGSTPercentage, @TakeAwayGSTPercentage, @BarGSTPerc, @SelectedOrderType, @IsDefaultGSTRequired, @IsTakeAwayGSTRequired, @IsTakeawayIncludedGSTReq, @IsDiscountApprovalRequired, @IsCardPaymentApprovalRequired, @IsCounterRequired, @IsTableMarkedAvailableAfterBillCompletion, @IsRequiredDiscountOnPOS, @IsRestrictMenuEditNonMainBranch, @BillFormat, GETDATE(), GETDATE()
);";

                using (var cmd = new SqlCommand(insertSql, connection))
                {
                    cmd.Parameters.AddWithValue("@RestaurantName", "My Restaurant");
                    cmd.Parameters.AddWithValue("@StreetAddress", "123 Main Street");
                    cmd.Parameters.AddWithValue("@City", "Mumbai");
                    cmd.Parameters.AddWithValue("@State", "Maharashtra");
                    cmd.Parameters.AddWithValue("@Pincode", "400001");
                    cmd.Parameters.AddWithValue("@Country", "India");
                    cmd.Parameters.AddWithValue("@GSTCode", "27AAPFU0939F1ZV");
                    cmd.Parameters.AddWithValue("@PhoneNumber", "+919876543210");
                    cmd.Parameters.AddWithValue("@Email", "info@myrestaurant.com");
                    cmd.Parameters.AddWithValue("@Website", "https://www.myrestaurant.com");
                    cmd.Parameters.AddWithValue("@LogoPath", string.Empty);
                    cmd.Parameters.AddWithValue("@CurrencySymbol", "₹");
                    cmd.Parameters.AddWithValue("@DefaultGSTPercentage", 5.00m);
                    cmd.Parameters.AddWithValue("@TakeAwayGSTPercentage", 5.00m);
                    cmd.Parameters.AddWithValue("@BarGSTPerc", 5.00m);
                    cmd.Parameters.AddWithValue("@SelectedOrderType", string.Empty);
                    cmd.Parameters.AddWithValue("@IsDefaultGSTRequired", true);
                    cmd.Parameters.AddWithValue("@IsTakeAwayGSTRequired", true);
                    cmd.Parameters.AddWithValue("@IsTakeawayIncludedGSTReq", false);
                    cmd.Parameters.AddWithValue("@IsDiscountApprovalRequired", false);
                    cmd.Parameters.AddWithValue("@IsCardPaymentApprovalRequired", false);
                    cmd.Parameters.AddWithValue("@IsCounterRequired", false);
                    cmd.Parameters.AddWithValue("@IsTableMarkedAvailableAfterBillCompletion", false);
                    cmd.Parameters.AddWithValue("@IsRequiredDiscountOnPOS", false);
                    cmd.Parameters.AddWithValue("@IsRestrictMenuEditNonMainBranch", false);
                    cmd.Parameters.AddWithValue("@BillFormat", "A4");

                    await cmd.ExecuteNonQueryAsync();
                }

                // Ensure any additional parameter columns exist
                await EnsureParameterColumnsExistAsync();

                return true;
            }
        }

        private bool ColumnExists(SqlDataReader reader, string columnName)
        {
            try
            {
                return reader.GetOrdinal(columnName) >= 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UpdateSettingsAsync(RestaurantSettings settings, int? branchId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var hasBranchColumn = await HasColumnAsync(connection, "RestaurantSettings", "BranchId");
                    var upsertSql = hasBranchColumn
                        ? @"
IF EXISTS (SELECT 1 FROM dbo.RestaurantSettings WHERE BranchId = @BranchId)
BEGIN
    UPDATE dbo.RestaurantSettings
    SET RestaurantName = @RestaurantName,
        StreetAddress = @StreetAddress,
        City = @City,
        State = @State,
        Pincode = @Pincode,
        Country = @Country,
        GSTCode = @GSTCode,
        PhoneNumber = @PhoneNumber,
        Email = @Email,
        Website = @Website,
        LogoPath = @LogoPath,
        CurrencySymbol = @CurrencySymbol,
        DefaultGSTPercentage = @DefaultGSTPercentage,
        TakeAwayGSTPercentage = @TakeAwayGSTPercentage,
        BarGSTPerc = @BarGSTPerc,
        SelectedOrderType = @SelectedOrderType,
        IsDefaultGSTRequired = @IsDefaultGSTRequired,
        IsTakeAwayGSTRequired = @IsTakeAwayGSTRequired,
        Is_TakeawayIncludedGST_Req = @IsTakeawayIncludedGSTReq,
        IsDiscountApprovalRequired = @IsDiscountApprovalRequired,
        IsCardPaymentApprovalRequired = @IsCardPaymentApprovalRequired,
        IsKOTBillPrintRequired = @IsKOTBillPrintRequired,
        IsCounterRequired = @IsCounterRequired,
        isReqAutoSentbillEmail = @IsReqAutoSentbillEmail,
        IsTableMarkedAvailableAfterBillCompletion = @IsTableMarkedAvailableAfterBillCompletion,
        IsRequiredDiscountOnPOS = @IsRequiredDiscountOnPOS,
        IsRestrictMenuEditNonMainBranch = @IsRestrictMenuEditNonMainBranch,
        BillFormat = @BillFormat,
        FssaiNo = @FssaiNo,
        UpdatedAt = GETDATE()
    WHERE BranchId = @BranchId;
END
ELSE
BEGIN
    INSERT INTO dbo.RestaurantSettings (
        BranchId, RestaurantName, StreetAddress, City, State, Pincode, Country, GSTCode, PhoneNumber, Email, Website, LogoPath, CurrencySymbol, DefaultGSTPercentage, TakeAwayGSTPercentage, BarGSTPerc, SelectedOrderType, IsDefaultGSTRequired, IsTakeAwayGSTRequired, Is_TakeawayIncludedGST_Req, IsDiscountApprovalRequired, IsCardPaymentApprovalRequired, IsKOTBillPrintRequired, IsCounterRequired, isReqAutoSentbillEmail, IsTableMarkedAvailableAfterBillCompletion, IsRequiredDiscountOnPOS, IsRestrictMenuEditNonMainBranch, BillFormat, FssaiNo, CreatedAt, UpdatedAt
    ) VALUES (
        @BranchId, @RestaurantName, @StreetAddress, @City, @State, @Pincode, @Country, @GSTCode, @PhoneNumber, @Email, @Website, @LogoPath, @CurrencySymbol, @DefaultGSTPercentage, @TakeAwayGSTPercentage, @BarGSTPerc, @SelectedOrderType, @IsDefaultGSTRequired, @IsTakeAwayGSTRequired, @IsTakeawayIncludedGSTReq, @IsDiscountApprovalRequired, @IsCardPaymentApprovalRequired, @IsKOTBillPrintRequired, @IsCounterRequired, @IsReqAutoSentbillEmail, @IsTableMarkedAvailableAfterBillCompletion, @IsRequiredDiscountOnPOS, @IsRestrictMenuEditNonMainBranch, @BillFormat, @FssaiNo, GETDATE(), GETDATE()
    );
END"
                        : @"
IF EXISTS (SELECT 1 FROM dbo.RestaurantSettings)
BEGIN
    UPDATE dbo.RestaurantSettings
    SET RestaurantName = @RestaurantName,
        StreetAddress = @StreetAddress,
        City = @City,
        State = @State,
        Pincode = @Pincode,
        Country = @Country,
        GSTCode = @GSTCode,
        PhoneNumber = @PhoneNumber,
        Email = @Email,
        Website = @Website,
        LogoPath = @LogoPath,
        CurrencySymbol = @CurrencySymbol,
        DefaultGSTPercentage = @DefaultGSTPercentage,
        TakeAwayGSTPercentage = @TakeAwayGSTPercentage,
        BarGSTPerc = @BarGSTPerc,
        SelectedOrderType = @SelectedOrderType,
        IsDefaultGSTRequired = @IsDefaultGSTRequired,
        IsTakeAwayGSTRequired = @IsTakeAwayGSTRequired,
        Is_TakeawayIncludedGST_Req = @IsTakeawayIncludedGSTReq,
        IsDiscountApprovalRequired = @IsDiscountApprovalRequired,
        IsCardPaymentApprovalRequired = @IsCardPaymentApprovalRequired,
        IsKOTBillPrintRequired = @IsKOTBillPrintRequired,
        IsCounterRequired = @IsCounterRequired,
        isReqAutoSentbillEmail = @IsReqAutoSentbillEmail,
        IsTableMarkedAvailableAfterBillCompletion = @IsTableMarkedAvailableAfterBillCompletion,
        IsRequiredDiscountOnPOS = @IsRequiredDiscountOnPOS,
        IsRestrictMenuEditNonMainBranch = @IsRestrictMenuEditNonMainBranch,
        BillFormat = @BillFormat,
        FssaiNo = @FssaiNo,
        UpdatedAt = GETDATE();
END
ELSE
BEGIN
    INSERT INTO dbo.RestaurantSettings (
        RestaurantName, StreetAddress, City, State, Pincode, Country, GSTCode, PhoneNumber, Email, Website, LogoPath, CurrencySymbol, DefaultGSTPercentage, TakeAwayGSTPercentage, BarGSTPerc, SelectedOrderType, IsDefaultGSTRequired, IsTakeAwayGSTRequired, Is_TakeawayIncludedGST_Req, IsDiscountApprovalRequired, IsCardPaymentApprovalRequired, IsKOTBillPrintRequired, IsCounterRequired, isReqAutoSentbillEmail, IsTableMarkedAvailableAfterBillCompletion, IsRequiredDiscountOnPOS, IsRestrictMenuEditNonMainBranch, BillFormat, FssaiNo, CreatedAt, UpdatedAt
    ) VALUES (
        @RestaurantName, @StreetAddress, @City, @State, @Pincode, @Country, @GSTCode, @PhoneNumber, @Email, @Website, @LogoPath, @CurrencySymbol, @DefaultGSTPercentage, @TakeAwayGSTPercentage, @BarGSTPerc, @SelectedOrderType, @IsDefaultGSTRequired, @IsTakeAwayGSTRequired, @IsTakeawayIncludedGSTReq, @IsDiscountApprovalRequired, @IsCardPaymentApprovalRequired, @IsKOTBillPrintRequired, @IsCounterRequired, @IsReqAutoSentbillEmail, @IsTableMarkedAvailableAfterBillCompletion, @IsRequiredDiscountOnPOS, @IsRestrictMenuEditNonMainBranch, @BillFormat, @FssaiNo, GETDATE(), GETDATE()
    );
END";

                    using (var cmd = new SqlCommand(upsertSql, connection))
                    {
                        if (hasBranchColumn && branchId.HasValue)
                        {
                            cmd.Parameters.AddWithValue("@BranchId", branchId.Value);
                        }

                        cmd.Parameters.AddWithValue("@RestaurantName", (object)settings.RestaurantName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@StreetAddress", (object)settings.StreetAddress ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@City", (object)settings.City ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@State", (object)settings.State ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Pincode", (object)settings.Pincode ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Country", (object)settings.Country ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@GSTCode", (object)settings.GSTCode ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@PhoneNumber", (object)settings.PhoneNumber ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Email", (object)settings.Email ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Website", (object)settings.Website ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@LogoPath", (object)settings.LogoPath ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@CurrencySymbol", (object)settings.CurrencySymbol ?? "₹");
                        cmd.Parameters.AddWithValue("@DefaultGSTPercentage", settings.DefaultGSTPercentage);
                        cmd.Parameters.AddWithValue("@TakeAwayGSTPercentage", settings.TakeAwayGSTPercentage);
                        cmd.Parameters.AddWithValue("@BarGSTPerc", settings.BarGSTPerc);
                        cmd.Parameters.AddWithValue("@SelectedOrderType", (object)settings.SelectedOrderType ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@IsDefaultGSTRequired", settings.IsDefaultGSTRequired);
                        cmd.Parameters.AddWithValue("@IsTakeAwayGSTRequired", settings.IsTakeAwayGSTRequired);
                        cmd.Parameters.AddWithValue("@IsTakeawayIncludedGSTReq", settings.IsTakeawayIncludedGSTReq);
                        cmd.Parameters.AddWithValue("@IsDiscountApprovalRequired", settings.IsDiscountApprovalRequired);
                        cmd.Parameters.AddWithValue("@IsCardPaymentApprovalRequired", settings.IsCardPaymentApprovalRequired);
                        cmd.Parameters.AddWithValue("@IsKOTBillPrintRequired", settings.IsKOTBillPrintRequired);
                        cmd.Parameters.AddWithValue("@IsCounterRequired", settings.IsCounterRequired);
                        cmd.Parameters.AddWithValue("@IsReqAutoSentbillEmail", settings.IsReqAutoSentbillEmail);
                        cmd.Parameters.AddWithValue("@IsTableMarkedAvailableAfterBillCompletion", settings.IsTableMarkedAvailableAfterBillCompletion);
                        cmd.Parameters.AddWithValue("@IsRequiredDiscountOnPOS", settings.IsRequiredDiscountOnPOS);
                        cmd.Parameters.AddWithValue("@IsRestrictMenuEditNonMainBranch", settings.IsRestrictMenuEditNonMainBranch);
                        cmd.Parameters.AddWithValue("@BillFormat", (object)settings.BillFormat ?? "A4");
                        cmd.Parameters.AddWithValue("@FssaiNo", (object)settings.FssaiNo ?? DBNull.Value);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task EnsureSettingsForBranchExistsAsync(int? branchId, RestaurantSettings settings)
        {
            await UpdateSettingsAsync(settings, branchId);
        }

        private static async Task<bool> HasColumnAsync(SqlConnection connection, string tableName, string columnName)
        {
            var sql = @"
SELECT COUNT(1)
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo'
  AND TABLE_NAME = @TableName
  AND COLUMN_NAME = @ColumnName";

            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@TableName", tableName);
                cmd.Parameters.AddWithValue("@ColumnName", columnName);
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return count > 0;
            }
        }
        
        private async Task EnsureParameterColumnsExistAsync()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if IsDefaultGSTRequired column exists
                var checkColumn1 = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'RestaurantSettings' 
                    AND COLUMN_NAME = 'IsDefaultGSTRequired'
                    AND TABLE_SCHEMA = 'dbo'", connection);
                    
                var column1Exists = (int)await checkColumn1.ExecuteScalarAsync() > 0;
                
                if (!column1Exists)
                {
                    var addColumn1 = new SqlCommand(@"
                        ALTER TABLE [dbo].[RestaurantSettings] 
                        ADD [IsDefaultGSTRequired] BIT NOT NULL DEFAULT 1", connection);
                    await addColumn1.ExecuteNonQueryAsync();
                }
                
                // Check if IsTakeAwayGSTRequired column exists
                var checkColumn2 = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'RestaurantSettings' 
                    AND COLUMN_NAME = 'IsTakeAwayGSTRequired'
                    AND TABLE_SCHEMA = 'dbo'", connection);
                    
                var column2Exists = (int)await checkColumn2.ExecuteScalarAsync() > 0;
                
                if (!column2Exists)
                {
                    var addColumn2 = new SqlCommand(@"
                        ALTER TABLE [dbo].[RestaurantSettings] 
                        ADD [IsTakeAwayGSTRequired] BIT NOT NULL DEFAULT 1", connection);
                    await addColumn2.ExecuteNonQueryAsync();
                }

                // Check if Is_TakeawayIncludedGST_Req column exists
                var checkColumnTakeawayInclude = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'RestaurantSettings' 
                    AND COLUMN_NAME = 'Is_TakeawayIncludedGST_Req'
                    AND TABLE_SCHEMA = 'dbo'", connection);
                var takeawayIncludeExists = (int)await checkColumnTakeawayInclude.ExecuteScalarAsync() > 0;
                if (!takeawayIncludeExists)
                {
                    var addColumnTakeawayInclude = new SqlCommand(@"
                        ALTER TABLE [dbo].[RestaurantSettings] 
                        ADD [Is_TakeawayIncludedGST_Req] BIT NOT NULL DEFAULT 0", connection);
                    await addColumnTakeawayInclude.ExecuteNonQueryAsync();
                }
                
                // Check if BillFormat column exists
                var checkColumn3 = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'RestaurantSettings' 
                    AND COLUMN_NAME = 'BillFormat'
                    AND TABLE_SCHEMA = 'dbo'", connection);
                    
                var column3Exists = (int)await checkColumn3.ExecuteScalarAsync() > 0;
                
                if (!column3Exists)
                {
                    var addColumn3 = new SqlCommand(@"
                        ALTER TABLE [dbo].[RestaurantSettings] 
                        ADD [BillFormat] NVARCHAR(10) NOT NULL DEFAULT N'A4'", connection);
                    await addColumn3.ExecuteNonQueryAsync();
                }

                // Ensure IsDiscountApprovalRequired column exists
                var checkColumn4 = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'RestaurantSettings' 
                    AND COLUMN_NAME = 'IsDiscountApprovalRequired'
                    AND TABLE_SCHEMA = 'dbo'", connection);
                var column4Exists = (int)await checkColumn4.ExecuteScalarAsync() > 0;
                if (!column4Exists)
                {
                    var addColumn4 = new SqlCommand(@"
                        ALTER TABLE [dbo].[RestaurantSettings] 
                        ADD [IsDiscountApprovalRequired] BIT NOT NULL DEFAULT 0", connection);
                    await addColumn4.ExecuteNonQueryAsync();
                }

                // Ensure IsCardPaymentApprovalRequired column exists
                var checkColumn5 = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'RestaurantSettings' 
                    AND COLUMN_NAME = 'IsCardPaymentApprovalRequired'
                    AND TABLE_SCHEMA = 'dbo'", connection);
                var column5Exists = (int)await checkColumn5.ExecuteScalarAsync() > 0;
                if (!column5Exists)
                {
                    var addColumn5 = new SqlCommand(@"
                        ALTER TABLE [dbo].[RestaurantSettings] 
                        ADD [IsCardPaymentApprovalRequired] BIT NOT NULL DEFAULT 0", connection);
                    await addColumn5.ExecuteNonQueryAsync();
                }

                // Ensure FssaiNo column exists
                var checkFssai = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'RestaurantSettings' 
                    AND COLUMN_NAME = 'FssaiNo'
                    AND TABLE_SCHEMA = 'dbo'", connection);
                var fssaiExists = (int)await checkFssai.ExecuteScalarAsync() > 0;
                if (!fssaiExists)
                {
                    var addFssai = new SqlCommand(@"
                        ALTER TABLE [dbo].[RestaurantSettings]
                        ADD [FssaiNo] NVARCHAR(32) NULL", connection);
                    await addFssai.ExecuteNonQueryAsync();
                }

                // Ensure IsKOTBillPrintRequired column exists
                var checkKOT = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'RestaurantSettings' 
                    AND COLUMN_NAME = 'IsKOTBillPrintRequired'
                    AND TABLE_SCHEMA = 'dbo'", connection);
                var kotExists = (int)await checkKOT.ExecuteScalarAsync() > 0;
                if (!kotExists)
                {
                    var addKOT = new SqlCommand(@"
                        ALTER TABLE [dbo].[RestaurantSettings] 
                        ADD [IsKOTBillPrintRequired] BIT NOT NULL DEFAULT 0", connection);
                    await addKOT.ExecuteNonQueryAsync();
                }

                // Ensure IsCounterRequired column exists
                var checkCounterReq = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'RestaurantSettings'
                    AND COLUMN_NAME = 'IsCounterRequired'
                    AND TABLE_SCHEMA = 'dbo'", connection);
                var counterReqExists = (int)await checkCounterReq.ExecuteScalarAsync() > 0;
                if (!counterReqExists)
                {
                    var addCounterReq = new SqlCommand(@"
                        ALTER TABLE [dbo].[RestaurantSettings]
                        ADD [IsCounterRequired] BIT NOT NULL DEFAULT 0", connection);
                    await addCounterReq.ExecuteNonQueryAsync();
                }

                // Ensure IsTableMarkedAvailableAfterBillCompletion column exists
                var checkTableAvailableAfterBill = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'RestaurantSettings'
                    AND COLUMN_NAME = 'IsTableMarkedAvailableAfterBillCompletion'
                    AND TABLE_SCHEMA = 'dbo'", connection);
                var tableAvailableAfterBillExists = (int)await checkTableAvailableAfterBill.ExecuteScalarAsync() > 0;
                if (!tableAvailableAfterBillExists)
                {
                    var addTableAvailableAfterBill = new SqlCommand(@"
                        ALTER TABLE [dbo].[RestaurantSettings]
                        ADD [IsTableMarkedAvailableAfterBillCompletion] BIT NOT NULL DEFAULT 0", connection);
                    await addTableAvailableAfterBill.ExecuteNonQueryAsync();
                }

                // Ensure BarGSTPerc column exists
                var checkBarGST = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'RestaurantSettings' 
                    AND COLUMN_NAME = 'BarGSTPerc'
                    AND TABLE_SCHEMA = 'dbo'", connection);
                var barGSTExists = (int)await checkBarGST.ExecuteScalarAsync() > 0;
                if (!barGSTExists)
                {
                    var addBarGST = new SqlCommand(@"
                        ALTER TABLE [dbo].[RestaurantSettings] 
                        ADD [BarGSTPerc] DECIMAL(5,2) NOT NULL DEFAULT 5.00", connection);
                    await addBarGST.ExecuteNonQueryAsync();
                }

                // Ensure SelectedOrderType column exists
                var checkSelectedOrderType = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'RestaurantSettings' 
                    AND COLUMN_NAME = 'SelectedOrderType'
                    AND TABLE_SCHEMA = 'dbo'", connection);
                var selectedOrderTypeExists = (int)await checkSelectedOrderType.ExecuteScalarAsync() > 0;
                if (!selectedOrderTypeExists)
                {
                    var addSelectedOrderType = new SqlCommand(@"
                        ALTER TABLE [dbo].[RestaurantSettings]
                        ADD [SelectedOrderType] NVARCHAR(200) NULL", connection);
                    await addSelectedOrderType.ExecuteNonQueryAsync();
                }



                // Ensure IsRestrictMenuEditNonMainBranch column exists
                var checkRestrictMenu = new SqlCommand(@"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'RestaurantSettings' 
                    AND COLUMN_NAME = 'IsRestrictMenuEditNonMainBranch'
                    AND TABLE_SCHEMA = 'dbo'", connection);
                var restrictMenuExists = (int)await checkRestrictMenu.ExecuteScalarAsync() > 0;
                if (!restrictMenuExists)
                {
                    var addRestrictMenu = new SqlCommand(@"
                        ALTER TABLE [dbo].[RestaurantSettings] 
                        ADD [IsRestrictMenuEditNonMainBranch] BIT NOT NULL DEFAULT 0", connection);
                    await addRestrictMenu.ExecuteNonQueryAsync();
                }

                // Normalize existing rows: replace NULLs with sensible defaults to avoid EF materialization errors
                try
                {
                    var normalizeSql = @"
UPDATE dbo.RestaurantSettings
SET IsDefaultGSTRequired = ISNULL(IsDefaultGSTRequired, 1),
    IsTakeAwayGSTRequired = ISNULL(IsTakeAwayGSTRequired, 1),
    Is_TakeawayIncludedGST_Req = ISNULL(Is_TakeawayIncludedGST_Req, 0),
    IsDiscountApprovalRequired = ISNULL(IsDiscountApprovalRequired, 0),
    IsCardPaymentApprovalRequired = ISNULL(IsCardPaymentApprovalRequired, 0),
    IsCounterRequired = ISNULL(IsCounterRequired, 0),
    IsTableMarkedAvailableAfterBillCompletion = ISNULL(IsTableMarkedAvailableAfterBillCompletion, 0),
    DefaultGSTPercentage = ISNULL(DefaultGSTPercentage, 5.00),
    TakeAwayGSTPercentage = ISNULL(TakeAwayGSTPercentage, 5.00),
    BarGSTPerc = ISNULL(BarGSTPerc, 5.00),
    CurrencySymbol = ISNULL(CurrencySymbol, N'₹'),
    BillFormat = ISNULL(BillFormat, N'A4')
    , FssaiNo = ISNULL(FssaiNo, N'')
    , SelectedOrderType = ISNULL(SelectedOrderType, N'')
WHERE Id IS NOT NULL";

                    using (var normalizeCmd = new SqlCommand(normalizeSql, connection))
                    {
                        await normalizeCmd.ExecuteNonQueryAsync();
                    }
                }
                catch
                {
                    // If normalization fails, swallow - it's a best-effort safety step
                }
            }
        }
    }
}