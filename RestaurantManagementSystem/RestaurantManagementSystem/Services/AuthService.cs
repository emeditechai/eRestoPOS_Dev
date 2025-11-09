using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using RestaurantManagementSystem.Models;

namespace RestaurantManagementSystem.Services
{
    public class AuthService : IAuthService
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuthService> _logger;
        
        public AuthService(
            IConfiguration configuration,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuthService> logger = null)
        {
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }
        
        public async Task<(bool Success, string Message, ClaimsPrincipal Principal)> AuthenticateUserAsync(string username, string password)
        {
            try
            {
                _logger?.LogInformation("Attempting to authenticate user: {Username}", username);
                
                // Debug log for connection string
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                _logger?.LogInformation("Using connection string: {ConnectionString}", 
                    connectionString?.Substring(0, Math.Min(connectionString?.Length ?? 0, 30)) + "..." ?? "NULL");
                
                var user = await GetUserByUsernameAsync(username);
            
                if (user == null)
                {
                    _logger?.LogWarning("Authentication failed: User not found: {Username}", username);
                    return (false, "Invalid username or password", null);
                }
                
                _logger?.LogInformation("User found with ID: {UserId}, Username: {Username}, Hash: {PasswordHashLength} chars", 
                    user.Id, user.Username, user.PasswordHash?.Length ?? 0);
                    
                try {
                    bool passwordVerified = VerifyPassword(password, user.PasswordHash);
                    _logger?.LogInformation("Password verification result: {Result}", passwordVerified);
                    
                    if (!passwordVerified)
                    {
                        _logger?.LogWarning("Authentication failed: Invalid password for user: {Username}", username);
                        // Increment failed login attempts
                        await IncrementFailedLoginAttemptsAsync(user.Id);
                        return (false, "Invalid username or password", null);
                    }
                } catch (Exception pwEx) {
                    _logger?.LogError(pwEx, "Error during password verification for user {Username}", username);
                    return (false, "An error occurred during password verification", null);
                }
                
                if (user.IsLockedOut)
                {
                    return (false, "Your account is locked out. Please contact an administrator.", null);
                }
                
                if (!user.IsActive)
                {
                    return (false, "Your account is not active. Please contact an administrator.", null);
                }
                
                // Reset failed login attempts
                await ResetFailedLoginAttemptsAsync(user.Id);
                
                // Get user roles
                user.Roles = await GetUserRolesAsync(user.Id);
                
                // Create claims for the user
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim("FullName", $"{user.FirstName} {user.LastName}".Trim()),
                    new Claim(ClaimTypes.GivenName, user.FirstName)
                };
                
                // Add email if available
                if (!string.IsNullOrEmpty(user.Email))
                {
                    claims.Add(new Claim(ClaimTypes.Email, user.Email));
                }
                
                // Add surname if available
                if (!string.IsNullOrEmpty(user.LastName))
                {
                    claims.Add(new Claim(ClaimTypes.Surname, user.LastName));
                }
                
                // Add MFA claim if applicable
                if (user.RequiresMFA)
                {
                    claims.Add(new Claim("RequiresMFA", "true"));
                }
                
                // Add user roles to claims
                foreach (var role in user.Roles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role.Name));
                    _logger?.LogInformation("Added role claim: {Role} for user: {Username}", role.Name, username);
                }
                
                // Create identity and principal
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                
                // Update last login time
                await UpdateLastLoginTimeAsync(user.Id);
                
                return (true, "Authentication successful", principal);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during authentication process for user: {Username}", username);
                return (false, "An error occurred during authentication", null);
            }
        }
        
        public async Task SignInUserAsync(ClaimsPrincipal principal, bool rememberMe)
        {
            // Before signing in, create a session record in the database and add the session token as a claim
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;

                // Extract user id from principal
                int? userId = null;
                var idClaim = principal?.FindFirst(ClaimTypes.NameIdentifier);
                if (idClaim != null && int.TryParse(idClaim.Value, out int uid))
                {
                    userId = uid;
                }

                // Generate a session token
                var sessionToken = Guid.NewGuid().ToString();

                // Capture IP address using helper (prefers X-Forwarded-For, X-Real-IP, then RemoteIpAddress)
                string ip = GetClientIp(httpContext);

                // Capture user agent
                string userAgent = httpContext?.Request?.Headers["User-Agent"].FirstOrDefault();

                // Device id (optional header)
                string deviceId = httpContext?.Request?.Headers["X-Device-Id"].FirstOrDefault();

                // Call stored procedure to create/update session
                Guid? sessionId = null;
                try
                {
                    if (userId.HasValue)
                    {
                        using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                        using (var command = new SqlCommand("sp_CreateOrUpdateSession", connection))
                        {
                            command.CommandType = CommandType.StoredProcedure;
                            command.Parameters.AddWithValue("@UserId", userId.Value);
                            command.Parameters.AddWithValue("@Token", sessionToken);
                            command.Parameters.AddWithValue("@ExpiryMinutes", 60 * 12); // 12 hours
                            command.Parameters.AddWithValue("@IpAddress", (object)ip ?? DBNull.Value);
                            command.Parameters.AddWithValue("@DeviceId", (object)deviceId ?? DBNull.Value);
                            command.Parameters.AddWithValue("@UserAgent", (object)userAgent ?? DBNull.Value);

                            await connection.OpenAsync();
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    if (!reader.IsDBNull(reader.GetOrdinal("SessionId")))
                                    {
                                        sessionId = reader.GetGuid(reader.GetOrdinal("SessionId"));
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error creating user session for user {UserId}", userId);
                }

                // If we have a session token, add it as a claim so we can end the session on sign-out
                if (!string.IsNullOrEmpty(sessionToken) && principal?.Identity is ClaimsIdentity identity)
                {
                    identity.AddClaim(new Claim("SessionToken", sessionToken));
                    if (sessionId.HasValue)
                    {
                        identity.AddClaim(new Claim("SessionId", sessionId.Value.ToString()));
                    }
                }

                // Sign in the user (cookie) with the augmented claims
                await _httpContextAccessor.HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = rememberMe,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
                    });

                // Create an audit log entry for login
                try
                {
                    if (userId.HasValue)
                    {
                        await CreateAuditLogAsync(userId.Value, "Login", null, ip, userAgent, "UserSessions", sessionId?.ToString());
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error creating audit log for login for user {UserId}", userId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in SignInUserAsync session creation/logging");
                // fallback to simple sign-in to avoid blocking login
                await _httpContextAccessor.HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties { IsPersistent = rememberMe, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12) });
            }
        }

        public async Task SignOutUserAsync()
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                var user = httpContext?.User;

                // Capture session token claim if present
                string sessionToken = user?.FindFirst("SessionToken")?.Value;

                // Capture user id for audit
                int? userId = null;
                var idClaim = user?.FindFirst(ClaimTypes.NameIdentifier);
                if (idClaim != null && int.TryParse(idClaim.Value, out int uid)) userId = uid;

                // Capture IP and user agent
                string ip = GetClientIp(httpContext);
                string userAgent = httpContext?.Request?.Headers["User-Agent"].FirstOrDefault();

                // Deactivate session record if token exists
                if (!string.IsNullOrEmpty(sessionToken))
                {
                    try
                    {
                        using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                        using (var command = new SqlCommand(@"UPDATE UserSessions SET IsActive = 0 WHERE Token = @Token", connection))
                        {
                            command.Parameters.AddWithValue("@Token", sessionToken);
                            await connection.OpenAsync();
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error deactivating session token {Token}", sessionToken);
                    }
                }

                // Create audit log for logout
                try
                {
                    if (userId.HasValue)
                    {
                        await CreateAuditLogAsync(userId.Value, "Logout", null, ip, userAgent, "UserSessions", sessionToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error creating audit log for logout for user {UserId}", userId);
                }

                // Sign out the cookie
                await _httpContextAccessor.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in SignOutUserAsync");
                // Try a best-effort signout
                await _httpContextAccessor.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        }

        private async Task CreateAuditLogAsync(int userId, string action, string details, string ipAddress, string userAgent, string entityName = null, string entityId = null)
        {
            try
            {
                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                using (var command = new SqlCommand("sp_CreateAuditLog", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@UserId", userId);
                    command.Parameters.AddWithValue("@Action", action);
                    command.Parameters.AddWithValue("@Details", (object)details ?? DBNull.Value);
                    command.Parameters.AddWithValue("@IpAddress", (object)ipAddress ?? DBNull.Value);
                    command.Parameters.AddWithValue("@UserAgent", (object)userAgent ?? DBNull.Value);
                    command.Parameters.AddWithValue("@EntityName", (object)entityName ?? DBNull.Value);
                    command.Parameters.AddWithValue("@EntityId", (object)entityId ?? DBNull.Value);

                    await connection.OpenAsync();
                    await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error creating audit log entry for user {UserId}", userId);
            }
        }

            /// <summary>
            /// Update the IP address for an existing session token.
            /// </summary>
            public async Task<bool> UpdateSessionIpAsync(string token, string ipAddress)
            {
                if (string.IsNullOrEmpty(token)) return false;

                try
                {
                    using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                    using (var command = new SqlCommand(@"UPDATE UserSessions SET IpAddress = @IpAddress, LastActivityDate = GETDATE() WHERE Token = @Token", connection))
                    {
                        command.Parameters.AddWithValue("@IpAddress", (object)ipAddress ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Token", token);
                        await connection.OpenAsync();
                        var rows = await command.ExecuteNonQueryAsync();
                        return rows > 0;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error updating session IP for token {Token}", token);
                    return false;
                }
            }

        /// <summary>
        /// Extract client IP address, preferring X-Forwarded-For, X-Real-IP or CF-Connecting-IP headers when present.
        /// When a header contains multiple IPs, prefer the first public IP (skip private/local addresses).
        /// Normalizes IPv6-loopback and IPv6-mapped IPv4 addresses to IPv4 where possible.
        /// </summary>
        private string GetClientIp(HttpContext httpContext)
        {
            try
            {
                if (httpContext == null) return null;

                string ip = null;

                if (httpContext.Request?.Headers != null)
                {
                    // Check common headers in order of trust
                    if (httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var xff))
                        ip = xff.FirstOrDefault();

                    if (string.IsNullOrEmpty(ip) && httpContext.Request.Headers.TryGetValue("X-Real-IP", out var xrip))
                        ip = xrip.FirstOrDefault();

                    if (string.IsNullOrEmpty(ip) && httpContext.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfip))
                        ip = cfip.FirstOrDefault();

                    if (string.IsNullOrEmpty(ip) && httpContext.Request.Headers.TryGetValue("Forwarded", out var forwarded))
                    {
                        // Forwarded: for=192.0.2.60;proto=http;by=203.0.113.43
                        var f = forwarded.FirstOrDefault();
                        if (!string.IsNullOrEmpty(f))
                        {
                            var forPart = f.Split(';').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("for=", StringComparison.OrdinalIgnoreCase));
                            if (!string.IsNullOrEmpty(forPart))
                            {
                                var raw = forPart.Substring(4).Trim();
                                // strip quotes
                                raw = raw.Trim('"');
                                ip = raw;
                            }
                        }
                    }
                }

                // If header contains multiple IPs, prefer the first public IP (skip private/local addresses)
                if (!string.IsNullOrEmpty(ip) && ip.Contains(","))
                {
                    var candidates = ip.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                    var publicIp = candidates.FirstOrDefault(c => IsPublicIp(c));
                    ip = publicIp ?? candidates.FirstOrDefault();
                }

                // Fallback to remote address
                if (string.IsNullOrEmpty(ip))
                {
                    ip = httpContext.Connection?.RemoteIpAddress?.ToString();
                }

                if (string.IsNullOrEmpty(ip)) return null;

                // Normalize IPv6 loopback to IPv4 loopback
                if (ip == "::1" || ip == "0:0:0:0:0:0:0:1") ip = "127.0.0.1";

                // Handle IPv6-mapped IPv4 addresses like ::ffff:127.0.0.1
                if (IPAddress.TryParse(ip, out var addr))
                {
                    if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                    {
                        try
                        {
                            var mapped = addr.MapToIPv4();
                            if (mapped != null)
                            {
                                ip = mapped.ToString();
                            }
                        }
                        catch
                        {
                            // ignore mapping errors and keep original
                        }
                    }
                }

                return ip;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to determine client IP");
                return null;
            }
        }

        private bool IsPublicIp(string ipString)
        {
            if (string.IsNullOrEmpty(ipString)) return false;
            if (!IPAddress.TryParse(ipString, out var addr)) return false;

            // IPv4 private ranges and loopback
            if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = addr.GetAddressBytes();
                // 10.0.0.0/8
                if (bytes[0] == 10) return false;
                // 172.16.0.0/12
                if (bytes[0] == 172 && (bytes[1] >= 16 && bytes[1] <= 31)) return false;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return false;
                // 127.0.0.0/8 loopback
                if (bytes[0] == 127) return false;
                // Link-local 169.254.0.0/16
                if (bytes[0] == 169 && bytes[1] == 254) return false;
                return true;
            }

            // For IPv6, consider global unicast as public
            if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                // fc00::/7 are unique local addresses
                var bytes = addr.GetAddressBytes();
                if ((bytes[0] & 0xfe) == 0xfc) return false; // fc00::/7
                if (addr.IsIPv6LinkLocal || addr.IsIPv6SiteLocal || addr.IsIPv6Multicast) return false;
                // loopback
                if (IPAddress.IsLoopback(addr)) return false;
                return true;
            }

            return false;
        }
        
        private async Task<User> GetUserByUsernameAsync(string username)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            using (var connection = new SqlConnection(connectionString))
            {
                try
                {
                    await connection.OpenAsync();
                    _logger?.LogInformation("Database connection opened successfully");
                    
                    // Try known schemas: prefer the namespaced table used by the UI/stored procedures, then fall back to the default Users table
                    var tableCandidates = new[] { "dbo.Users", "Users" };
                    foreach (var table in tableCandidates)
                    {
                        try
                        {
                            using (var command = new SqlCommand($"SELECT * FROM {table} WHERE Username = @Username", connection))
                            {
                                command.Parameters.AddWithValue("@Username", username);
                                _logger?.LogInformation("Executing query against {Table} for username: {Username}", table, username);

                                using (var reader = await command.ExecuteReaderAsync())
                                {
                                    if (await reader.ReadAsync())
                                    {
                                        _logger?.LogInformation("User found in database ({Table})", table);

                                        // Debug log all columns to help diagnose issues
                                        var columnNames = new List<string>();
                                        for (int i = 0; i < reader.FieldCount; i++)
                                        {
                                            columnNames.Add(reader.GetName(i));
                                        }
                                        _logger?.LogInformation("Columns in {Table}: {Columns}", table, string.Join(", ", columnNames));

                                        // Extract the password hash directly to check its format
                                        string passwordHash = reader["PasswordHash"]?.ToString();
                                        _logger?.LogInformation("Raw PasswordHash: {PasswordHash}", passwordHash ?? "NULL");

                                        return new User
                                        {
                                            Id = Convert.ToInt32(reader["Id"]),
                                            Username = reader["Username"].ToString(),
                                            PasswordHash = passwordHash,
                                            FirstName = reader["FirstName"]?.ToString(),
                                            LastName = reader.IsDBNull(reader.GetOrdinal("LastName")) ? null : reader["LastName"].ToString(),
                                            Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader["Email"].ToString(),
                                            IsActive = reader.IsDBNull(reader.GetOrdinal("IsActive")) ? true : Convert.ToBoolean(reader["IsActive"]),
                                            IsLockedOut = reader.IsDBNull(reader.GetOrdinal("IsLockedOut")) ? false : Convert.ToBoolean(reader["IsLockedOut"]),
                                            RequiresMFA = reader.IsDBNull(reader.GetOrdinal("RequiresMFA")) ? false : Convert.ToBoolean(reader["RequiresMFA"]),
                                            CreatedAt = reader.IsDBNull(reader.GetOrdinal("CreatedDate")) ? DateTime.MinValue : Convert.ToDateTime(reader["CreatedDate"])
                                        };
                                    }
                                }
                            }
                        }
                        catch (SqlException sqlEx)
                        {
                            // If table doesn't exist in this schema, try the next candidate
                            _logger?.LogDebug(sqlEx, "Query against {Table} failed, trying next candidate", table);
                        }
                    }
                    _logger?.LogWarning("No user found with username: {Username} in any known Users table", username);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error retrieving user from database");
                }
            }
            
            return null;
        }
        
        private async Task<List<Role>> GetUserRolesAsync(int userId)
        {
            var roles = new List<Role>();
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    using (var command = new SqlCommand(@"
                        SELECT r.Id, r.Name, r.Description
                        FROM Roles r
                        INNER JOIN UserRoles ur ON r.Id = ur.RoleId
                        WHERE ur.UserId = @UserId", connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                roles.Add(new Role
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    Name = reader["Name"].ToString(),
                                    Description = reader["Description"]?.ToString()
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error retrieving user roles");
            }
            
            return roles;
        }
        
        public async Task<bool> IsUserInRoleAsync(int userId, string roleName)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    using (var command = new SqlCommand(@"
                        SELECT COUNT(1)
                        FROM UserRoles ur
                        INNER JOIN Roles r ON ur.RoleId = r.Id
                        WHERE ur.UserId = @UserId AND r.Name = @RoleName", connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        command.Parameters.AddWithValue("@RoleName", roleName);
                        
                        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error checking if user is in role");
                return false;
            }
        }
        
        public async Task<(bool success, string message)> RegisterUserAsync(User user, string password, string roleName = "Staff")
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                
                // Hash the password
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));
                
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Begin transaction
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Insert user
                            int userId;
                            using (var command = new SqlCommand(@"
                                INSERT INTO Users (Username, PasswordHash, FirstName, LastName, Email, IsActive, IsLockedOut, RequiresMFA, CreatedAt)
                                VALUES (@Username, @PasswordHash, @FirstName, @LastName, @Email, @IsActive, @IsLockedOut, @RequiresMFA, @CreatedAt);
                                SELECT SCOPE_IDENTITY();", connection, transaction))
                            {
                                command.Parameters.AddWithValue("@Username", user.Username);
                                command.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
                                command.Parameters.AddWithValue("@FirstName", user.FirstName);
                                command.Parameters.AddWithValue("@LastName", user.LastName ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@Email", user.Email ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@IsActive", user.IsActive);
                                command.Parameters.AddWithValue("@IsLockedOut", user.IsLockedOut);
                                command.Parameters.AddWithValue("@RequiresMFA", user.RequiresMFA);
                                command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);
                                
                                userId = Convert.ToInt32(await command.ExecuteScalarAsync());
                            }
                            
                            // Get role ID
                            int roleId;
                            using (var command = new SqlCommand("SELECT Id FROM Roles WHERE Name = @RoleName", connection, transaction))
                            {
                                command.Parameters.AddWithValue("@RoleName", roleName);
                                var result = await command.ExecuteScalarAsync();
                                
                                if (result == null)
                                {
                                    // Role doesn't exist, create it
                                    using (var createRoleCommand = new SqlCommand("INSERT INTO Roles (Name, Description) VALUES (@Name, @Description); SELECT SCOPE_IDENTITY();", connection, transaction))
                                    {
                                        createRoleCommand.Parameters.AddWithValue("@Name", roleName);
                                        createRoleCommand.Parameters.AddWithValue("@Description", $"{roleName} role");
                                        roleId = Convert.ToInt32(await createRoleCommand.ExecuteScalarAsync());
                                    }
                                }
                                else
                                {
                                    roleId = Convert.ToInt32(result);
                                }
                            }
                            
                            // Assign role to user
                            using (var command = new SqlCommand("INSERT INTO UserRoles (UserId, RoleId) VALUES (@UserId, @RoleId)", connection, transaction))
                            {
                                command.Parameters.AddWithValue("@UserId", userId);
                                command.Parameters.AddWithValue("@RoleId", roleId);
                                await command.ExecuteNonQueryAsync();
                            }
                            
                            // Commit transaction
                            transaction.Commit();
                            return (true, "User registered successfully");
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error registering user");
                            transaction.Rollback();
                            return (false, $"Error registering user: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error registering user");
                return (false, $"Error registering user: {ex.Message}");
            }
        }
        
        public async Task<(bool success, string message)> UpdatePasswordAsync(int userId, string newPassword)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                
                // Hash the new password
                string passwordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, BCrypt.Net.BCrypt.GenerateSalt(12));
                
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    using (var command = new SqlCommand("UPDATE Users SET PasswordHash = @PasswordHash WHERE Id = @UserId", connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        command.Parameters.AddWithValue("@PasswordHash", passwordHash);
                        
                        var rowsAffected = await command.ExecuteNonQueryAsync();
                        return rowsAffected > 0 
                            ? (true, "Password updated successfully") 
                            : (false, "User not found");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating password");
                return (false, $"Error updating password: {ex.Message}");
            }
        }
        
        public async Task<(bool success, string message)> LockUserAsync(int userId)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    using (var command = new SqlCommand("UPDATE Users SET IsLockedOut = 1 WHERE Id = @UserId", connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        
                        var rowsAffected = await command.ExecuteNonQueryAsync();
                        return rowsAffected > 0
                            ? (true, "User locked successfully")
                            : (false, "User not found");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error locking user");
                return (false, $"Error locking user: {ex.Message}");
            }
        }
        
        public async Task<(bool success, string message)> UnlockUserAsync(int userId)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    using (var command = new SqlCommand("UPDATE Users SET IsLockedOut = 0 WHERE Id = @UserId", connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        
                        var rowsAffected = await command.ExecuteNonQueryAsync();
                        return rowsAffected > 0
                            ? (true, "User unlocked successfully")
                            : (false, "User not found");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error unlocking user");
                return (false, $"Error unlocking user: {ex.Message}");
            }
        }
        
        public async Task<(bool success, string message)> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get current password hash
                    string currentHash;
                    using (var command = new SqlCommand("SELECT PasswordHash FROM Users WHERE Id = @UserId", connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        currentHash = (string)await command.ExecuteScalarAsync();
                    }
                    
                    // Verify current password
                    if (!VerifyPassword(currentPassword, currentHash))
                    {
                        return (false, "Current password is incorrect");
                    }
                    
                    // Update to new password
                    string newHash = BCrypt.Net.BCrypt.HashPassword(newPassword, BCrypt.Net.BCrypt.GenerateSalt(12));
                    
                    using (var command = new SqlCommand("UPDATE Users SET PasswordHash = @PasswordHash WHERE Id = @UserId", connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        command.Parameters.AddWithValue("@PasswordHash", newHash);
                        
                        await command.ExecuteNonQueryAsync();
                    }
                    
                    return (true, "Password changed successfully");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error changing password");
                return (false, "An error occurred while changing the password");
            }
        }
        
        public async Task<List<User>> GetUsersAsync()
        {
            var users = new List<User>();
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    using (var command = new SqlCommand("SELECT * FROM Users ORDER BY Username", connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                users.Add(new User
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    Username = reader["Username"].ToString(),
                                    FirstName = reader["FirstName"].ToString(),
                                    LastName = reader.IsDBNull(reader.GetOrdinal("LastName")) ? null : reader["LastName"].ToString(),
                                    Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader["Email"].ToString(),
                                    IsActive = Convert.ToBoolean(reader["IsActive"]),
                                    IsLockedOut = Convert.ToBoolean(reader["IsLockedOut"]),
                                    CreatedAt = Convert.ToDateTime(reader["CreatedDate"])
                                });
                            }
                        }
                    }
                    
                    // Get roles for each user
                    foreach (var user in users)
                    {
                        user.Roles = await GetUserRolesAsync(user.Id);
                    }
                }
                
                return users;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting users");
                return new List<User>();
            }
        }
        
        public async Task<User> GetUserForEditAsync(int userId)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    using (var command = new SqlCommand("SELECT * FROM Users WHERE Id = @UserId", connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var user = new User
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    Username = reader["Username"].ToString(),
                                    FirstName = reader["FirstName"].ToString(),
                                    LastName = reader.IsDBNull(reader.GetOrdinal("LastName")) ? null : reader["LastName"].ToString(),
                                    Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader["Email"].ToString(),
                                    IsActive = Convert.ToBoolean(reader["IsActive"]),
                                    IsLockedOut = Convert.ToBoolean(reader["IsLockedOut"]),
                                    CreatedAt = Convert.ToDateTime(reader["CreatedDate"])
                                };
                                
                                // Get user roles
                                user.Roles = await GetUserRolesAsync(user.Id);
                                
                                return user;
                            }
                        }
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting user for edit");
                return null;
            }
        }
        
        public async Task<(bool success, string message)> UpdateUserAsync(User user, int updatedByUserId)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Update user
                            using (var command = new SqlCommand(@"
                                UPDATE Users
                                SET FirstName = @FirstName,
                                    LastName = @LastName,
                                    Email = @Email,
                                    IsActive = @IsActive,
                                    IsLockedOut = @IsLockedOut
                                WHERE Id = @UserId", connection, transaction))
                            {
                                command.Parameters.AddWithValue("@UserId", user.Id);
                                command.Parameters.AddWithValue("@FirstName", user.FirstName);
                                command.Parameters.AddWithValue("@LastName", user.LastName ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@Email", user.Email ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@IsActive", user.IsActive);
                                command.Parameters.AddWithValue("@IsLockedOut", user.IsLockedOut);
                                
                                await command.ExecuteNonQueryAsync();
                            }
                            
                            // Update user roles (remove all and add selected)
                            using (var command = new SqlCommand("DELETE FROM UserRoles WHERE UserId = @UserId", connection, transaction))
                            {
                                command.Parameters.AddWithValue("@UserId", user.Id);
                                await command.ExecuteNonQueryAsync();
                            }
                            
                            if (user.Roles != null && user.Roles.Count > 0)
                            {
                                foreach (var roleName in user.Roles)
                                {
                                    // Get role ID
                                    int roleId;
                                    using (var command = new SqlCommand("SELECT Id FROM Roles WHERE Name = @RoleName", connection, transaction))
                                    {
                                        command.Parameters.AddWithValue("@RoleName", roleName);
                                        var result = await command.ExecuteScalarAsync();
                                        
                                        if (result == null)
                                        {
                                            // Role doesn't exist, create it
                                            using (var createRoleCommand = new SqlCommand("INSERT INTO Roles (Name, Description) VALUES (@Name, @Description); SELECT SCOPE_IDENTITY();", connection, transaction))
                                            {
                                                createRoleCommand.Parameters.AddWithValue("@Name", roleName);
                                                createRoleCommand.Parameters.AddWithValue("@Description", $"{roleName} role");
                                                roleId = Convert.ToInt32(await createRoleCommand.ExecuteScalarAsync());
                                            }
                                        }
                                        else
                                        {
                                            roleId = Convert.ToInt32(result);
                                        }
                                    }
                                    
                                    // Assign role to user
                                    using (var command = new SqlCommand("INSERT INTO UserRoles (UserId, RoleId) VALUES (@UserId, @RoleId)", connection, transaction))
                                    {
                                        command.Parameters.AddWithValue("@UserId", user.Id);
                                        command.Parameters.AddWithValue("@RoleId", roleId);
                                        await command.ExecuteNonQueryAsync();
                                    }
                                }
                            }
                            
                            transaction.Commit();
                            return (true, "User updated successfully");
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            _logger?.LogError(ex, "Error updating user");
                            return (false, "An error occurred while updating the user");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating user");
                return (false, "An error occurred while updating the user");
            }
        }
        
        private bool VerifyPassword(string password, string passwordHash)
        {
            try
            {
                _logger?.LogInformation("Verifying password. Stored hash length: {Length}", passwordHash?.Length ?? 0);

                if (string.IsNullOrEmpty(passwordHash))
                {
                    _logger?.LogWarning("Empty password hash provided");
                    return false;
                }

                // BCrypt formats start with $2a$, $2b$, $2y$
                if (passwordHash.StartsWith("$2a$") || passwordHash.StartsWith("$2b$") || passwordHash.StartsWith("$2y$"))
                {
                    var ok = BCrypt.Net.BCrypt.Verify(password, passwordHash);
                    _logger?.LogInformation("BCrypt verification result: {Result}", ok);
                    return ok;
                }

                // Legacy PBKDF2 format: iterations:salt:hash
                if (passwordHash.Contains(":"))
                {
                    var ok = PasswordHasher.VerifyPassword(password, passwordHash);
                    _logger?.LogInformation("PBKDF2 verification result: {Result}", ok);
                    return ok;
                }

                // Unknown format - attempt BCrypt verify as a last resort
                try
                {
                    var ok = BCrypt.Net.BCrypt.Verify(password, passwordHash);
                    _logger?.LogInformation("Fallback BCrypt verification result: {Result}", ok);
                    return ok;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Unknown password hash format and BCrypt fallback failed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error verifying password");
                return false;
            }
        }
        
        private async Task IncrementFailedLoginAttemptsAsync(int userId)
        {
            // This would update a failed login attempts counter in your database
            // For now, just logging it
            _logger?.LogInformation("Incrementing failed login attempts for user ID: {UserId}", userId);
        }
        
        private async Task ResetFailedLoginAttemptsAsync(int userId)
        {
            // This would reset a failed login attempts counter in your database
            // For now, just logging it
            _logger?.LogInformation("Resetting failed login attempts for user ID: {UserId}", userId);
        }
        
        private async Task UpdateLastLoginTimeAsync(int userId)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check which LastLogin column exists
                    string columnToUpdate = null;
                    using (var checkCmd = new SqlCommand(@"
                        SELECT COLUMN_NAME 
                        FROM INFORMATION_SCHEMA.COLUMNS 
                        WHERE TABLE_NAME = 'Users' 
                        AND COLUMN_NAME IN ('LastLoginDate', 'LastLoginAt', 'LastLogin')", connection))
                    {
                        var result = await checkCmd.ExecuteScalarAsync();
                        columnToUpdate = result?.ToString();
                    }

                    if (!string.IsNullOrEmpty(columnToUpdate))
                    {
                        var updateQuery = $"UPDATE Users SET {columnToUpdate} = @LastLoginTime WHERE Id = @UserId";
                        using (var updateCmd = new SqlCommand(updateQuery, connection))
                        {
                            updateCmd.Parameters.AddWithValue("@UserId", userId);
                            updateCmd.Parameters.AddWithValue("@LastLoginTime", DateTime.Now);
                            await updateCmd.ExecuteNonQueryAsync();
                        }
                        _logger?.LogInformation("Updated {Column} for user {UserId}", columnToUpdate, userId);
                    }
                    else
                    {
                        _logger?.LogDebug("No LastLogin column found on Users table.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating last login time: {Error}", ex.Message);
            }
        }
    }
}
