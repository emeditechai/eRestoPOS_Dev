/**
 * printer-manager.js
 * Global BLE thermal printer manager for the Restaurant app.
 *
 * Usage:
 *   await PrinterManager.init();          // load saved config (localStorage → server)
 *   await PrinterManager.print(b64, cb);  // connect + print base64 ESC/POS bytes
 *   await PrinterManager.scan(cb);        // show picker, detect UUIDs, return device info
 *   await PrinterManager.savePrinter(device, svcUUID, chrUUID); // persist printer
 *   await PrinterManager.forget();        // remove saved printer everywhere
 *   PrinterManager.getConfig();           // { id, name, svcUUID, chrUUID } or null
 *   PrinterManager.isReady();             // true if a printer is saved
 *
 * Storage keys (localStorage):
 *   escpos_printer_id    — Chrome BLE device.id (opaque, browser-scoped)
 *   escpos_printer_name  — human-readable printer name
 *   escpos_svc_uuid      — BLE service UUID (fast-connect cache)
 *   escpos_chr_uuid      — BLE characteristic UUID (fast-connect cache)
 *
 * Server API (same-origin, requires auth + antiforgery for POST):
 *   GET  /Utility/GetBlePrinter   — returns { success, printer: {id,name,svcUUID,chrUUID} }
 *   POST /Utility/SaveBlePrinter  — body: { id, name, svcUUID, chrUUID }
 *   POST /Utility/DeleteBlePrinter
 */
window.PrinterManager = (function () {
    'use strict';

    // ── Known BLE service / characteristic UUIDs for common thermal printers ──
    var BLE_SERVICES = [
        '000018f0-0000-1000-8000-00805f9b34fb',
        '6e400001-b5a3-f393-e0a9-e50e24dcca9e',
        '49535343-fe7d-4ae5-8fa9-9fafd205e455',
        '0000ff00-0000-1000-8000-00805f9b34fb'
    ];
    var BLE_WRITE_CHARS = [
        '00002af1-0000-1000-8000-00805f9b34fb',
        '6e400002-b5a3-f393-e0a9-e50e24dcca9e',
        '49535343-8841-43f4-a8d4-ecbe34729bb3',
        '0000ff02-0000-1000-8000-00805f9b34fb'
    ];

    var LS_ID   = 'escpos_printer_id';
    var LS_NAME = 'escpos_printer_name';
    var LS_SVC  = 'escpos_svc_uuid';
    var LS_CHR  = 'escpos_chr_uuid';

    // ── Internal state ─────────────────────────────────────────────────────────
    var _config         = null;   // { id, name, svcUUID, chrUUID }
    var _device         = null;   // BluetoothDevice (may be null after page reload)
    var _server         = null;   // BluetoothRemoteGATTServer — cached to skip reconnect
    var _characteristic = null;   // BluetoothRemoteGATTCharacteristic — cached to skip discovery

    // ── Antiforgery helper ─────────────────────────────────────────────────────
    function getAntiforgeryToken() {
        var meta = document.querySelector('meta[name="request-verification-token"]');
        return meta ? meta.content : '';
    }

    // ── localStorage helpers ────────────────────────────────────────────────────
    function loadFromStorage() {
        try {
            var name = localStorage.getItem(LS_NAME);
            if (!name) return null;
            return {
                id:      localStorage.getItem(LS_ID)   || '',
                name:    name,
                svcUUID: localStorage.getItem(LS_SVC)  || '',
                chrUUID: localStorage.getItem(LS_CHR)  || ''
            };
        } catch (e) { return null; }
    }

    function saveToStorage(cfg) {
        try {
            if (cfg.id)      localStorage.setItem(LS_ID,   cfg.id);
            if (cfg.name)    localStorage.setItem(LS_NAME, cfg.name);
            if (cfg.svcUUID) localStorage.setItem(LS_SVC,  cfg.svcUUID);
            if (cfg.chrUUID) localStorage.setItem(LS_CHR,  cfg.chrUUID);
        } catch (e) {}
    }

    function clearStorage() {
        try {
            [LS_ID, LS_NAME, LS_SVC, LS_CHR].forEach(function (k) { localStorage.removeItem(k); });
        } catch (e) {}
    }

    // ── Server helpers ──────────────────────────────────────────────────────────
    function loadFromServer() {
        return fetch('/Utility/GetBlePrinter', { credentials: 'same-origin' })
            .then(function (res) { return res.ok ? res.json() : null; })
            .then(function (data) {
                if (data && data.success && data.printer) return data.printer;
                return null;
            })
            .catch(function () { return null; });
    }

    function saveToServer(cfg) {
        // Fire-and-forget — localStorage is authoritative for speed
        fetch('/Utility/SaveBlePrinter', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiforgeryToken()
            },
            body: JSON.stringify({ id: cfg.id, name: cfg.name, svcUUID: cfg.svcUUID, chrUUID: cfg.chrUUID })
        }).catch(function () {});
    }

    function deleteFromServer() {
        return fetch('/Utility/DeleteBlePrinter', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'RequestVerificationToken': getAntiforgeryToken() }
        }).catch(function () {});
    }

    // ── BLE helpers ─────────────────────────────────────────────────────────────
    function delay(ms) {
        return new Promise(function (resolve) { setTimeout(resolve, ms); });
    }

    // Write ESC/POS data in chunks over BLE.
    //
    // Strategy:
    //   • Start at 128-byte chunks. Android Chrome negotiates MTU automatically
    //     during gatt.connect() (usually 128–512 bytes). 128 bytes = 6.4× fewer
    //     round trips than 20 bytes for a typical 3 KB receipt.
    //   • On first NetworkError / InvalidStateError (printer rejected the size),
    //     fall back to 20-byte chunks and retry from the same offset.
    //   • Use writeValueWithoutResponse when available — no per-chunk ACK wait,
    //     much faster on mobile. Use 3ms delay (just enough to yield the BLE
    //     stack; the old 10ms was a conservative guess).
    //   • writeValue (older characteristic) needs more breathing room — 15ms.
    function writeChunked(characteristic, data) {
        var useWoR  = typeof characteristic.writeValueWithoutResponse === 'function';
        var CHUNK   = 128;   // start large; falls back to 20 on rejection
        var DELAY   = useWoR ? 3 : 15;
        var offset  = 0;

        function next() {
            if (offset >= data.length) return Promise.resolve();
            var chunk = data.slice(offset, offset + CHUNK);
            var writeP = useWoR
                ? characteristic.writeValueWithoutResponse(chunk)
                : characteristic.writeValue(chunk);
            return writeP
                .then(function () {
                    offset += CHUNK;
                    return offset < data.length ? delay(DELAY).then(next) : Promise.resolve();
                })
                .catch(function (e) {
                    // Chunk too large for this printer — fall back to 20 bytes once.
                    if (CHUNK > 20 && (
                        e.name === 'NetworkError' ||
                        e.name === 'InvalidStateError' ||
                        (e.message && (
                            e.message.indexOf('not allowed') >= 0 ||
                            e.message.indexOf('too long')    >= 0 ||
                            e.message.indexOf('invalid')     >= 0
                        ))
                    )) {
                        CHUNK = 20;
                        return delay(50).then(next);  // brief pause then retry same offset
                    }
                    return Promise.reject(e);
                });
        }
        return next();
    }

    // Discover the first writable service+characteristic combination
    function findWriteCharacteristic(server, knownSvcUUID, knownChrUUID) {
        // Fast path — use known UUIDs from previous connection
        if (knownSvcUUID && knownChrUUID) {
            return server.getPrimaryService(knownSvcUUID)
                .then(function (svc) { return svc.getCharacteristic(knownChrUUID); })
                .then(function (ch) { return { characteristic: ch, svcUUID: knownSvcUUID, chrUUID: knownChrUUID }; })
                .catch(function () { return discoverCharacteristic(server); }); // fallback to discovery
        }
        return discoverCharacteristic(server);
    }

    function discoverCharacteristic(server) {
        var si = 0;
        function tryService() {
            if (si >= BLE_SERVICES.length) return Promise.reject(new Error('Printer service not found. Ensure the printer is on and Bluetooth is enabled.'));
            var svcUUID = BLE_SERVICES[si++];
            return server.getPrimaryService(svcUUID)
                .then(function (svc) { return tryChar(svc, svcUUID, 0); })
                .catch(function (e) {
                    if (e && e.name === 'SecurityError') return Promise.reject(e);
                    return tryService();
                });
        }
        function tryChar(svc, svcUUID, ci) {
            if (ci >= BLE_WRITE_CHARS.length) return Promise.reject(new Error('no_char'));
            var chrUUID = BLE_WRITE_CHARS[ci];
            return svc.getCharacteristic(chrUUID)
                .then(function (ch) { return { characteristic: ch, svcUUID: svcUUID, chrUUID: chrUUID }; })
                .catch(function (e) {
                    if (e && (e.name === 'NetworkError' || e.name === 'InvalidStateError')) return Promise.reject(e);
                    return tryChar(svc, svcUUID, ci + 1);
                });
        }
        return tryService();
    }

    function connectGatt(device) {
        // Reuse cached server if the GATT connection is still alive.
        // This makes "Print again" in the same session instant on mobile —
        // skips the ~400-800ms GATT connect + service discovery round-trip.
        if (_server && _server.connected) {
            return Promise.resolve(_server);
        }
        // Clear stale cache
        _server         = null;
        _characteristic = null;
        return device.gatt.connect()
            .then(function (s) { _server = s; return s; })
            .catch(function () {
                return delay(900).then(function () {
                    return device.gatt.connect()
                        .then(function (s) { _server = s; return s; });
                });
            });
    }

    // ── Public: init ────────────────────────────────────────────────────────────
    // Load saved printer config: localStorage first (instant), then server (fallback)
    function init() {
        var stored = loadFromStorage();
        if (stored) {
            _config = stored;
            return Promise.resolve(_config);
        }
        return loadFromServer().then(function (serverCfg) {
            if (serverCfg) {
                _config = serverCfg;
                saveToStorage(_config);
            }
            return _config;
        });
    }

    // ── Public: connect ─────────────────────────────────────────────────────────
    // Connects to the saved printer silently; shows picker only when necessary.
    // Returns Promise<{ device, svcUUID, chrUUID }>
    //
    // Priority order:
    //   1. In-memory _device ref (same page session — zero user interaction)
    //   2. getDevices() silent match — no picker:
    //        a. Match by stored ID
    //        b. Match by name (device.name can be null when not advertising)
    //        c. If only ONE granted device exists → must be ours, use it directly
    //   3. requestDevice() exact name filter — shows only the saved printer
    //   4. requestDevice() acceptAllDevices — fallback / first-ever pair
    function connect(statusCallback) {
        function status(msg) { if (typeof statusCallback === 'function') statusCallback(msg); }

        if (!navigator.bluetooth) {
            return Promise.reject(new Error('Web Bluetooth not supported. Please use Chrome on Android.'));
        }

        // ── 1. Reuse in-memory device (same page session, zero user interaction) ──
        if (_device) {
            return Promise.resolve({
                device:  _device,
                svcUUID: _config ? _config.svcUUID : '',
                chrUUID: _config ? _config.chrUUID : ''
            });
        }

        // ── 2. Cross-session silent reconnect via getDevices() ────────────────────
        // Chrome returns all devices this origin has been granted access to.
        // No UI, no picker. Requires HTTPS or localhost, Chrome 85+.
        if (_config && typeof navigator.bluetooth.getDevices === 'function') {
            status('Connecting to ' + (_config.name || 'printer') + '\u2026');
            return navigator.bluetooth.getDevices().then(function (devices) {
                if (!devices || devices.length === 0) return showPicker(status);

                var matched = null;

                // 2a. Exact ID match
                if (_config.id) {
                    for (var i = 0; i < devices.length; i++) {
                        if (devices[i].id === _config.id) { matched = devices[i]; break; }
                    }
                }

                // 2b. Name match — Chrome can assign a different opaque ID to the same
                //     physical device across browser restarts.
                //     IMPORTANT: device.name is null when device is not advertising
                //     (idle/sleeping). Guard with truthy check.
                if (!matched && _config.name) {
                    for (var j = 0; j < devices.length; j++) {
                        if (devices[j].name && devices[j].name === _config.name) {
                            matched    = devices[j];
                            _config.id = matched.id;
                            saveToStorage(_config);
                            break;
                        }
                    }
                }

                // 2c. Single-device shortcut — Chrome only returns devices this origin
                //     was explicitly granted. If only ONE exists, it must be ours.
                //     This handles the common case where device.name is null.
                if (!matched && devices.length === 1) {
                    matched    = devices[0];
                    _config.id = matched.id;
                    if (matched.name) _config.name = matched.name;
                    saveToStorage(_config);
                }

                if (matched) {
                    _device = matched;
                    return { device: matched, svcUUID: _config.svcUUID, chrUUID: _config.chrUUID };
                }

                return showPicker(status);
            }).catch(function () { return showPicker(status); });
        }

        return showPicker(status);
    }

    // ── showPicker ───────────────────────────────────────────────────────────────
    // Exact name filter → picker shows ONLY the saved printer (1 tap, no list).
    // After the user taps, Chrome grants the device and future loads use getDevices().
    function showPicker(status) {
        if (typeof status !== 'function') status = function () {};

        if (_config && _config.name) {
            status('Tap \u201c' + _config.name + '\u201d to connect\u2026');
            return navigator.bluetooth.requestDevice({
                filters: [{ name: _config.name }],
                optionalServices: BLE_SERVICES
            })
            .then(function (d) {
                _device    = d;
                _config.id = d.id;      // sync ID → next session uses getDevices() silently
                saveToStorage(_config);
                return { device: d, svcUUID: _config.svcUUID, chrUUID: _config.chrUUID };
            })
            .catch(function (e) {
                // Printer out of range / BT off → open full list as last resort
                if (e && (e.name === 'NotFoundError' || e.name === 'TypeError')) {
                    return showAllDevicesPicker(status);
                }
                throw e;
            });
        }
        return showAllDevicesPicker(status);
    }

    function showAllDevicesPicker(status) {
        if (typeof status !== 'function') status = function () {};
        status('Select your printer\u2026');
        return navigator.bluetooth.requestDevice({ acceptAllDevices: true, optionalServices: BLE_SERVICES })
            .then(function (d) {
                _device = d;
                if (_config) {
                    _config.id   = d.id;
                    _config.name = d.name || _config.name;
                } else {
                    _config = { id: d.id, name: d.name || 'Thermal Printer', svcUUID: '', chrUUID: '' };
                }
                saveToStorage(_config);
                return { device: d, svcUUID: _config.svcUUID, chrUUID: _config.chrUUID };
            });
    }

    // ── Bridge: try the Flutter Print Bridge app first ───────────────────────────
    // The app runs an HTTP server on localhost:9100 that talks to the paired
    // Bluetooth printer natively — no Web BT picker, no permission dialog.
    // Chrome on Android allows HTTPS → http://localhost (W3C Secure Contexts).
    // Returns Promise<true> on success, Promise<null> if app is not running.
    var BRIDGE_URL = 'http://127.0.0.1:9100';

    function tryAppBridge(base64Bytes, status) {
        return fetch(BRIDGE_URL + '/status', {
            signal: (typeof AbortSignal !== 'undefined' && AbortSignal.timeout)
                        ? AbortSignal.timeout(800)   // 800ms max to detect if app is running
                        : undefined
        })
        .then(function (res) { return res.json(); })
        .then(function (data) {
            if (!data.ready) {
                // App running but no printer paired — tell user to open app
                status('Print Bridge app running but no printer paired. Open the app and tap Pair Printer.', 'warn');
                return null;
            }
            status('Sending via Print Bridge app\u2026');
            return fetch(BRIDGE_URL + '/print', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ data: base64Bytes })
            })
            .then(function (res) { return res.json(); })
            .then(function (result) {
                if (!result.success) throw new Error(result.error || 'Print failed');
                status('\u2713 Printed via Print Bridge app!', 'ok');
                return true;
            });
        })
        .catch(function (e) {
            // App not running or fetch aborted — fall through to Web Bluetooth
            if (e && e.name !== 'AbortError' && e.message && e.message.indexOf('Print Bridge') >= 0) {
                return Promise.reject(e);  // re-throw user-visible errors
            }
            return null;  // silent fallback to Web BT
        });
    }

    // ── Public: print ───────────────────────────────────────────────────────────
    // 1. Tries the Flutter Print Bridge app (localhost:9100) — silent, no picker.
    // 2. Falls back to Web Bluetooth if the app is not running.
    // @param base64Bytes  string — standard base64-encoded ESC/POS bytes
    // @param statusCallback function(msg, cls) — optional UI feedback
    // Returns Promise<{ id, name, svcUUID, chrUUID } | true>
    function print(base64Bytes, statusCallback) {
        function status(msg, cls) {
            if (typeof statusCallback === 'function') statusCallback(msg, cls);
        }

        // ── Step 1: try Print Bridge app ──────────────────────────────────────
        return tryAppBridge(base64Bytes, status)
            .then(function (bridgeResult) {
                if (bridgeResult !== null) return bridgeResult;  // success or unrecoverable error already shown
                // ── Step 2: fall back to Web Bluetooth ───────────────────────
                status('Print Bridge app not detected — using Web Bluetooth\u2026');
                return _printViaBluetooth(base64Bytes, status);
            });
    }

    function _printViaBluetooth(base64Bytes, status) {
        var bytes;
        try {
            var str = atob(base64Bytes);
            bytes = new Uint8Array(str.length);
            for (var i = 0; i < str.length; i++) bytes[i] = str.charCodeAt(i);
        } catch (e) {
            return Promise.reject(new Error('Invalid print data (base64 decode failed).'));
        }

        return connect(function (msg) { status(msg); })
            .then(function (result) {
                var device  = result.device;
                var svcUUID = result.svcUUID;
                var chrUUID = result.chrUUID;
                status('Sending to ' + (device.name || 'printer') + '\u2026');
                return connectGatt(device)
                    .then(function (server) {
                        // Use cached characteristic when GATT is already connected —
                        // skips service + characteristic discovery (saves ~300ms on mobile).
                        if (_characteristic) {
                            return Promise.resolve({ characteristic: _characteristic, svcUUID: svcUUID, chrUUID: chrUUID });
                        }
                        return findWriteCharacteristic(server, svcUUID, chrUUID);
                    })
                    .then(function (found) {
                        _characteristic = found.characteristic;   // cache for next print
                        return writeChunked(found.characteristic, bytes)
                            .then(function () { return found; });
                    })
                    .then(function (found) {
                        var newCfg = {
                            id:      device.id,
                            name:    device.name || (_config && _config.name) || 'Thermal Printer',
                            svcUUID: found.svcUUID,
                            chrUUID: found.chrUUID
                        };
                        _config = newCfg;
                        _device = device;
                        saveToStorage(newCfg);
                        saveToServer(newCfg);   // fire-and-forget DB persist
                        status('\u2713 Printed to ' + newCfg.name + '!', 'ok');
                        return newCfg;
                    })
                    .catch(function (e) {
                        // If the cached GATT server/characteristic went stale, clear and retry once.
                        if (_server || _characteristic) {
                            _server         = null;
                            _characteristic = null;
                            return connectGatt(device)
                                .then(function (server) { return findWriteCharacteristic(server, svcUUID, chrUUID); })
                                .then(function (found) {
                                    _characteristic = found.characteristic;
                                    return writeChunked(found.characteristic, bytes).then(function () { return found; });
                                })
                                .then(function (found) {
                                    var newCfg = {
                                        id:      device.id,
                                        name:    device.name || (_config && _config.name) || 'Thermal Printer',
                                        svcUUID: found.svcUUID,
                                        chrUUID: found.chrUUID
                                    };
                                    _config = newCfg;
                                    _device = device;
                                    saveToStorage(newCfg);
                                    saveToServer(newCfg);
                                    status('\u2713 Printed to ' + newCfg.name + '!', 'ok');
                                    return newCfg;
                                });
                        }
                        return Promise.reject(e);
                    });
            });
    }

    // ── Public: scan ────────────────────────────────────────────────────────────
    // Shows BLE device picker, connects briefly to discover service/char UUIDs,
    // then disconnects.  Does NOT save — call savePrinter() afterwards.
    // @param statusCallback function(msg) — optional UI feedback
    // Returns Promise<{ device, svcUUID, chrUUID }>
    function scan(statusCallback) {
        function status(msg) { if (typeof statusCallback === 'function') statusCallback(msg); }

        if (!navigator.bluetooth) {
            return Promise.reject(new Error('Web Bluetooth not supported. Please use Chrome on Android.'));
        }
        status('Scanning for printers\u2026');
        return navigator.bluetooth.requestDevice({ acceptAllDevices: true, optionalServices: BLE_SERVICES })
            .then(function (device) {
                status('Detecting printer profile on ' + (device.name || 'device') + '\u2026');
                return connectGatt(device)
                    .then(function (server) {
                        return discoverCharacteristic(server)
                            .then(function (found) {
                                try { server.disconnect(); } catch (e) {}
                                return { device: device, svcUUID: found.svcUUID, chrUUID: found.chrUUID };
                            })
                            .catch(function () {
                                try { server.disconnect(); } catch (e) {}
                                // Could not determine UUIDs — return device with empty UUIDs
                                return { device: device, svcUUID: '', chrUUID: '' };
                            });
                    });
            });
    }

    // ── Public: savePrinter ─────────────────────────────────────────────────────
    // Persists the printer to localStorage and server DB.
    function savePrinter(device, svcUUID, chrUUID) {
        var cfg = {
            id:      device.id   || '',
            name:    device.name || 'Thermal Printer',
            svcUUID: svcUUID     || '',
            chrUUID: chrUUID     || ''
        };
        _config = cfg;
        _device = device;
        saveToStorage(cfg);
        return new Promise(function (resolve) {
            fetch('/Utility/SaveBlePrinter', {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getAntiforgeryToken()
                },
                body: JSON.stringify({ id: cfg.id, name: cfg.name, svcUUID: cfg.svcUUID, chrUUID: cfg.chrUUID })
            })
            .then(function (res) { return res.json(); })
            .then(function (data) { resolve(data); })
            .catch(function () { resolve({ success: false }); });
        });
    }

    // ── Public: forget ──────────────────────────────────────────────────────────
    // Removes the saved printer from localStorage and server DB.
    function forget() {
        _config         = null;
        _device         = null;
        _server         = null;
        _characteristic = null;
        clearStorage();
        return deleteFromServer();
    }

    // ── Public: getters ─────────────────────────────────────────────────────────
    function getConfig() { return _config; }
    function isReady()   { return !!(_config && _config.name); }

    // ── Expose public API ───────────────────────────────────────────────────────
    return {
        init:         init,
        connect:      connect,
        print:        print,
        scan:         scan,
        savePrinter:  savePrinter,
        forget:       forget,
        getConfig:    getConfig,
        isReady:      isReady
    };

})();
