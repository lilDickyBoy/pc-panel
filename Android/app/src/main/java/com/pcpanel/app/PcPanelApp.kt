package com.pcpanel.app

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.content.ContextCompat
import androidx.lifecycle.DefaultLifecycleObserver
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.ProcessLifecycleOwner
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.Connection
import com.pcpanel.app.service.AlertService
import com.pcpanel.app.ui.Str

class PcPanelApp : Application() {

    override fun onCreate() {
        super.onCreate()
        Prefs.init(this)
        Str.init(this)
        Connection.init(this)
        createChannels()

        ProcessLifecycleOwner.get().lifecycle.addObserver(object : DefaultLifecycleObserver {
            override fun onStart(owner: LifecycleOwner) {
                Connection.setForeground(true)
                // Android only allows starting a foreground service while the app is visible
                if ((Prefs.bgAlerts || !Prefs.powerSave) && Prefs.pc != null) startAlertService(this@PcPanelApp)
            }

            override fun onStop(owner: LifecycleOwner) {
                Connection.setForeground(false)
            }
        })
    }

    private fun createChannels() {
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(
            NotificationChannel(CH_ALERTS, "PC alerts", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "Overheating warnings and Find my phone"
            }
        )
        nm.createNotificationChannel(
            NotificationChannel(CH_SERVICE, "Background connection", NotificationManager.IMPORTANCE_MIN).apply {
                description = "Keeps PC Panel connected in the background"
                setShowBadge(false)
            }
        )
        nm.createNotificationChannel(
            NotificationChannel(CH_FILES, "Files from PC", NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = "Files your PC sent to this phone"
            }
        )
    }

    companion object {
        const val CH_ALERTS = "alerts"
        const val CH_SERVICE = "service"
        const val CH_FILES = "files"

        fun startAlertService(ctx: Context) {
            try { ContextCompat.startForegroundService(ctx, Intent(ctx, AlertService::class.java)) } catch (_: Exception) { }
        }

        fun stopAlertService(ctx: Context) {
            try { ctx.stopService(Intent(ctx, AlertService::class.java)) } catch (_: Exception) { }
        }

        fun openAppIntent(ctx: Context): PendingIntent = PendingIntent.getActivity(
            ctx, 0,
            Intent(ctx, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
    }
}
