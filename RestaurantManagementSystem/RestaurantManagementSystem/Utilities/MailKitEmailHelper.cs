using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Logging;

namespace RestaurantManagementSystem.Utilities
{
    public static class MailKitEmailHelper
    {
        public static SecureSocketOptions ResolveSecureSocketOptions(int port, bool enableSsl)
        {
            if (port == 465)
            {
                // Port 465 is dedicated SSL/TLS (Implicit SSL / SslOnConnect)
                return SecureSocketOptions.SslOnConnect;
            }
            if (port == 587)
            {
                // Port 587 is submission with STARTTLS
                return enableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.StartTlsWhenAvailable;
            }
            if (port == 25 || port == 2525)
            {
                // Standard SMTP port
                return enableSsl ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.None;
            }

            // Other ports
            return enableSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None;
        }

        public static string? ExtractDomain(string? emailOrUsername)
        {
            if (string.IsNullOrWhiteSpace(emailOrUsername)) return null;
            var atIndex = emailOrUsername.IndexOf('@');
            if (atIndex >= 0 && atIndex < emailOrUsername.Length - 1)
            {
                return emailOrUsername.Substring(atIndex + 1).Trim();
            }
            return null;
        }

        public static string NormalizeSmtpServer(string? smtpServer)
        {
            if (string.IsNullOrWhiteSpace(smtpServer)) return string.Empty;
            smtpServer = smtpServer.Trim();

            if (string.Equals(smtpServer, "gmail.com", StringComparison.OrdinalIgnoreCase))
                return "smtp.gmail.com";
            if (string.Equals(smtpServer, "outlook.com", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(smtpServer, "hotmail.com", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(smtpServer, "office365.com", StringComparison.OrdinalIgnoreCase))
                return "smtp.office365.com";
            if (string.Equals(smtpServer, "yahoo.com", StringComparison.OrdinalIgnoreCase))
                return "smtp.mail.yahoo.com";

            return smtpServer;
        }

        public static async Task<(bool Success, string? ErrorMessage, int ProcessingTimeMs)> SendEmailAsync(
            string smtpServer,
            int smtpPort,
            string smtpUsername,
            string smtpPassword,
            bool enableSsl,
            string fromEmail,
            string fromName,
            string toEmail,
            string subject,
            string htmlBody,
            byte[]? attachmentBytes = null,
            string? attachmentFileName = null,
            string? attachmentContentType = null,
            ILogger? logger = null)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (string.IsNullOrWhiteSpace(toEmail))
                {
                    return (false, "Recipient email address is empty", 0);
                }

                if (string.IsNullOrWhiteSpace(fromEmail))
                {
                    return (false, "Sender (From) email address is empty", 0);
                }

                var normalizedHost = NormalizeSmtpServer(smtpServer);
                if (string.IsNullOrWhiteSpace(normalizedHost))
                {
                    return (false, "SMTP server address is not configured", 0);
                }

                var message = new MimeMessage();
                var senderDisplayName = string.IsNullOrWhiteSpace(fromName) ? fromEmail : fromName;
                message.From.Add(new MailboxAddress(senderDisplayName, fromEmail.Trim()));
                message.ReplyTo.Add(new MailboxAddress(senderDisplayName, fromEmail.Trim()));

                // Add recipients (support comma/semicolon separated list)
                var recipients = toEmail.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var recipient in recipients)
                {
                    var trimmed = recipient.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        if (MailboxAddress.TryParse(trimmed, out var mailbox))
                        {
                            message.To.Add(mailbox);
                        }
                        else
                        {
                            message.To.Add(new MailboxAddress(trimmed, trimmed));
                        }
                    }
                }

                if (message.To.Count == 0)
                {
                    return (false, "No valid recipient email address found", 0);
                }

                message.Subject = subject ?? string.Empty;

                var builder = new BodyBuilder
                {
                    HtmlBody = htmlBody ?? string.Empty,
                    TextBody = !string.IsNullOrWhiteSpace(htmlBody)
                        ? Regex.Replace(htmlBody, "<[^>]+>", " ").Trim()
                        : string.Empty
                };

                if (attachmentBytes != null && attachmentBytes.Length > 0 && !string.IsNullOrWhiteSpace(attachmentFileName))
                {
                    if (!string.IsNullOrWhiteSpace(attachmentContentType) && ContentType.TryParse(attachmentContentType, out var parsedContentType))
                    {
                        builder.Attachments.Add(attachmentFileName, attachmentBytes, parsedContentType);
                    }
                    else
                    {
                        builder.Attachments.Add(attachmentFileName, attachmentBytes);
                    }
                }

                message.Body = builder.ToMessageBody();

                using var client = new SmtpClient();

                // 1. Support self-signed TLS certificates (crucial for corporate mail / webmail / cPanel / MailEnable)
                client.ServerCertificateValidationCallback = (s, cert, chain, errors) => true;

                // 2. Set proper EHLO/HELO domain
                var localDomain = ExtractDomain(fromEmail) ?? ExtractDomain(smtpUsername) ?? "localhost";
                if (!string.IsNullOrWhiteSpace(localDomain))
                {
                    client.LocalDomain = localDomain;
                }

                // 3. Determine socket options
                var socketOptions = ResolveSecureSocketOptions(smtpPort, enableSsl);

                // 4. Timeout
                client.Timeout = 30000; // 30 seconds

                // 5. Connect
                await client.ConnectAsync(normalizedHost, smtpPort, socketOptions);

                // 6. Authenticate if credentials provided
                if (!string.IsNullOrWhiteSpace(smtpUsername) && !string.IsNullOrWhiteSpace(smtpPassword))
                {
                    await client.AuthenticateAsync(smtpUsername.Trim(), smtpPassword);
                }

                // 7. Send
                await client.SendAsync(message);

                // 8. Disconnect
                await client.DisconnectAsync(true);

                stopwatch.Stop();
                return (true, null, (int)stopwatch.ElapsedMilliseconds);
            }
            catch (MailKit.Security.AuthenticationException ex)
            {
                stopwatch.Stop();
                logger?.LogError(ex, "SMTP Authentication failed for {Username} on {Host}:{Port}", smtpUsername, smtpServer, smtpPort);
                
                var isGmail = smtpServer != null && smtpServer.Contains("gmail.com", StringComparison.OrdinalIgnoreCase);
                string msg;
                if (isGmail)
                {
                    msg = "Gmail Authentication Failed. Please ensure 2-Step Verification is ON and you are using a 16-character App Password (not your normal Google password).";
                }
                else
                {
                    msg = $"Authentication failed for user '{smtpUsername}' on SMTP server '{smtpServer}'. Please verify username and password.";
                }
                return (false, msg, (int)stopwatch.ElapsedMilliseconds);
            }
            catch (MailKit.Net.Smtp.SmtpCommandException ex)
            {
                stopwatch.Stop();
                logger?.LogError(ex, "SMTP command error: StatusCode {StatusCode}, Message: {Message}", ex.StatusCode, ex.Message);
                return (false, $"SMTP Server Command Error: {ex.Message} (Status Code: {ex.StatusCode})", (int)stopwatch.ElapsedMilliseconds);
            }
            catch (MailKit.Net.Smtp.SmtpProtocolException ex)
            {
                stopwatch.Stop();
                logger?.LogError(ex, "SMTP protocol error: {Message}", ex.Message);
                return (false, $"SMTP Protocol Error: {ex.Message}. Please check if Port ({smtpPort}) and SSL settings match the server.", (int)stopwatch.ElapsedMilliseconds);
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                stopwatch.Stop();
                logger?.LogError(ex, "Socket error connecting to {Host}:{Port}", smtpServer, smtpPort);
                return (false, $"Unable to connect to SMTP server '{smtpServer}' on port {smtpPort}. {ex.Message}. Check firewall or hostname/port.", (int)stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger?.LogError(ex, "Error sending email to {Recipient}", toEmail);
                var detail = ex.InnerException != null ? $" ({ex.InnerException.Message})" : "";
                return (false, $"{ex.Message}{detail}", (int)stopwatch.ElapsedMilliseconds);
            }
        }
    }
}
