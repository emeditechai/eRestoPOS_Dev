/**
 * escpos-print.js
 * 
 * Intercepts the #printPOSLink button and Shift+P hotkey when ESC/POS printing is enabled.
 * Supports two modes:
 *   BLE  – Web Bluetooth API (Android Chrome / Chromium only)
 *   TCP  – POST to server which sends raw bytes to the printer over WiFi TCP socket
 * 
 * window.EscPosPrint must be set before this script loads:
 *   window.EscPosPrint = { enabled: true/false, mode: "BLE"/"TCP", orderId: 123 }
 */
(function () {
    'use strict';

    var cfg = window.EscPosPrint || {};
    if (!cfg.enabled) return; // feature off — do nothing, let native links work

    var MODE    = (cfg.mode || 'BLE').toUpperCase();
    var ORDER_ID = cfg.orderId;

    // ── Toast helper ──────────────────────────────────────────────────────────
    function showToast(message, isError) {
        var existing = document.getElementById('escpos-toast');
        if (existing) existing.remove();

        var toast = document.createElement('div');
        toast.id = 'escpos-toast';
        toast.style.cssText = [
            'position:fixed', 'bottom:24px', 'right:24px', 'z-index:99999',
            'padding:14px 22px', 'border-radius:10px',
            'font-size:15px', 'font-weight:600', 'max-width:320px',
            'box-shadow:0 8px 24px rgba(0,0,0,0.18)',
            'transition:opacity 0.4s',
            isError
                ? 'background:#dc3545;color:#fff'
                : 'background:#198754;color:#fff'
        ].join(';');
        toast.textContent = message;
        document.body.appendChild(toast);

        setTimeout(function () {
            toast.style.opacity = '0';
            setTimeout(function () { if (toast.parentNode) toast.remove(); }, 500);
        }, 4000);
    }

    // ── Loading spinner overlay ───────────────────────────────────────────────
    function showSpinner() {
        var d = document.createElement('div');
        d.id = 'escpos-spinner';
        d.style.cssText = [
            'position:fixed','inset:0','z-index:99998',
            'background:rgba(0,0,0,0.35)',
            'display:flex','align-items:center','justify-content:center'
        ].join(';');
        d.innerHTML = '<div style="background:#fff;border-radius:12px;padding:28px 36px;text-align:center;">'
            + '<div style="width:40px;height:40px;border:4px solid #7c3aed;border-top-color:transparent;'
            + 'border-radius:50%;animation:escpos-spin 0.8s linear infinite;margin:0 auto 12px"></div>'
            + '<div style="font-size:15px;font-weight:600;color:#2a106d">Sending to printer…</div>'
            + '</div>';

        var style = document.createElement('style');
        style.textContent = '@keyframes escpos-spin{to{transform:rotate(360deg)}}';
        d.appendChild(style);
        document.body.appendChild(d);
    }

    function hideSpinner() {
        var d = document.getElementById('escpos-spinner');
        if (d) d.remove();
    }

    // ── CSRF token helper ─────────────────────────────────────────────────────
    function getCsrfToken() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    // ── TCP mode: POST to server ──────────────────────────────────────────────
    function printViaTCP() {
        var token = getCsrfToken();
        if (!token) {
            showToast('Security token missing. Please refresh the page.', true);
            return;
        }

        showSpinner();

        var form = new FormData();
        form.append('orderId', ORDER_ID);
        form.append('__RequestVerificationToken', token);

        fetch('/Payment/PrintEscPosTCP', {
            method: 'POST',
            body: form,
            credentials: 'same-origin'
        })
        .then(function (r) { return r.json(); })
        .then(function (data) {
            hideSpinner();
            if (data.success) {
                showToast('Receipt sent to printer!', false);
            } else {
                showToast('Print failed: ' + (data.error || 'Unknown error'), true);
            }
        })
        .catch(function (err) {
            hideSpinner();
            showToast('Network error: ' + err.message, true);
        });
    }

    // ── BLE mode: Web Bluetooth API ───────────────────────────────────────────
    // Known BLE printer service/characteristic UUIDs
    var BLE_SERVICES = [
        '000018f0-0000-1000-8000-00805f9b34fb', // common thermal printer service
        '6e400001-b5a3-f393-e0a9-e50e24dcca9e', // Nordic UART (NUS)
        '49535343-fe7d-4ae5-8fa9-9fafd205e455', // Microchip BLE UART
        '0000ff00-0000-1000-8000-00805f9b34fb', // custom ff00 service
    ];
    var BLE_WRITE_CHARS = [
        '00002af1-0000-1000-8000-00805f9b34fb', // common write char
        '6e400002-b5a3-f393-e0a9-e50e24dcca9e', // NUS RX (write to printer)
        '49535343-8841-43f4-a8d4-ecbe34729bb3', // Microchip RX
        '0000ff02-0000-1000-8000-00805f9b34fb', // custom ff02 write
    ];

    // Write Uint8Array in chunks to avoid MTU limit
    function writeChunked(characteristic, data) {
        var CHUNK = 512;
        var offset = 0;

        function writeNext() {
            if (offset >= data.length) return Promise.resolve();
            var chunk = data.slice(offset, offset + CHUNK);
            offset += CHUNK;
            return characteristic.writeValueWithoutResponse(chunk).then(writeNext);
        }
        return writeNext();
    }

    // Fetch ESC/POS bytes from server then write via BLE
    function printViaBLE() {
        showSpinner();

        // Step 1: request BLE device
        navigator.bluetooth.requestDevice({
            acceptAllDevices: true,
            optionalServices: BLE_SERVICES
        })
        .then(function (device) {
            return device.gatt.connect();
        })
        .then(function (server) {
            // Step 2: fetch receipt bytes from server
            return fetch('/Payment/GetEscPosBytes?orderId=' + ORDER_ID, {
                credentials: 'same-origin'
            })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (!data.success) throw new Error(data.error || 'Failed to get receipt data');

                // Decode base64 → Uint8Array
                var binaryStr = atob(data.data);
                var bytes = new Uint8Array(binaryStr.length);
                for (var i = 0; i < binaryStr.length; i++) {
                    bytes[i] = binaryStr.charCodeAt(i);
                }
                return { server: server, bytes: bytes };
            });
        })
        .then(function (ctx) {
            // Step 3: find writable characteristic (try each service UUID)
            function tryService(serviceIdx) {
                if (serviceIdx >= BLE_SERVICES.length) {
                    throw new Error('No compatible printer service found. Make sure the printer is in BLE mode.');
                }
                return ctx.server.getPrimaryService(BLE_SERVICES[serviceIdx])
                .then(function (service) {
                    return tryChar(service, 0, ctx.bytes);
                })
                .catch(function () {
                    return tryService(serviceIdx + 1);
                });
            }

            function tryChar(service, charIdx, bytes) {
                if (charIdx >= BLE_WRITE_CHARS.length) {
                    throw new Error('No writable characteristic found in printer service.');
                }
                return service.getCharacteristic(BLE_WRITE_CHARS[charIdx])
                .then(function (char) {
                    return writeChunked(char, bytes);
                })
                .catch(function () {
                    return tryChar(service, charIdx + 1, bytes);
                });
            }

            return tryService(0);
        })
        .then(function () {
            hideSpinner();
            showToast('Receipt printed successfully!', false);
        })
        .catch(function (err) {
            hideSpinner();
            if (err && err.name === 'NotFoundError') {
                // User cancelled device picker — silent
                return;
            }
            showToast('BLE print error: ' + (err.message || err), true);
        });
    }

    // ── Main trigger ──────────────────────────────────────────────────────────
    function triggerPrint(e) {
        if (MODE === 'BLE' && !navigator.bluetooth) {
            // BLE not available (HTTP or unsupported browser) — open HTML receipt as fallback
            // Don't prevent default: let the link open PrintPOS normally (htmlOnly=true skips BLE loop)
            var url = '/Payment/PrintPOS?orderId=' + ORDER_ID + '&htmlOnly=true';
            window.open(url, '_blank');
            if (e && e.preventDefault) e.preventDefault();
            return;
        }
        if (e && e.preventDefault) e.preventDefault();
        if (MODE === 'TCP') {
            printViaTCP();
        } else {
            printViaBLE();
        }
    }

    // ── Wire up buttons and hotkey ────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', function () {
        // Intercept all #printPOSLink elements (there may be 2 on the page)
        var links = document.querySelectorAll('#printPOSLink');
        links.forEach(function (link) {
            link.addEventListener('click', triggerPrint);
        });

        // Intercept Shift+P hotkey
        document.addEventListener('keydown', function (e) {
            if (e.shiftKey && (e.key === 'P' || e.key === 'p') && !e.ctrlKey && !e.altKey) {
                // Only if a printPOSLink is present on the page
                if (document.getElementById('printPOSLink')) {
                    triggerPrint(e);
                }
            }
        });
    });

})();
