/**
 * wake-lock.js
 *
 * Prevents the mobile screen from sleeping while the page is open.
 * Primary:  Screen Wake Lock API (Chrome Android 84+, HTTPS required).
 * Fallback: silent looping invisible <video> for older browsers / WebView.
 *
 * Auto-starts on DOMContentLoaded.
 * Re-acquires automatically when the user returns to the tab.
 * Call WakeLock.stop() to release explicitly (e.g. on page unload).
 */
window.WakeLock = (function () {
    'use strict';

    var _lock    = null;   // WakeLockSentinel (native API)
    var _active  = false;  // whether we are trying to stay awake
    var _videoEl = null;   // fallback <video> element

    // ── Screen Wake Lock API ──────────────────────────────────────────────────
    function acquireNative() {
        if (!navigator.wakeLock) return Promise.reject(new Error('unsupported'));
        return navigator.wakeLock.request('screen').then(function (sentinel) {
            _lock = sentinel;
            // The OS can revoke the lock (e.g. user pulls down notification shade).
            // Re-acquire automatically so the screen never sleeps mid-session.
            sentinel.addEventListener('release', function () {
                _lock = null;
                if (_active) setTimeout(acquireNative, 1000);
            });
        });
    }

    // ── Invisible-video fallback ──────────────────────────────────────────────
    // A muted, looping 1×1 video element tricks the browser into keeping the
    // screen on when the Wake Lock API is not available (older Android WebView,
    // Safari, Firefox).
    function startVideoFallback() {
        if (_videoEl) return;
        try {
            var v = document.createElement('video');
            v.loop        = true;
            v.muted       = true;
            v.playsInline = true;
            v.setAttribute('webkit-playsinline', '');
            v.style.cssText = 'position:fixed;width:1px;height:1px;top:0;left:0;' +
                              'opacity:0.001;pointer-events:none;z-index:-9999;';
            // Minimal valid MP4 (ftyp + empty mdat) — just enough for the browser
            // to accept it as a video source and loop silently forever.
            v.src = 'data:video/mp4;base64,' +
                'AAAAHGZ0eXBtcDQyAAAAAW1wNDJpc29tYXZjMQAAAAhtZGF0AAAA';
            document.body.appendChild(v);
            _videoEl = v;
            v.play().catch(function () {});
        } catch (e) {}
    }

    function stopVideoFallback() {
        if (!_videoEl) return;
        try { _videoEl.pause(); } catch (e) {}
        try {
            if (_videoEl.parentNode) _videoEl.parentNode.removeChild(_videoEl);
        } catch (e) {}
        _videoEl = null;
    }

    // ── Public: start ─────────────────────────────────────────────────────────
    function start() {
        _active = true;
        acquireNative().catch(startVideoFallback);
    }

    // ── Public: stop ──────────────────────────────────────────────────────────
    function stop() {
        _active = false;
        if (_lock) {
            try { _lock.release(); } catch (e) {}
            _lock = null;
        }
        stopVideoFallback();
    }

    // ── Re-acquire when user returns to this tab ──────────────────────────────
    // The native API drops the sentinel when the tab is hidden; reclaim it the
    // moment visibility is restored so the screen never sleeps between actions.
    document.addEventListener('visibilitychange', function () {
        if (!_active || document.visibilityState !== 'visible') return;
        if (!_lock) acquireNative().catch(startVideoFallback);
    });

    // ── Auto-start ────────────────────────────────────────────────────────────
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }

    return { start: start, stop: stop };
})();
