import 'dart:async';
import 'package:battery_plus/battery_plus.dart';
import 'package:flutter/material.dart';
import 'package:flutter_blue_plus/flutter_blue_plus.dart';
import 'package:permission_handler/permission_handler.dart';
import 'package:wakelock_plus/wakelock_plus.dart';
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
  bool _testPrinting = false;
  int  _batteryLevel   = 100;
  bool _batteryCharging = false;
  Timer? _batteryTimer;

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
    _loadBattery();
    _batteryTimer = Timer.periodic(const Duration(minutes: 1), (_) => _loadBattery());
  }

  @override
  void dispose() {
    _keepAliveTimer?.cancel();
    _batteryTimer?.cancel();
    WakelockPlus.disable();
    super.dispose();
  }

  Future<void> _startServerAndRefresh() async {
    try {
      final server = PrinterHttpServer(_printerService);
      await server.start();
      if (mounted) setState(() => _serviceRunning = true);
      // Keep screen + CPU awake so Android doesn't throttle network I/O.
      WakelockPlus.enable();
    } catch (_) {
      if (mounted) setState(() => _serviceRunning = false);
    }
    await _refresh();
  }

  Future<void> _loadBattery() async {
    try {
      final battery = Battery();
      _batteryLevel = await battery.batteryLevel;
      final state = await battery.batteryState;
      _batteryCharging = state == BatteryState.charging || state == BatteryState.full;
      if (mounted) setState(() {});
    } catch (_) {}
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

  // ── Test Print ───────────────────────────────────────────────────────────────

  Future<void> _doTestPrint() async {
    if (_testPrinting) return;
    if (!_status.ready) {
      _showSnack('Pair a printer first before sending a test print.', error: true);
      return;
    }
    setState(() => _testPrinting = true);
    try {
      await _printerService.testPrint();
      await _refresh();
      _showSnack('Test print sent successfully!', success: true);
    } catch (e) {
      await _refresh();
      _showSnack('Test print failed: $e', error: true);
    } finally {
      if (mounted) setState(() => _testPrinting = false);
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
              // ── Battery warning ───────────────────────────────────────────
              _BatteryWarningBar(level: _batteryLevel, charging: _batteryCharging),
              // ── Server badge ─────────────────────────────────────────────
              _StatusBadge(
                active: _serviceRunning,
                activeText: 'HTTP Server running  •  port 9100',
                inactiveText: 'HTTP Server stopped',
                activeIcon: Icons.wifi_tethering,
                inactiveIcon: Icons.wifi_tethering_off,
              ),
              const SizedBox(height: 40),

              // ── Printer icon (animated connection ring) ───────────────────────────
              _PulsingRing(
                connected: _status.connected,
                paired: _status.ready,
                scanning: _scanning,
                icon: Icons.print_rounded,
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

              // ── Last print timestamp ──────────────────────────────────────
              const SizedBox(height: 10),
              _LastPrintLabel(lastPrintAt: _status.lastPrintAt),
              const SizedBox(height: 16),
              // ── Today's print stats ────────────────────────────────────────────
              _TodayStatsBar(
                success: _status.todaySuccess,
                failed: _status.todayFailed,
              ),
              const SizedBox(height: 24),

              // ── Test print button ─────────────────────────────────────────
              if (_status.ready)
                SizedBox(
                  width: double.infinity,
                  child: OutlinedButton.icon(
                    onPressed: (_testPrinting || _scanning) ? null : _doTestPrint,
                    icon: _testPrinting
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.print_outlined),
                    label: Text(_testPrinting ? 'Sending test…' : 'Send Test Print'),
                    style: OutlinedButton.styleFrom(
                      padding: const EdgeInsets.symmetric(vertical: 12),
                    ),
                  ),
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

// ── Last print label ─────────────────────────────────────────────────────────

class _LastPrintLabel extends StatefulWidget {
  final DateTime? lastPrintAt;
  const _LastPrintLabel({this.lastPrintAt});

  @override
  State<_LastPrintLabel> createState() => _LastPrintLabelState();
}

class _LastPrintLabelState extends State<_LastPrintLabel> {
  late Timer _tick;

  @override
  void initState() {
    super.initState();
    // Refresh the relative label every 30 seconds
    _tick = Timer.periodic(const Duration(seconds: 30), (_) {
      if (mounted) setState(() {});
    });
  }

  @override
  void dispose() {
    _tick.cancel();
    super.dispose();
  }

  String _label() {
    final t = widget.lastPrintAt;
    if (t == null) return 'No prints yet this session';
    final diff = DateTime.now().difference(t);
    if (diff.inSeconds < 60) return 'Last print: just now';
    if (diff.inMinutes < 60) return 'Last print: ${diff.inMinutes} min ago';
    if (diff.inHours < 24) return 'Last print: ${diff.inHours} hr ago';
    return 'Last print: ${diff.inDays}d ago';
  }

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        Icon(Icons.access_time, size: 13, color: Colors.grey.shade500),
        const SizedBox(width: 4),
        Text(
          _label(),
          style: TextStyle(fontSize: 12, color: Colors.grey.shade500),
        ),
      ],
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

// ── Animated Pulsing Connection Ring ─────────────────────────────────────────

class _PulsingRing extends StatefulWidget {
  final bool connected;
  final bool paired;
  final bool scanning;
  final IconData icon;

  const _PulsingRing({
    required this.connected,
    required this.paired,
    required this.scanning,
    required this.icon,
  });

  @override
  State<_PulsingRing> createState() => _PulsingRingState();
}

class _PulsingRingState extends State<_PulsingRing>
    with SingleTickerProviderStateMixin {
  late AnimationController _ctrl;
  late Animation<double> _scale;
  late Animation<double> _opacity;

  bool get _shouldPulse => widget.connected || widget.scanning;

  @override
  void initState() {
    super.initState();
    _ctrl = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1400),
    );
    _scale   = Tween<double>(begin: 1.0, end: 1.55).animate(
        CurvedAnimation(parent: _ctrl, curve: Curves.easeOut));
    _opacity = Tween<double>(begin: 0.65, end: 0.0).animate(
        CurvedAnimation(parent: _ctrl, curve: Curves.easeOut));
    if (_shouldPulse) _ctrl.repeat();
  }

  @override
  void didUpdateWidget(_PulsingRing old) {
    super.didUpdateWidget(old);
    if (_shouldPulse && !_ctrl.isAnimating) {
      _ctrl.repeat();
    } else if (!_shouldPulse && _ctrl.isAnimating) {
      _ctrl.stop();
      _ctrl.value = 0;
    }
  }

  @override
  void dispose() {
    _ctrl.dispose();
    super.dispose();
  }

  Color _ringColor(ColorScheme scheme) {
    if (widget.scanning)  return Colors.amber.shade500;
    if (widget.connected) return Colors.green.shade500;
    if (widget.paired)    return scheme.primary;
    return Colors.grey.shade300;
  }

  Color _centerColor(ColorScheme scheme) {
    if (widget.scanning)  return Colors.amber.shade50;
    if (widget.connected) return Colors.green.shade50;
    if (widget.paired)    return scheme.primaryContainer;
    return Colors.grey.shade100;
  }

  Color _iconColor(ColorScheme scheme) {
    if (widget.scanning)  return Colors.amber.shade700;
    if (widget.connected) return Colors.green.shade700;
    if (widget.paired)    return scheme.primary;
    return Colors.grey;
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return SizedBox(
      width: 120,
      height: 120,
      child: AnimatedBuilder(
        animation: _ctrl,
        builder: (_, __) => Stack(
          alignment: Alignment.center,
          children: [
            if (_shouldPulse)
              Transform.scale(
                scale: _scale.value,
                child: Opacity(
                  opacity: _opacity.value,
                  child: Container(
                    width: 100,
                    height: 100,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      border: Border.all(color: _ringColor(scheme), width: 3),
                    ),
                  ),
                ),
              ),
            AnimatedContainer(
              duration: const Duration(milliseconds: 400),
              width: 100,
              height: 100,
              decoration: BoxDecoration(
                color: _centerColor(scheme),
                shape: BoxShape.circle,
              ),
              child: Icon(widget.icon, size: 52, color: _iconColor(scheme)),
            ),
          ],
        ),
      ),
    );
  }
}

// ── Battery Warning Bar ───────────────────────────────────────────────────────

class _BatteryWarningBar extends StatelessWidget {
  final int  level;
  final bool charging;

  const _BatteryWarningBar({required this.level, required this.charging});

  @override
  Widget build(BuildContext context) {
    if (charging || level > 20) return const SizedBox.shrink();
    return Container(
      width: double.infinity,
      margin: const EdgeInsets.only(bottom: 16),
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
      decoration: BoxDecoration(
        color: Colors.red.shade700,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        children: [
          const Icon(Icons.battery_alert, color: Colors.white, size: 18),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              'Battery at $level% — Plug in the charger. Bridge may stop printing.',
              style: const TextStyle(
                color: Colors.white,
                fontSize: 12,
                fontWeight: FontWeight.w500,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

// ── Today's Print Stats Bar ───────────────────────────────────────────────────

class _TodayStatsBar extends StatelessWidget {
  final int success;
  final int failed;

  const _TodayStatsBar({required this.success, required this.failed});

  @override
  Widget build(BuildContext context) {
    final total = success + failed;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: Colors.grey.shade200),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              const Text(
                "Today's Prints",
                style: TextStyle(fontWeight: FontWeight.w600, fontSize: 13),
              ),
              Row(
                children: [
                  Icon(Icons.check_circle, size: 14, color: Colors.green.shade600),
                  const SizedBox(width: 3),
                  Text(
                    '$success',
                    style: TextStyle(
                      color: Colors.green.shade700,
                      fontWeight: FontWeight.bold,
                      fontSize: 13,
                    ),
                  ),
                  const SizedBox(width: 10),
                  Icon(Icons.cancel, size: 14, color: Colors.red.shade600),
                  const SizedBox(width: 3),
                  Text(
                    '$failed',
                    style: TextStyle(
                      color: Colors.red.shade700,
                      fontWeight: FontWeight.bold,
                      fontSize: 13,
                    ),
                  ),
                ],
              ),
            ],
          ),
          const SizedBox(height: 8),
          ClipRRect(
            borderRadius: BorderRadius.circular(4),
            child: total == 0
                ? Container(height: 6, color: Colors.grey.shade200)
                : Row(
                    children: [
                      if (success > 0)
                        Expanded(
                          flex: success,
                          child: Container(height: 6, color: Colors.green.shade400),
                        ),
                      if (failed > 0)
                        Expanded(
                          flex: failed,
                          child: Container(height: 6, color: Colors.red.shade400),
                        ),
                    ],
                  ),
          ),
        ],
      ),
    );
  }
}
