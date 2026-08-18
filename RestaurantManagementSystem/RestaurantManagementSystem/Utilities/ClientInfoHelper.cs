using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace RestaurantManagementSystem.Utilities
{
    public class ClientBrowserInfo
    {
        public string Browser { get; set; } = "Unknown Browser";
        public string OperatingSystem { get; set; } = "Unknown OS";
        public string DeviceType { get; set; } = "Desktop";
        public string UserAgent { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;

        public string FormattedSummary => $"{Browser} on {OperatingSystem} ({DeviceType})";
    }

    public static class ClientInfoHelper
    {
        /// <summary>
        /// Extracts client IP address with multi-header support (X-Forwarded-For, X-Real-IP, CF-Connecting-IP, Forwarded, RemoteIpAddress).
        /// Normalizes IPv6 loopback (::1) to 127.0.0.1 and strips port numbers.
        /// </summary>
        public static string GetClientIp(HttpContext? httpContext)
        {
            if (httpContext == null) return "127.0.0.1";

            try
            {
                string? ip = null;

                if (httpContext.Request?.Headers != null)
                {
                    // 1. Cloudflare Connecting IP
                    if (httpContext.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp) && !string.IsNullOrWhiteSpace(cfIp))
                    {
                        ip = cfIp.FirstOrDefault();
                    }

                    // 2. Standard X-Forwarded-For
                    if (string.IsNullOrWhiteSpace(ip) && httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var xff) && !string.IsNullOrWhiteSpace(xff))
                    {
                        ip = xff.FirstOrDefault();
                    }

                    // 3. X-Real-IP
                    if (string.IsNullOrWhiteSpace(ip) && httpContext.Request.Headers.TryGetValue("X-Real-IP", out var xRealIp) && !string.IsNullOrWhiteSpace(xRealIp))
                    {
                        ip = xRealIp.FirstOrDefault();
                    }

                    // 4. RFC 7239 Forwarded header (e.g. for=192.0.2.60;proto=http)
                    if (string.IsNullOrWhiteSpace(ip) && httpContext.Request.Headers.TryGetValue("Forwarded", out var fwd) && !string.IsNullOrWhiteSpace(fwd))
                    {
                        var forPart = fwd.FirstOrDefault()?
                            .Split(';', StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Trim())
                            .FirstOrDefault(p => p.StartsWith("for=", StringComparison.OrdinalIgnoreCase));

                        if (!string.IsNullOrWhiteSpace(forPart) && forPart.Length > 4)
                        {
                            ip = forPart.Substring(4).Trim('"', ' ');
                        }
                    }
                }

                // If comma-separated (proxy chain), choose first public or first non-empty IP
                if (!string.IsNullOrWhiteSpace(ip) && ip.Contains(','))
                {
                    var ipCandidates = ip.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    ip = ipCandidates.FirstOrDefault(c => IsPublicIp(c)) ?? ipCandidates.FirstOrDefault();
                }

                // 5. Fallback to HttpContext.Connection.RemoteIpAddress
                if (string.IsNullOrWhiteSpace(ip))
                {
                    ip = httpContext.Connection?.RemoteIpAddress?.ToString();
                }

                if (string.IsNullOrWhiteSpace(ip))
                {
                    return "127.0.0.1";
                }

                ip = ip.Trim();

                // Strip port if IPv4 with port (e.g. 192.168.1.1:54321)
                if (ip.Contains(':') && !ip.Contains("::") && IPAddress.TryParse(ip.Split(':')[0], out _))
                {
                    ip = ip.Split(':')[0];
                }

                // Normalize IPv6 loopback
                if (ip == "::1" || ip == "0:0:0:0:0:0:0:1" || ip.Equals("::ffff:127.0.0.1", StringComparison.OrdinalIgnoreCase))
                {
                    ip = "127.0.0.1";
                }
                else if (ip.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
                {
                    ip = ip.Substring(7);
                }

                return ip;
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        /// <summary>
        /// Parses the User-Agent header and Client Hints to determine browser, operating system, and device type.
        /// </summary>
        public static ClientBrowserInfo GetBrowserInfo(HttpContext? httpContext)
        {
            var info = new ClientBrowserInfo();

            if (httpContext == null) return info;

            try
            {
                info.IpAddress = GetClientIp(httpContext);

                var ua = httpContext.Request?.Headers["User-Agent"].ToString() ?? string.Empty;
                info.UserAgent = ua.Length > 500 ? ua.Substring(0, 500) : ua;

                if (string.IsNullOrWhiteSpace(ua))
                {
                    // Try Client Hints if available
                    if (httpContext.Request?.Headers.TryGetValue("Sec-CH-UA", out var secChUa) == true && !string.IsNullOrWhiteSpace(secChUa))
                    {
                        info.Browser = secChUa.ToString();
                    }
                    if (httpContext.Request?.Headers.TryGetValue("Sec-CH-UA-Platform", out var secPlatform) == true && !string.IsNullOrWhiteSpace(secPlatform))
                    {
                        info.OperatingSystem = secPlatform.ToString().Trim('"');
                    }
                    return info;
                }

                // 1. Detect Device Type
                if (Regex.IsMatch(ua, @"(tablet|ipad|playbook|silk)|(android(?!.*mobi))", RegexOptions.IgnoreCase))
                {
                    info.DeviceType = "Tablet";
                }
                else if (Regex.IsMatch(ua, @"Mobile|iP(hone|od)|Android|BlackBerry|IEMobile|Kindle|NetFront|Silk-Accelerated|(hpw|web)OS|Fennec|Minimo|Opera M(obi|ini)|Blazer|Dolfin|Dolphin|Skyfire|Zune", RegexOptions.IgnoreCase))
                {
                    info.DeviceType = "Mobile";
                }
                else
                {
                    info.DeviceType = "Desktop";
                }

                // 2. Detect Operating System
                if (ua.Contains("Windows NT 10.0", StringComparison.OrdinalIgnoreCase))
                    info.OperatingSystem = "Windows 10/11";
                else if (ua.Contains("Windows NT 6.3", StringComparison.OrdinalIgnoreCase))
                    info.OperatingSystem = "Windows 8.1";
                else if (ua.Contains("Windows NT 6.2", StringComparison.OrdinalIgnoreCase))
                    info.OperatingSystem = "Windows 8";
                else if (ua.Contains("Windows NT 6.1", StringComparison.OrdinalIgnoreCase))
                    info.OperatingSystem = "Windows 7";
                else if (ua.Contains("Windows NT", StringComparison.OrdinalIgnoreCase))
                    info.OperatingSystem = "Windows";
                else if (ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
                    info.OperatingSystem = "iOS (iPhone)";
                else if (ua.Contains("iPad", StringComparison.OrdinalIgnoreCase))
                    info.OperatingSystem = "iOS (iPad)";
                else if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(ua, @"Android\s+([0-9\.]+)");
                    info.OperatingSystem = m.Success ? $"Android {m.Groups[1].Value}" : "Android";
                }
                else if (ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(ua, @"Mac OS X\s+([0-9_\.]+)");
                    var macVer = m.Success ? m.Groups[1].Value.Replace('_', '.') : "";
                    info.OperatingSystem = string.IsNullOrEmpty(macVer) ? "macOS" : $"macOS {macVer}";
                }
                else if (ua.Contains("Linux", StringComparison.OrdinalIgnoreCase))
                    info.OperatingSystem = "Linux";
                else if (ua.Contains("CrOS", StringComparison.OrdinalIgnoreCase))
                    info.OperatingSystem = "Chrome OS";

                // 3. Detect Browser Name & Version
                if (ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) || ua.Contains("Edge/", StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(ua, @"Edg(e)?\/([0-9\.]+)");
                    info.Browser = m.Success ? $"Microsoft Edge {GetMajorVersion(m.Groups[2].Value)}" : "Microsoft Edge";
                }
                else if (ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase) || ua.Contains("Opera", StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(ua, @"OPR\/([0-9\.]+)");
                    info.Browser = m.Success ? $"Opera {GetMajorVersion(m.Groups[1].Value)}" : "Opera";
                }
                else if (ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) && !ua.Contains("Chromium", StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(ua, @"Chrome\/([0-9\.]+)");
                    info.Browser = m.Success ? $"Chrome {GetMajorVersion(m.Groups[1].Value)}" : "Chrome";
                }
                else if (ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(ua, @"Firefox\/([0-9\.]+)");
                    info.Browser = m.Success ? $"Firefox {GetMajorVersion(m.Groups[1].Value)}" : "Firefox";
                }
                else if (ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) && !ua.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(ua, @"Version\/([0-9\.]+)");
                    info.Browser = m.Success ? $"Safari {GetMajorVersion(m.Groups[1].Value)}" : "Safari";
                }
                else if (ua.Contains("SamsungBrowser/", StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(ua, @"SamsungBrowser\/([0-9\.]+)");
                    info.Browser = m.Success ? $"Samsung Internet {GetMajorVersion(m.Groups[1].Value)}" : "Samsung Internet";
                }
                else if (ua.Contains("MSIE", StringComparison.OrdinalIgnoreCase) || ua.Contains("Trident/", StringComparison.OrdinalIgnoreCase))
                {
                    info.Browser = "Internet Explorer";
                }
                else
                {
                    info.Browser = "Generic Browser";
                }
            }
            catch
            {
                info.Browser = "Browser";
                info.OperatingSystem = "OS";
                info.DeviceType = "Device";
            }

            return info;
        }

        private static string GetMajorVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return string.Empty;
            var parts = version.Split('.');
            return parts.Length > 0 ? parts[0] : version;
        }

        private static bool IsPublicIp(string ipAddress)
        {
            if (!IPAddress.TryParse(ipAddress, out var ip)) return false;

            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            if (IPAddress.IsLoopback(ip)) return false;

            byte[] bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
            {
                // 10.0.0.0/8
                if (bytes[0] == 10) return false;
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return false;
                // 127.0.0.0/8
                if (bytes[0] == 127) return false;
                // 169.254.0.0/16 Link-local
                if (bytes[0] == 169 && bytes[1] == 254) return false;
                return true;
            }

            return !ip.IsIPv6LinkLocal && !ip.IsIPv6SiteLocal;
        }
    }
}
