# Restaurant Management System - Hotkey Documentation

## Overview
A comprehensive keyboard shortcut system has been implemented to improve workflow efficiency and speed up common operations across the restaurant management application.

## Global Navigation Hotkeys

### From Home Dashboard (`/`)

| Hotkey | Action | Description |
|--------|--------|-------------|
| **Shift + F** | Open Food Order Page | Navigate to `/Order/Create` to create a new food order |
| **Shift + K** | Open Kitchen Dashboard | Navigate to `/Kitchen/Dashboard` to view kitchen operations |
| **Shift + T** | Open Table Dashboard | Navigate to `/TableService/Dashboard` to manage tables |

## Order Management Hotkeys

### Order Create Page (`/Order/Create`)

| Hotkey | Action | Description |
|--------|--------|-------------|
| **Shift + O** | Create Order | Submit the order creation form |

### Order Details Page (`/Order/Details/{id}`)

| Hotkey | Action | Description |
|--------|--------|-------------|
| **Space** | Focus Menu Item | Focus and open the menu item search dropdown |
| **Enter** (in Menu Input) | Move to Quantity | After selecting menu item, move focus to quantity field |
| **Enter** (in Quantity) | Add Item | Add the selected menu item to the order grid |
| **Ctrl + S** | Save Order | Save all changes to the current order |
| **Ctrl + F** | Fire to Kitchen | Open the fire items modal to send items to kitchen |
| **Ctrl + P** | Open Payment | Navigate to payment page for the current order |

### Enhanced Workflow: Adding Menu Items
1. Press **Space** to focus menu item search
2. Type to search and select a menu item
3. Press **Enter** to move to quantity field
4. Enter quantity and press **Enter** to add to grid
5. Repeat steps 1-4 for additional items
6. Press **Ctrl + S** to save order
7. Press **Ctrl + F** to fire to kitchen
8. Press **Ctrl + P** to process payment

## Payment Hotkeys

### Payment Index Page (`/Payment/Index/{id}`)

| Hotkey | Action | Description |
|--------|--------|-------------|
| **Ctrl + P** | Process Payment | Click "Process Payment" button to complete the transaction |

## Technical Implementation

### Features
- **Context-Aware**: Hotkeys only work on their designated pages
- **Safe Input Handling**: Hotkeys are disabled when typing in input fields (except Space bar)
- **Visual Feedback**: Toast notifications show which hotkey was triggered
- **Button State Validation**: Checks if buttons are disabled before triggering actions
- **Cross-Platform**: Supports both Ctrl (Windows/Linux) and Cmd (Mac) keys
- **Non-Intrusive**: Does not interfere with existing functionality

### Implementation Location
- **File**: `/Views/Shared/_Layout.cshtml`
- **Type**: Global JavaScript implementation
- **Scope**: Automatically loaded on all pages

### Configuration
The hotkey system can be configured via the `HOTKEY_CONFIG` object:

```javascript
const HOTKEY_CONFIG = {
    enabled: true,              // Enable/disable entire hotkey system
    showNotifications: true,    // Show visual feedback notifications
    notificationDuration: 2500  // Notification display time in milliseconds
};
```

## Safety Features

1. **Input Protection**: Hotkeys won't trigger while user is typing in text fields
2. **Button Validation**: Checks if target buttons are disabled or have `btn-disabled` class
3. **Page Context**: Each hotkey only works on its designated page
4. **Non-Conflicting**: Uses standard modifier keys (Shift, Ctrl) to avoid conflicts
5. **Graceful Degradation**: If target elements don't exist, hotkeys safely do nothing

## Browser Console
When the page loads, available hotkeys are logged to the browser console for quick reference. Press F12 and check the console to see:

```
🎹 Hotkey System Loaded
Available Hotkeys:
  Shift+F - Open Food Order (Home)
  Shift+K - Open Kitchen Dashboard (Home)
  Shift+T - Open Table Dashboard (Home)
  Shift+O - Create Order (Order Create)
  Space   - Focus Menu Item (Order Details)
  Enter   - Select Menu → Qty → Add (Order Details)
  Ctrl+S  - Save Order (Order Details)
  Ctrl+F  - Fire to Kitchen (Order Details)
  Ctrl+P  - Payment / Process Payment
```

## Customization

To add new hotkeys, edit `/Views/Shared/_Layout.cshtml` and:

1. Add a new function following the naming pattern `hotkeyXxxYy(e)`
2. Implement page detection and action logic
3. Add the handler to the main `keydown` event listener
4. Update this documentation

## Testing Checklist

- [ ] Test all navigation hotkeys from Home Dashboard
- [ ] Test Shift+O on Order Create page
- [ ] Test Space bar on Order Details page
- [ ] Test Enter key flow (Menu → Qty → Add)
- [ ] Test Ctrl+S to save order
- [ ] Test Ctrl+F to fire to kitchen
- [ ] Test Ctrl+P on Order Details page
- [ ] Test Ctrl+P on Payment Index page
- [ ] Verify hotkeys don't interfere with normal typing
- [ ] Verify disabled buttons are not triggered
- [ ] Test on different browsers (Chrome, Firefox, Safari, Edge)
- [ ] Test on Mac (Cmd key) and Windows (Ctrl key)

## Troubleshooting

**Hotkeys not working?**
- Check browser console for JavaScript errors
- Verify you're on the correct page for the hotkey
- Ensure target buttons are not disabled
- Check if `HOTKEY_CONFIG.enabled` is set to `true`

**Notifications not showing?**
- Set `HOTKEY_CONFIG.showNotifications` to `true`
- Check if CSS animations are enabled in browser

**Conflicts with browser shortcuts?**
- Most hotkeys use Shift or Ctrl modifiers to avoid conflicts
- Browser's Ctrl+P (print) is intentionally overridden on specific pages

## Future Enhancements

- Add hotkey help modal (Shift + ?)
- Add customizable user preferences
- Add hotkey for search functionality
- Add hotkey for reports navigation
- Add keyboard navigation for table selection
- Add quick print hotkeys

## Version History

- **v1.0** (Current) - Initial implementation with core navigation and order management hotkeys

## Support

For issues or suggestions regarding the hotkey system, please contact the development team or create an issue in the project repository.
