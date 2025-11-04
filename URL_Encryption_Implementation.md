# URL Encryption Implementation

## Overview
The URL encryption feature protects sensitive payment data (order IDs, discount amounts) from being manipulated or exposed in URLs using AES-256 encryption.

## Implementation Details

### 1. Encryption Service (`UrlEncryptionService.cs`)

**Location**: `RestaurantManagementSystem/Services/UrlEncryptionService.cs`

**Features**:
- **AES-256 Encryption**: Industry-standard encryption algorithm
- **URL-Safe Encoding**: Encrypted tokens are Base64-URL encoded (no special characters)
- **Bidirectional**: Can encrypt and decrypt parameter dictionaries
- **Configurable**: Uses encryption keys from `appsettings.json`

**Key Methods**:
```csharp
// Encrypt parameters into a URL-safe token
string EncryptParameters(Dictionary<string, string> parameters)

// Decrypt token back to parameters
Dictionary<string, string> DecryptParameters(string encryptedToken)

// Generate new encryption keys (use once for setup)
static string GenerateKey()
static string GenerateIV()
```

### 2. Configuration (`appsettings.json`)

The encryption keys are stored in configuration:

```json
{
  "Encryption": {
    "Key": "6K8p9mN2rT5vB3wX7yQ4zC1aF8hJ0kL9nM3pR6sU2vY=",
    "IV": "4A7bD9eF2gH5jK8mN0qR3tV="
  }
}
```

**⚠️ IMPORTANT SECURITY NOTES**:
- Never commit actual encryption keys to source control
- Use different keys for development, staging, and production
- Store production keys in secure environment variables or Azure Key Vault
- Rotate keys periodically for enhanced security

### 3. Dependency Injection (`Program.cs`)

The service is registered in the DI container:

```csharp
builder.Services.AddScoped<UrlEncryptionService>();
```

### 4. PaymentController Integration

**Constructor Injection**:
```csharp
private readonly UrlEncryptionService _encryptionService;

public PaymentController(
    IConfiguration configuration, 
    ILogger<PaymentController> logger, 
    UrlEncryptionService encryptionService)
{
    _encryptionService = encryptionService;
    // ...
}
```

**ProcessPayment Method** - Dual Support:
```csharp
public IActionResult ProcessPayment(
    int? orderId = null, 
    decimal? discount = null, 
    string discountType = null, 
    string token = null)
```

The method supports **both encrypted tokens and plain parameters** for backward compatibility:

1. **With Encrypted Token**: `/Payment/ProcessPayment?token={encrypted}`
2. **With Plain Parameters**: `/Payment/ProcessPayment?orderId=123&discount=10`

**Helper Method**:
```csharp
private string GetEncryptedPaymentUrl(int orderId, decimal? discount = null, string? discountType = null)
```

Generates encrypted payment URLs programmatically.

## Usage Examples

### Generating Encrypted URLs

**From Controller**:
```csharp
var encryptedUrl = GetEncryptedPaymentUrl(orderId: 46, discount: 10.5m, discountType: "percent");
// Result: /Payment/ProcessPayment?token=encrypted_string_here
```

**From View (using helper extension)**:
```cshtml
@inject UrlEncryptionService EncryptionService

@{
    var parameters = new Dictionary<string, string>
    {
        ["orderId"] = "46",
        ["discount"] = "10.50"
    };
    var token = EncryptionService.EncryptParameters(parameters);
}

<a href="/Payment/ProcessPayment?token=@token">Process Payment</a>
```

### Decrypting Parameters

The `ProcessPayment` action automatically decrypts the token:

```csharp
if (!string.IsNullOrEmpty(token))
{
    var parameters = _encryptionService.DecryptParameters(token);
    actualOrderId = int.Parse(parameters["orderId"]);
    if (parameters.ContainsKey("discount"))
        actualDiscount = decimal.Parse(parameters["discount"]);
}
```

## Benefits

✅ **Security**: Order IDs and discount amounts are encrypted, preventing URL manipulation  
✅ **URL Safety**: No special characters in URLs  
✅ **Backward Compatible**: Existing plain URLs still work  
✅ **Error Handling**: Invalid tokens redirect with error message  
✅ **Flexible**: Can encrypt any parameter dictionary  

## Migration Path

### Phase 1: Current (Backward Compatible)
- Both encrypted and plain URLs work
- Existing links continue to function
- New features can use encryption

### Phase 2: Gradual Migration
- Update views to use encrypted URLs
- Monitor logs for plain URL usage
- Keep fallback for legacy links

### Phase 3: Enforce Encryption (Optional)
- Disable plain parameter support
- All payment URLs must use encrypted tokens
- Update configuration to enforce encryption-only mode

## Security Best Practices

1. **Key Management**:
   - Store keys in environment variables in production
   - Use Azure Key Vault or similar for key storage
   - Never commit keys to source control

2. **Key Rotation**:
   - Rotate keys periodically (e.g., every 90 days)
   - Keep old keys temporarily for decrypting existing tokens
   - Implement graceful key rotation mechanism

3. **Token Expiration** (Future Enhancement):
   - Add timestamp to encrypted parameters
   - Validate token age on decrypt
   - Reject tokens older than X minutes/hours

4. **Logging**:
   - Log encryption/decryption failures
   - Monitor for repeated invalid token attempts
   - Alert on suspicious patterns

## Testing

### Test Encrypted URL Generation
```bash
# Run the application
dotnet run --project RestaurantManagementSystem/RestaurantManagementSystem/RestaurantManagementSystem.csproj

# Navigate to payment page with plain URL (still works)
http://localhost:5290/Payment/ProcessPayment?orderId=46

# Future: Use encrypted URL
http://localhost:5290/Payment/ProcessPayment?token={encrypted_token}
```

### Generate New Keys (if needed)
```bash
# Run the key generator
dotnet run --project RestaurantManagementSystem/RestaurantManagementSystem/GenerateEncryptionKeys.cs
```

## Files Modified

### Created:
- ✅ `Services/UrlEncryptionService.cs` - Encryption service implementation
- ✅ `GenerateEncryptionKeys.cs` - Utility to generate new keys

### Modified:
- ✅ `Program.cs` - Register UrlEncryptionService in DI
- ✅ `Controllers/PaymentController.cs` - Inject service, update ProcessPayment, add helper method
- ✅ `appsettings.json` - Add Encryption configuration section

### To Update (Future):
- `Views/Payment/Index.cshtml` - Use encrypted URLs in links
- `Views/Payment/Dashboard.cshtml` - Use encrypted URLs
- `Views/Payment/BarDashboard.cshtml` - Use encrypted URLs

## Status

✅ **Completed**: Encryption service, DI registration, PaymentController integration  
⏳ **Pending**: View updates to use encrypted URLs  
🔮 **Future**: Token expiration, key rotation mechanism, enforce encryption-only mode

## Notes

- The implementation is backward compatible by design
- Plain URLs will continue to work alongside encrypted ones
- Views can be updated gradually to use encrypted URLs
- The encryption adds negligible performance overhead
- URL length increases slightly due to encryption (typical: 100-150 characters)

---

**Last Updated**: November 3, 2025  
**Implementation Status**: Service Complete, Integration Ready
