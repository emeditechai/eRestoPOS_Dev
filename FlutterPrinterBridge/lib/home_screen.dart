import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter_blue_plus/flutter_blue_plus.dart';
import 'package:permission_handler/permission_handler.dart';
import 'printer_service.dart';
import 'http_server.dart';

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  final _printerService = PrinterService();
  PrinterStatus _status = const PrinterStatus(ready: false, connected: false);
  bool _serviceRunning = false;
  bool _scanning = false;

  // Keep-alive timer: fires every 30s to keep the Dart event loop active.
  // This prevents Android from throttling the HTTP server's network I/O
  // when the app is backgrounded for several minutes.
  Timer? _keepAliveTimer;

  @override
  void initState() {
    super.initState();
    _startServerAndRefresh();
    _keepAliveTimer = Timer.periodic(
      const Duration(seconds: 30),
      (_) => _refresh(),
    );
  }

  @override
  void dispose() {
    _keepAliveTimer?.cancel();
    super.dispose();
  }

  Future<void> _startServerAndRefresh() async {
    try {
      final server = PrinterHttpServer(_printerService);
      await server.start();
      if (mounted) setState(() => _serviceRunning = true);
    } catch (_) {
      if (mounted) setState(() => _serviceRunning = false);
    }
    await _refresh();
  }

  Future<void> _refresh() async {
    await _printerService.loadSaved();
    if (mounted) {
      setState(() {
        _status = _printerService.getStatus();
      });
    }
  }

  // ── Permissions ─────────────────────────────────────────────────────────────

  Future<bool> _requestBluetooth() async {
    // Do NOT include Permission.bluetooth (legacy) — it is declared with
    // maxSdkVersion="30" in the manifest, so permission_handler returns
    // 'denied' on Android 12+ even when everything is granted.
    final statuses = await [
      Permission.bluetoothScan,
      Permission.bluetoothConnect,
      Permission.locationWhenInUse,
    ].request();

    final permanentlyDenied = statuses.values.any((s) => s.isPermanentlyDenied);
    if (permanentlyDenied && mounted) {
      _showSnack('Bluetooth permission denied. Please allow it in Settings → Apps → Print Bridge → Permissions.', error: true);
      return false;
    }

    final denied = statuses.values.any((s) => s.isDenied);
    if (denied && mounted) {
      _showSnack('Please grant all Bluetooth permissions and try again.', error: true);
      return false;
    }

    // Check Bluetooth is switched on
    try {
      final adapterState = await FlutterBluePlus.adapterState.first;
      if (adapterState != BluetoothAdapterState.on) {
        if (mounted) _showSnack('Please turn on Bluetooth and try again.', error: true);
        return false;
      }
    } catch (_) {
      // Some devices don't support adapterState check — proceed anyway
    }

    return true;
  }

  // ── Pair ────────────────────────────────────────────────────────────────────

  Future<void> _pairPrinter() async {
    final ok = await _requestBluetooth();
    if (!ok) return;

    setState(() => _scanning = true);
    _showSnack('Scanning for devices (10 seconds)…');

    try {
      final devices = await _printerService.scanDevices();

      if (!mounted) return;
      setState(() => _scanning = false);

      if (devices.isEmpty) {
        _showSnack('No Bluetooth devices found. Make sure the printer is ON and nearby.', error: true);
        return;
      }

      // Show picker dialog
      final picked = await showDialog<BluetoothDevice>(
        context: context,
        builder: (_) => AlertDialog(
          title: const Text('Select your printer'),
          content: SizedBox(
            width: double.maxFinite,
            child: ListView.builder(
              shrinkWrap: true,
              itemCount: devices.length,
              itemBuilder: (_, i) {
                final d = devices[i];
                return ListTile(
                  leading: const Icon(Icons.print),
                  title: Text(d.platformName.isNotEmpty ? d.platformName : 'Unknown device'),
                  subtitle: Text(d.remoteId.str),
                  onTap: () => Navigator.pop(context, d),
                );
              },
            ),
          ),
          actions: [
            TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
          ],
        ),
      );

      if (picked == null) return;

      setState(() => _scanning = true);
      _showSnack('Connecting to ${picked.platformName}…');
      final name = await _printerService.pairDevice(picked);

      if (mounted) {
        _showSnack('Paired: $name', error: false, success: true);
        await _refresh();
      }
    } catch (e) {
      if (mounted) _showSnack('Error: $e', error: true);
    } finally {
      if (mounted) setState(() => _scanning = false);
    }
  }

  // ── Forget ──────────────────────────────────────────────────────────────────

  Future<void> _forgetPrinter() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('Remove Printer'),
        content: Text('Remove ${_status.printerName ?? 'this printer'}?'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          TextButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Remove', style: TextStyle(color: Colors.red)),
          ),
        ],
      ),
    );
    if (confirmed != true) return;

    await _printerService.forget();
    await _refresh();
  }

  // ── Snack ────────────────────────────────────────────────────────────────────

  void _showSnack(String msg, {bool error = false, bool success = false}) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
      content: Text(msg),
      backgroundColor: error ? Colors.red.shade700 : success ? Colors.green.shade700 : null,
      duration: Duration(seconds: error ? 5 : 3),
    ));
  }

  // ── UI ───────────────────────────────────────────────────────────────────────

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Scaffold(
      backgroundColor: scheme.surface,
      appBar: AppBar(
        titleSpacing: 12,
        title: Image.asset('assets/applogo.png', height: 32, fit: BoxFit.contain),
        backgroundColor: Colors.white,
        elevation: 1,
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: _refresh,
            tooltip: 'Refresh',
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: _refresh,
        child: Center(
          child: ConstrainedBox(
            // Limit width on tablets — content looks bad stretched to 10"
            constraints: const BoxConstraints(maxWidth: 600),
            child: SingleChildScrollView(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 32),
              child: Column(
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              // ── Server badge ─────────────────────────────────────────────
              _StatusBadge(
                active: _serviceRunning,
                activeText: 'HTTP Server running  •  port 9100',
                inactiveText: 'HTTP Server stopped',
                activeIcon: Icons.wifi_tethering,
                inactiveIcon: Icons.wifi_tethering_off,
              ),
              const SizedBox(height: 40),

              // ── Printer icon ─────────────────────────────────────────────
              AnimatedContainer(
                duration: const Duration(milliseconds: 400),
                width: 100,
                height: 100,
                decoration: BoxDecoration(
                  color: _status.ready ? scheme.primaryContainer : Colors.grey.shade100,
                  shape: BoxShape.circle,
                ),
                child: Icon(
                  Icons.print_rounded,
                  size: 52,
                  color: _status.ready ? scheme.primary : Colors.grey,
                ),
              ),
              const SizedBox(height: 16),

              // ── Printer name ─────────────────────────────────────────────
              Text(
                _status.printerName ?? 'No Printer Paired',
                style: Theme.of(context).textTheme.titleLarge?.copyWith(
                      fontWeight: FontWeight.bold,
                      color: _status.ready ? scheme.primary : Colors.grey.shade600,
                    ),
              ),
              const SizedBox(height: 6),

              if (_status.connected)
                Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Icon(Icons.bluetooth_connected, size: 14, color: Colors.green.shade600),
                    const SizedBox(width: 4),
                    Text(
                      'Connected',
                      style: TextStyle(color: Colors.green.shade700, fontSize: 13),
                    ),
                  ],
                )
              else if (_status.ready)
                Text(
                  'Not connected — will connect on next print',
                  style: TextStyle(color: Colors.grey.shade500, fontSize: 13),
                ),

              const SizedBox(height: 40),

              // ── Pair button ──────────────────────────────────────────────
              SizedBox(
                width: double.infinity,
                child: FilledButton.icon(
                  onPressed: _scanning ? null : _pairPrinter,
                  icon: _scanning
                      ? const SizedBox(
                          width: 18,
                          height: 18,
                          child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                        )
                      : const Icon(Icons.bluetooth_searching),
                  label: Text(_scanning
                      ? 'Scanning…'
                      : _status.ready
                          ? 'Change Printer'
                          : 'Pair Printer'),
                  style: FilledButton.styleFrom(padding: const EdgeInsets.symmetric(vertical: 14)),
                ),
              ),

              if (_status.ready) ...[
                const SizedBox(height: 12),
                SizedBox(
                  width: double.infinity,
                  child: OutlinedButton.icon(
                    onPressed: _forgetPrinter,
                    icon: const Icon(Icons.link_off, color: Colors.red),
                    label: const Text('Remove Printer', style: TextStyle(color: Colors.red)),
                    style: OutlinedButton.styleFrom(
                      side: const BorderSide(color: Colors.red),
                      padding: const EdgeInsets.symmetric(vertical: 14),
                    ),
                  ),
                ),
              ],

              const SizedBox(height: 48),

              // ── Stats dashboard ──────────────────────────────────────────
              Row(
                children: [
                  Expanded(
                    child: _StatCard(
                      label: 'Successful',
                      count: _status.printSuccess,
                      icon: Icons.check_circle_outline,
                      color: Colors.green.shade700,
                      bg: Colors.green.shade50,
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: _StatCard(
                      label: 'Failed',
                      count: _status.printFailed,
                      icon: Icons.error_outline,
                      color: Colors.red.shade700,
                      bg: Colors.red.shade50,
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: _StatCard(
                      label: 'Total',
                      count: _status.printSuccess + _status.printFailed,
                      icon: Icons.receipt_long,
                      color: scheme.primary,
                      bg: scheme.primaryContainer,
                    ),
                  ),
                ],
              ),

              const SizedBox(height: 32),

              // ── Instructions ─────────────────────────────────────────────
              Container(
                padding: const EdgeInsets.all(16),
                decoration: BoxDecoration(
                  color: scheme.surfaceContainerHighest,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('How to use', style: TextStyle(fontWeight: FontWeight.bold, color: scheme.primary)),
                    const SizedBox(height: 8),
                    const _Step(n: '1', text: 'Keep this app running (minimize — don\'t close)'),
                    const _Step(n: '2', text: 'Pair your thermal printer using the button above'),
                    const _Step(n: '3', text: 'Open your restaurant app in Chrome and print'),
                    const _Step(n: '4', text: 'No Bluetooth picker will appear — it prints silently!'),
                  ],
                ),
              ),

              const SizedBox(height: 32),

              // ── Copyright ────────────────────────────────────────────────
              const Text(
                'Copyright Reserved Emeditech Plus LLP',
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 11, color: Colors.grey),
              ),
              const SizedBox(height: 8),
            ],
          ),
        ),
        ),
        ),
      ),
    );
  }
}

// ── Stat card ────────────────────────────────────────────────────────────────

class _StatCard extends StatelessWidget {
  final String label;
  final int count;
  final IconData icon;
  final Color color;
  final Color bg;

  const _StatCard({
    required this.label,
    required this.count,
    required this.icon,
    required this.color,
    required this.bg,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(vertical: 14, horizontal: 8),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        children: [
          Icon(icon, color: color, size: 24),
          const SizedBox(height: 6),
          Text(
            '$count',
            style: TextStyle(fontSize: 22, fontWeight: FontWeight.bold, color: color),
          ),
          const SizedBox(height: 2),
          Text(label, style: TextStyle(fontSize: 11, color: color)),
        ],
      ),
    );
  }
}

// ── Small widgets ────────────────────────────────────────────────────────────

class _StatusBadge extends StatelessWidget {
  final bool active;
  final String activeText;
  final String inactiveText;
  final IconData activeIcon;
  final IconData inactiveIcon;

  const _StatusBadge({
    required this.active,
    required this.activeText,
    required this.inactiveText,
    required this.activeIcon,
    required this.inactiveIcon,
  });

  @override
  Widget build(BuildContext context) {
    return AnimatedContainer(
      duration: const Duration(milliseconds: 300),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
      decoration: BoxDecoration(
        color: active ? Colors.green.shade50 : Colors.grey.shade100,
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: active ? Colors.green.shade200 : Colors.grey.shade300),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(active ? activeIcon : inactiveIcon,
              size: 15, color: active ? Colors.green.shade700 : Colors.grey),
          const SizedBox(width: 6),
          Text(
            active ? activeText : inactiveText,
            style: TextStyle(
              fontSize: 12,
              color: active ? Colors.green.shade700 : Colors.grey.shade600,
              fontWeight: FontWeight.w500,
            ),
          ),
        ],
      ),
    );
  }
}

class _Step extends StatelessWidget {
  final String n;
  final String text;

  const _Step({required this.n, required this.text});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(top: 6),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          CircleAvatar(
            radius: 10,
            backgroundColor: Theme.of(context).colorScheme.primary,
            child: Text(n, style: const TextStyle(fontSize: 11, color: Colors.white)),
          ),
          const SizedBox(width: 10),
          Expanded(child: Text(text, style: const TextStyle(fontSize: 13))),
        ],
      ),
    );
  }
}
