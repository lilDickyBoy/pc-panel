package com.pcpanel.app.service

import android.app.Notification
import android.content.ComponentName
import android.content.Context
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.drawable.BitmapDrawable
import android.graphics.drawable.Drawable
import android.provider.Settings
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.Connection
import java.io.ByteArrayOutputStream
import java.util.Base64

/**
 * Mirrors phone notifications (and incoming calls) to the PC's notch.
 * Only runs when the user grants notification access in Android settings.
 */
class NotifListener : NotificationListenerService() {

    private val iconCache = HashMap<String, String>()
    private var lastKey = ""
    private var lastAt = 0L
    private var ringingKey: String? = null

    override fun onNotificationPosted(sbn: StatusBarNotification?) {
        val s = sbn ?: return
        try {
            if (!Connection.connected) return
            if (s.packageName == packageName) return
            val n = s.notification ?: return
            val isCall = n.category == Notification.CATEGORY_CALL
            if (isCall) {
                if (!Prefs.mirrorCalls) return
                // ongoing call notification after answering also has CATEGORY_CALL; only "ringing" ones have a full screen intent
                if (n.fullScreenIntent == null && ringingKey == null) return
                ringingKey = s.key
                val ex = n.extras
                val name = (ex.getCharSequence(Notification.EXTRA_TITLE) ?: "").toString()
                Connection.cmd("call", "state" to "ringing", "name" to name, "app" to appName(s.packageName), "icon" to icon(s.packageName))
                return
            }
            if (!Prefs.mirrorNotifs) return
            if (s.packageName in Prefs.mutedApps) return
            if (s.isOngoing || (n.flags and Notification.FLAG_GROUP_SUMMARY) != 0) return
            if (n.category == Notification.CATEGORY_PROGRESS || n.category == Notification.CATEGORY_TRANSPORT || n.category == Notification.CATEGORY_SERVICE) return
            val ex = n.extras
            val title = (ex.getCharSequence(Notification.EXTRA_TITLE) ?: "").toString()
            val text = (ex.getCharSequence(Notification.EXTRA_BIG_TEXT) ?: ex.getCharSequence(Notification.EXTRA_TEXT) ?: "").toString()
            if (title.isBlank() && text.isBlank()) return
            // drop exact duplicates (apps re-post the same notification)
            val key = s.packageName + "|" + title + "|" + text
            val now = System.currentTimeMillis()
            if (key == lastKey && now - lastAt < 4000) return
            lastKey = key; lastAt = now
            Connection.cmd(
                "notif",
                "app" to appName(s.packageName), "pkg" to s.packageName,
                "title" to title.take(120), "text" to text.take(300),
                "icon" to icon(s.packageName)
            )
        } catch (_: Exception) { }
    }

    override fun onNotificationRemoved(sbn: StatusBarNotification?) {
        val s = sbn ?: return
        if (s.key == ringingKey) {
            ringingKey = null
            if (Connection.connected) Connection.cmd("call", "state" to "idle")
        }
    }

    private fun appName(pkg: String): String = try {
        val pm = packageManager
        pm.getApplicationLabel(pm.getApplicationInfo(pkg, 0)).toString()
    } catch (_: PackageManager.NameNotFoundException) { pkg }

    private fun icon(pkg: String): String {
        iconCache[pkg]?.let { return it }
        val b64 = try {
            val d = packageManager.getApplicationIcon(pkg)
            val bmp = toBitmap(d, 96)
            val out = ByteArrayOutputStream()
            bmp.compress(Bitmap.CompressFormat.PNG, 100, out)
            Base64.getEncoder().encodeToString(out.toByteArray())
        } catch (_: Exception) { "" }
        iconCache[pkg] = b64
        return b64
    }

    private fun toBitmap(d: Drawable, size: Int): Bitmap {
        if (d is BitmapDrawable && d.bitmap != null) return Bitmap.createScaledBitmap(d.bitmap, size, size, true)
        val bmp = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
        val c = Canvas(bmp)
        d.setBounds(0, 0, size, size)
        d.draw(c)
        return bmp
    }

    companion object {
        fun hasAccess(ctx: Context): Boolean {
            val flat = Settings.Secure.getString(ctx.contentResolver, "enabled_notification_listeners") ?: return false
            val me = ComponentName(ctx, NotifListener::class.java).flattenToString()
            return flat.split(":").any { it == me }
        }
    }
}
