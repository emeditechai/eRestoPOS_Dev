using RestaurantManagementSystem.Services;

// Generate encryption key and IV for configuration
Console.WriteLine("=== AES-256 Encryption Key and IV Generator ===");
Console.WriteLine();
Console.WriteLine("Copy these values to your appsettings.json:");
Console.WriteLine();
Console.WriteLine("\"Encryption\": {");
Console.WriteLine($"  \"Key\": \"{UrlEncryptionService.GenerateKey()}\",");
Console.WriteLine($"  \"IV\": \"{UrlEncryptionService.GenerateIV()}\"");
Console.WriteLine("}");
Console.WriteLine();
Console.WriteLine("IMPORTANT: Keep these values secret and secure!");
Console.WriteLine("For production, use User Secrets or Environment Variables.");
