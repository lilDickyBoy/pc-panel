package com.pcpanel.app.service

import android.content.BroadcastReceiver
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.BatteryManager
import androidx.core.content.ContextCompat
import com.pcpanel.app.net.BannerKind
import com.pcpanel.app.net.Connection
import com.pcpanel.app.ui.Str

/** Battery reporting and clipboard sync with the PC. */
object PhoneLink {
    private var batteryReceiver: BroadcastReceiver? = null
    private var lastLevel = -2
    private var lastCharging = false
    private var clipListener: ClipboardManager.OnPrimaryClipChangedListener? = null
    private var lastClipSent = ""
    private var lastClipReceived = ""
    @Volatile private var suppressUntil = 0L

    fun onConnected(ctx: Context) {
        lastLevel = -2
        registerBattery(ctx)
        if (Connection.foreground) onForeground(ctx)
    }

    fun onDisconnected(ctx: Context) {
        val r = batteryReceiver ?: return
        batteryReceiver = null
        try { ctx.applicationContext.unregisterReceiver(r) } catch (_: Exception) { }
    }

    // ------------------------------------------------------------------ battery
    private fun registerBattery(ctx: Context) {
        if (batteryReceiver != null) return
        val r = object : BroadcastReceiver() {
            override fun onReceive(c: Context?, i: Intent?) { if (i != null) report(i) }
        }
        batteryReceiver = r
        val sticky = ContextCompat.registerReceiver(ctx.applicationContext, r, IntentFilter(Intent.ACTION_BATTERY_CHANGED), ContextCompat.RECEIVER_NOT_EXPORTED)
        if (sticky != null) report(sticky)
    }

    private fun report(i: Intent) {
        val level = i.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
        val scale = i.getIntExtra(BatteryManager.EXTRA_SCALE, 100).coerceAtLeast(1)
        val pct = if (level < 0) -1 else level * 100 / scale
        val charging = i.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) != 0
        val temp = i.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, -10) / 10.0
        if (pct == lastLevel && charging == lastCharging) return
        lastLevel = pct; lastCharging = charging
        Connection.cmd("phone", "level" to pct, "charging" to charging, "temp" to temp)
    }

    // ------------------------------------------------------------------ clipboard
    /** Android only lets apps read the clipboard while they're in front. */
    fun onForeground(ctx: Context) {
        if (!Connection.connected) return
        val cm = ctx.getSystemService(Context.CLIPBOARD_SERVICE) as? ClipboardManager ?: return
        if (clipListener == null) {
            val l = ClipboardManager.OnPrimaryClipChangedListener { pushClipboard(ctx, explicit = false) }
            clipListener = l
            cm.addPrimaryClipChangedListener(l)
        }
        // whatever was copied while we were away
        android.os.Handler(android.os.Looper.getMainLooper()).postDelayed({ pushClipboard(ctx, explicit = false) }, 600)
    }

    fun readClipboard(ctx: Context): String? {
        return try {
            val cm = ctx.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            cm.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(ctx)?.toString()
        } catch (_: Exception) { null }
    }

    fun pushClipboard(ctx: Context, explicit: Boolean): Boolean {
        if (!Connection.connected) return false
        if (!explicit && (!Connection.pcSettings.clipSync || System.currentTimeMillis() < suppressUntil)) return false
        val text = readClipboard(ctx) ?: return false
        if (text.isEmpty() || text.length > 200_000) return false
        if (!explicit && (text == lastClipSent || text == lastClipReceived)) return false
        lastClipSent = text
        return Connection.cmd("clip", "text" to text, "explicit" to explicit)
    }

    fun receiveClipboard(ctx: Context, text: String) {
        if (text.isEmpty()) return
        Connection.ui {
            try {
                lastClipReceived = text
                suppressUntil = System.currentTimeMillis() + 800
                val cm = ctx.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                cm.setPrimaryClip(ClipData.newPlainText("PC Panel", text))
                if (Connection.foreground) Connection.showBanner(Str.t("clip.fromPc"), BannerKind.Success)
            } catch (_: Exception) { }
        }
    }
}
