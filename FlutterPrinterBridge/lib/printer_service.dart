import 'dart:async';
import 'dart:typed_data';
import 'package:flutter_blue_plus/flutter_blue_plus.dart';
import 'package:shared_preferences/shared_preferences.dart';

// ── Known BLE service / characteristic UUIDs for common thermal printers ──────
// Must match the same lists in printer-manager.js on the web side.
const List<String> _kServiceUUIDs = [
  '000018f0-0000-1000-8000-00805f9b34fb',
  '6e400001-b5a3-f393-e0a9-e50e24dcca9e',
  '49535343-fe7d-4ae5-8fa9-9fafd205e455',
  '0000ff00-0000-1000-8000-00805f9b34fb',
];

const List<String> _kWriteCharUUIDs = [
  '00002af1-0000-1000-8000-00805f9b34fb',
  '6e400002-b5a3-f393-e0a9-e50e24dcca9e',
  '49535343-8841-43f4-a8d4-ecbe34729bb3',
  '0000ff02-0000-1000-8000-00805f9b34fb',
];

const _kPrefMac    = 'printer_mac';
const _kPrefName   = 'printer_name';
const _kPrefSvcUUID = 'printer_svc_uuid';
const _kPrefChrUUID = 'printer_chr_uuid';

class PrinterStatus {
  final bool ready;
  final String? printerName;
  final bool connected;
  final int printSuccess;
  final int printFailed;
  const PrinterStatus({
    required this.ready,
    this.printerName,
    required this.connected,
    this.printSuccess = 0,
    this.printFailed  = 0,
  });

  Map<String, dynamic> toJson() => {
    'ready': ready,
    'printerName': printerName ?? '',
    'connected': connected,
    'printSuccess': printSuccess,
    'printFailed':  printFailed,
  };
}

class PrinterService {
  String? _savedMac;
  String? _savedName;
  String? _savedSvcUUID;
  String? _savedChrUUID;

  BluetoothDevice? _device;
  BluetoothCharacteristic? _characteristic;

  // Guard against concurrent print calls
  bool _isPrinting = false;

  // Print stats
  int _printSuccess = 0;
  int _printFailed  = 0;

  String? get savedName    => _savedName;
  String? get savedMac     => _savedMac;
  int     get printSuccess => _printSuccess;
  int     get printFailed  => _printFailed;

  // ── Persistence ──────────────────────────────────────────────────────────────

  Future<void> loadSaved() async {
    final prefs = await SharedPreferences.getInstance();
    _savedMac    = prefs.getString(_kPrefMac);
    _savedName   = prefs.getString(_kPrefName);
    _savedSvcUUID = prefs.getString(_kPrefSvcUUID);
    _savedChrUUID = prefs.getString(_kPrefChrUUID);
  }

  Future<void> _persistPrinter({
    required String mac,
    required String name,
    required String svcUUID,
    required String chrUUID,
  }) async {
    _savedMac     = mac;
    _savedName    = name;
    _savedSvcUUID = svcUUID;
    _savedChrUUID = chrUUID;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_kPrefMac,     mac);
    await prefs.setString(_kPrefName,    name);
    await prefs.setString(_kPrefSvcUUID, svcUUID);
    await prefs.setString(_kPrefChrUUID, chrUUID);
  }

  Future<void> forget() async {
    _device         = null;
    _characteristic = null;
    _savedMac       = null;
    _savedName      = null;
    _savedSvcUUID   = null;
    _savedChrUUID   = null;
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_kPrefMac);
    await prefs.remove(_kPrefName);
    await prefs.remove(_kPrefSvcUUID);
    await prefs.remove(_kPrefChrUUID);
  }

  // ── Status ───────────────────────────────────────────────────────────────────

  PrinterStatus getStatus() {
    final connected = _device?.isConnected ?? false;
    return PrinterStatus(
      ready: _savedMac != null,
      printerName: _savedName,
      connected: connected,
      printSuccess: _printSuccess,
      printFailed: _printFailed,
    );
  }

  // ── BLE Scan + Pair (runs on UI isolate) ─────────────────────────────────────

  /// Scans for nearby BLE devices and returns a list of found devices.
  /// Caller shows a picker dialog; user selects the printer.
  Future<List<BluetoothDevice>> scanDevices() async {
    final found = <String, BluetoothDevice>{};

    final sub = FlutterBluePlus.onScanResults.listen((results) {
      for (final r in results) {
        // Only include named devices
        if (r.device.platformName.isNotEmpty) {
          found[r.device.remoteId.str] = r.device;
        }
      }
    });

    await FlutterBluePlus.startScan(timeout: const Duration(seconds: 10));
    await FlutterBluePlus.isScanning.where((s) => !s).first;
    sub.cancel();

    return found.values.toList();
  }

  /// Connects to chosen device, discovers service/char UUIDs, persists.
  Future<String> pairDevice(BluetoothDevice device) async {
    String? foundSvcUUID;
    String? foundChrUUID;

    try {
      await device.connect(timeout: const Duration(seconds: 15));
      final services = await device.discoverServices();

      // Try known printer UUIDs first
      outer:
      for (final svc in services) {
        final svcId = svc.serviceUuid.str128.toLowerCase();
        if (_kServiceUUIDs.contains(svcId)) {
          for (final chr in svc.characteristics) {
            final chrId = chr.characteristicUuid.str128.toLowerCase();
            if (_kWriteCharUUIDs.contains(chrId)) {
              foundSvcUUID = svcId;
              foundChrUUID = chrId;
              break outer;
            }
          }
        }
      }

      // Fallback: find any writable characteristic
      if (foundSvcUUID == null) {
        outer2:
        for (final svc in services) {
          for (final chr in svc.characteristics) {
            if (chr.properties.write || chr.properties.writeWithoutResponse) {
              foundSvcUUID = svc.serviceUuid.str128.toLowerCase();
              foundChrUUID = chr.characteristicUuid.str128.toLowerCase();
              break outer2;
            }
          }
        }
      }
    } finally {
      try { await device.disconnect(); } catch (_) {}
    }

    final mac  = device.remoteId.str;
    final name = device.platformName.isNotEmpty ? device.platformName : 'Thermal Printer';

    await _persistPrinter(
      mac:     mac,
      name:    name,
      svcUUID: foundSvcUUID ?? '',
      chrUUID: foundChrUUID ?? '',
    );

    return name;
  }

  // ── BLE Print ────────────────────────────────────────────────────────────────

  Future<void> print(Uint8List bytes) async {
    if (_isPrinting) throw Exception('Already printing — please wait');
    _isPrinting = true;
    try {
      await _ensureConnected();
      await _writeChunked(_characteristic!, bytes);
      _printSuccess++;
    } catch (e) {
      _printFailed++;
      rethrow;
    } finally {
      _isPrinting = false;
    }
  }

  Future<void> _ensureConnected() async {
    if (_savedMac == null) throw Exception('No printer paired');

    // Happy path: device object cached and still connected
    if (_device != null &&
        _device!.remoteId.str == _savedMac &&
        _device!.isConnected &&
        _characteristic != null) {
      return;
    }

    // Clear stale characteristic cache
    _characteristic = null;

    // Build (or reuse) BluetoothDevice from saved MAC
    final device = (_device != null && _device!.remoteId.str == _savedMac)
        ? _device!
        : BluetoothDevice.fromId(_savedMac!);
    _device = device;

    if (!device.isConnected) {
      try {
        await device.connect(timeout: const Duration(seconds: 15));
      } catch (_) {
        // One retry
        await Future.delayed(const Duration(milliseconds: 500));
        await device.connect(timeout: const Duration(seconds: 15));
      }
    }

    // Discover services and find the write characteristic
    final services = await device.discoverServices();

    // Fast path: use saved UUIDs
    if (_savedSvcUUID != null && _savedChrUUID != null) {
      for (final svc in services) {
        if (svc.serviceUuid.str128.toLowerCase() == _savedSvcUUID) {
          for (final chr in svc.characteristics) {
            if (chr.characteristicUuid.str128.toLowerCase() == _savedChrUUID) {
              _characteristic = chr;
              return;
            }
          }
        }
      }
    }

    // Fallback: search all known UUIDs
    for (final svc in services) {
      final svcId = svc.serviceUuid.str128.toLowerCase();
      if (_kServiceUUIDs.contains(svcId)) {
        for (final chr in svc.characteristics) {
          final chrId = chr.characteristicUuid.str128.toLowerCase();
          if (_kWriteCharUUIDs.contains(chrId)) {
            _characteristic = chr;
            return;
          }
        }
      }
    }

    throw Exception('Compatible write characteristic not found on this printer');
  }

  Future<void> _writeChunked(BluetoothCharacteristic chr, Uint8List data) async {
    // Try 128-byte chunks first (Android negotiates MTU automatically).
    // Fall back to 20 bytes if the printer rejects the larger payload.
    int chunkSize = 128;
    const delayMs = 3;
    int offset = 0;

    while (offset < data.length) {
      final end = (offset + chunkSize > data.length) ? data.length : offset + chunkSize;
      final chunk = data.sublist(offset, end);

      try {
        // withoutResponse = true → no per-chunk ACK wait → much faster
        await chr.write(chunk, withoutResponse: true);
        offset = end;
        if (offset < data.length) {
          await Future.delayed(const Duration(milliseconds: delayMs));
        }
      } catch (e) {
        if (chunkSize > 20) {
          // Printer rejected chunk size — fall back to 20 bytes and retry this chunk
          chunkSize = 20;
          await Future.delayed(const Duration(milliseconds: 50));
          continue; // retry same offset
        }
        rethrow;
      }
    }
  }
}
