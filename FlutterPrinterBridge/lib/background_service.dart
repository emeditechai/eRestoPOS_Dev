import 'dart:async';
import 'package:flutter/widgets.dart';
import 'package:flutter_background_service/flutter_background_service.dart';
import 'package:flutter_background_service_android/flutter_background_service_android.dart';
import 'printer_service.dart';
import 'http_server.dart';

/// Configures and starts the Android foreground service.
/// Must be called from main() before runApp().
Future<void> initializeService() async {
  final service = FlutterBackgroundService();

  await service.configure(
    androidConfiguration: AndroidConfiguration(
      onStart: _onStart,
      autoStart: false,
      isForegroundMode: true,
      notificationChannelId: 'print_bridge_channel',
      initialNotificationTitle: 'Print Bridge',
      initialNotificationContent: 'Starting…',
      foregroundServiceNotificationId: 8001,
    ),
    iosConfiguration: IosConfiguration(autoStart: false),
  );
  // Do NOT start here — HomeScreen starts it after UI is ready
}

/// Entry point for the background/foreground service isolate.
/// This runs in a SEPARATE Dart isolate from the UI.
@pragma('vm:entry-point')
void _onStart(ServiceInstance service) async {

  final printerService = PrinterService();
  await printerService.loadSaved();

  // Update notification once printer info is loaded
  _updateNotification(service, printerService);

  // Start the HTTP server on localhost:9100
  final httpServer = PrinterHttpServer(printerService);
  await httpServer.start();

  // Handle commands from the UI isolate
  service.on('reload_printer').listen((_) async {
    await printerService.loadSaved();
    _updateNotification(service, printerService);
  });

  service.on('forget_printer').listen((_) async {
    await printerService.forget();
    _updateNotification(service, printerService);
  });

  service.on('stop').listen((_) {
    service.stopSelf();
  });

  // Refresh notification every 30 s (e.g., after BLE connects/disconnects)
  Timer.periodic(const Duration(seconds: 30), (_) {
    _updateNotification(service, printerService);
  });
}

void _updateNotification(ServiceInstance service, PrinterService printerService) {
  if (service is AndroidServiceInstance) {
    final name = printerService.savedName;
    service.setForegroundNotificationInfo(
      title: 'Print Bridge',
      content: name != null ? 'Paired: $name — Ready to print' : 'No printer paired — open app to pair',
    );
  }
}
