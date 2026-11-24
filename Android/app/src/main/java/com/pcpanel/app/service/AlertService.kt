package com.pcpanel.app.service

import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import com.pcpanel.app.PcPanelApp
import com.pcpanel.app.R
import com.pcpanel.app.ui.Str

/**
 * Optional (Settings > Background alerts). Keeps the process alive so overheating alerts
 * still arrive when the app is closed. Uses a minimal-importance notification.
 */
class AlertService : Service() {
    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val n = NotificationCompat.Builder(this, PcPanelApp.CH_SERVICE)
            .setSmallIcon(R.drawable.ic_stat_pcpanel)
            .setContentTitle("PC Panel")
            .setContentText(Str.t("svc.text"))
            .setOngoing(true)
            .setSilent(true)
            .setPriority(NotificationCompat.PRIORITY_MIN)
            .setContentIntent(PcPanelApp.openAppIntent(this))
            .build()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q)
            startForeground(1001, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        else
            startForeground(1001, n)
        return START_STICKY
    }
}
