# Print Bridge — Complete Setup & Usage Guide

## What is Print Bridge?

Print Bridge is a small Android app that runs a local server on your phone.  
When you print a receipt from the restaurant browser app, Chrome silently sends the data to Print Bridge, which prints it over Bluetooth — **no popup, no picker, no permission dialog** every time.

---

## Part A — One-Time Developer Setup (Build the APK)

> Do this once on the Mac. After this, staff just download the APK from the app.

### Step 1 — Install Android Studio
Android Studio is already downloading via Homebrew. Once done:
1. Open **Android Studio** from `/Applications`
2. First launch shows "Android SDK Setup" → click **Next → Next → Finish**
3. Wait for SDK components to download (~3–5 min)

### Step 2 — Add environment variables
Open Terminal and run:
```bash
echo 'export ANDROID_HOME="$HOME/Library/Android/sdk"' >> ~/.zshrc
echo 'export PATH="$PATH:$ANDROID_HOME/platform-tools"' >> ~/.zshrc
echo 'export PATH="$PATH:/opt/homebrew/Caskroom/flutter/3.41.8/flutter/bin"' >> ~/.zshrc
source ~/.zshrc
```

Verify:
```bash
flutter doctor
# Should show: Android SDK ✓, Flutter ✓
```

### Step 3 — Build the APK
```bash
cd /Users/abhikporel/dev/Restaurantapp/FlutterPrinterBridge
flutter build apk --release
```
Output: `build/app/outputs/flutter-apk/app-release.apk`

### Step 4 — Copy APK into the web project
```bash
cp build/app/outputs/flutter-apk/app-release.apk \
   ../RestaurantManagementSystem/RestaurantManagementSystem/wwwroot/download/printer-bridge.apk
```

### Step 5 — Publish the web app
```bash
cd /Users/abhikporel/dev/Restaurantapp
dotnet publish RestaurantManagementSystem/RestaurantManagementSystem/RestaurantManagementSystem.csproj \
  -c Release -o /Users/abhikporel/Publish/Restaurant_Publish --nologo
```
The APK is now available at:  
`https://198.38.81.123:9005/Utility/PrinterSetup` → Download App button  
or directly: `https://198.38.81.123:9005/Utility/DownloadPrinterApp`

---

## Part B — Staff Phone Setup (One-time per phone)

### Step 1 — Allow installs from Chrome
1. Go to phone **Settings → Apps → Special App Access → Install Unknown Apps**
2. Find **Chrome** → turn ON "Allow from this source"

> This is a one-time Android security step. Only needs to be done once.

### Step 2 — Download and install the app
1. Open **Chrome** on the Android phone
2. Go to: `https://198.38.81.123:9005/Utility/PrinterSetup`
3. Tap **"Download App (.apk)"**
4. Once downloaded, tap the file → **Install**
5. If Android shows "Unsafe app" warning → tap **"More details" → "Install anyway"**

### Step 3 — Grant Bluetooth permissions
1. Open **Print Bridge** app (purple printer icon)
2. Android will ask for permissions:
   - **Bluetooth** → Allow
   - **Nearby devices** (Android 12+) → Allow
   - **Location** (Android 11 and below) → Allow While Using App
3. Make sure **Bluetooth is ON** on the phone

### Step 4 — Pair the thermal printer
1. **Turn on the thermal printer** (make sure it is powered and nearby)
2. In the Print Bridge app, tap **"Pair Printer"**
3. Phone will scan for nearby BLE printers
4. Your printer will appear in the list — tap it
5. App shows: **"Paired: [Printer Name] — Ready to print"**

### Step 5 — Keep the app running
- **Minimize** the app (press the home button or swipe up)
- **Do NOT force-close** it from the recent apps
- You will see a permanent notification: **"Print Bridge — Paired: [name] — Ready to print"**
- This notification means the app is running correctly in the background

---

## Part C — Daily Printing (Normal Usage)

Once set up, printing is completely automatic:

1. Staff opens restaurant app in **Chrome** on phone
2. Creates / completes an order → taps **Print**
3. Receipt prints immediately — **no dialogs, no popups**

### If printing stops working
Check in this order:
| Problem | Fix |
|---|---|
| Notification "Print Bridge" is gone | Open the app again → it will restart the service |
| Printer name shows "No printer paired" | Open app → tap Pair Printer again |
| Print still shows Bluetooth picker | App is not running — open it and minimize |
| Bluetooth off on phone | Turn Bluetooth on |
| Printer is off | Turn printer on — app will reconnect automatically |

---

## Part D — APK Update Process

When a new version of Print Bridge is released:

### Developer (Mac)
```bash
# 1. Rebuild APK
cd /Users/abhikporel/dev/Restaurantapp/FlutterPrinterBridge
flutter build apk --release

# 2. Copy to web project
cp build/app/outputs/flutter-apk/app-release.apk \
   ../RestaurantManagementSystem/RestaurantManagementSystem/wwwroot/download/printer-bridge.apk

# 3. Publish
cd ..
dotnet publish RestaurantManagementSystem/RestaurantManagementSystem/RestaurantManagementSystem.csproj \
  -c Release -o /Users/abhikporel/Publish/Restaurant_Publish --nologo
```

### Staff (phone)
1. Go to `https://198.38.81.123:9005/Utility/PrinterSetup`
2. Tap **Download App** again
3. Install over the existing version (paired printer settings are kept)

---

## Part E — How It Works (Technical Summary)

```
Chrome (HTTPS) → fetch http://127.0.0.1:9100/status → App running?
                                                              │
                                          YES ───────────────┼──────── NO
                                           │                            │
                              POST /print (base64 ESC/POS)    Falls back to Web Bluetooth
                                           │
                              Flutter app → BLE (by MAC address)
                                           │
                                    Thermal Printer
                                     ✓ Silent print
```

- Port `9100` is local only (127.0.0.1) — not accessible on the network
- Chrome on Android allows `https → http://localhost` (W3C spec)
- Flutter uses real MAC address → always reconnects silently
- Web Bluetooth fallback works if app is not installed (laptop/desktop users)

---

## Quick Reference Card

| Task | Where |
|---|---|
| Download APK | `https://198.38.81.123:9005/Utility/PrinterSetup` |
| Check app status (on phone) | Same page — shows "App running" badge |
| Pair printer | Print Bridge app → Pair Printer button |
| Re-pair if changed printer | Print Bridge app → Change Printer |
| Remove printer | Print Bridge app → Remove Printer |
| App running indicator | Permanent notification in phone notification bar |
