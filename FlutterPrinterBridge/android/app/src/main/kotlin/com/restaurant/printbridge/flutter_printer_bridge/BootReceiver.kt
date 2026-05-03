package com.restaurant.printbridge.flutter_printer_bridge

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/**
 * Starts the app when the device boots so the HTTP print bridge is ready
 * without manual intervention after a power cycle or reboot.
 */
class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_BOOT_COMPLETED ||
            intent.action == "android.intent.action.QUICKBOOT_POWERON"
        ) {
            val launch = Intent(context, MainActivity::class.java).apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            context.startActivity(launch)
        }
    }
}
