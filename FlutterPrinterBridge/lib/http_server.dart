import 'dart:convert';
import 'dart:typed_data';
import 'dart:io';
import 'package:shelf/shelf.dart';
import 'package:shelf/shelf_io.dart' as shelf_io;
import 'package:shelf_router/shelf_router.dart';
import 'printer_service.dart';

class PrinterHttpServer {
  final PrinterService _printer;

  PrinterHttpServer(this._printer);

  Future<void> start() async {
    final router = Router();

    // ── GET /status ───────────────────────────────────────────────────────────
    // Returns printer state. The browser polls this before each print.
    router.get('/status', (_) async {
      final status = _printer.getStatus();
      return _json(status.toJson());
    });

    // ── POST /print ───────────────────────────────────────────────────────────
    // Body: { "data": "<standard base64 ESC/POS bytes>" }
    // Returns: { "success": true } or { "success": false, "error": "…" }
    router.post('/print', (Request req) async {
      try {
        final body = await req.readAsString();
        final json = jsonDecode(body) as Map<String, dynamic>;
        final b64 = json['data'] as String?;

        if (b64 == null || b64.isEmpty) {
          return _json({'success': false, 'error': 'No data provided'}, 400);
        }

        final bytes = Uint8List.fromList(base64Decode(b64));
        await _printer.print(bytes);
        return _json({'success': true});
      } catch (e) {
        return _json({'success': false, 'error': e.toString()}, 500);
      }
    });

    // ── POST /forget ──────────────────────────────────────────────────────────
    router.post('/forget', (_) async {
      await _printer.forget();
      return _json({'success': true});
    });

    // ── OPTIONS * (CORS preflight) ────────────────────────────────────────────
    router.options('/<ignored|.*>', (_) => Response.ok('', headers: _corsHeaders));

    // Pipeline: add CORS headers to every response
    final handler = const Pipeline()
        .addMiddleware(_corsMiddleware)
        .addHandler(router.call);

    // Bind to all interfaces so the restaurant web server can reach this device on LAN
    await shelf_io.serve(handler, InternetAddress.anyIPv4, 9100);
  }

  // ── Helpers ─────────────────────────────────────────────────────────────────

  static Response _json(Map<String, dynamic> body, [int status = 200]) {
    return Response(
      status,
      body: jsonEncode(body),
      headers: {'content-type': 'application/json', ..._corsHeaders},
    );
  }

  static const Map<String, String> _corsHeaders = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type',
  };

  static Handler _corsMiddleware(Handler inner) {
    return (Request req) async {
      if (req.method == 'OPTIONS') {
        return Response.ok('', headers: _corsHeaders);
      }
      final response = await inner(req);
      return response.change(headers: _corsHeaders);
    };
  }
}
