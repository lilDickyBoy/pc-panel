package com.pcpanel.app.service

import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.media.AudioAttributes
import android.media.AudioManager
import android.media.MediaPlayer
import android.media.RingtoneManager
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.app.NotificationCompat
import com.pcpanel.app.PcPanelApp
import com.pcpanel.app.R
import com.pcpanel.app.ui.Str

/** "Find my phone": rings loudly (even in silent mode) for 30 seconds. */
object Ringer {
    var ringing by mutableStateOf(false)
        private set

    private var player: MediaPlayer? = null
    private var oldVolume = -1
    private val handler = Handler(Looper.getMainLooper())
    private const val NOTIF_ID = 1100

    fun start(ctx: Context) {
        handler.post {
            if (ringing) return@post
            val app = ctx.applicationContext
            ringing = true
            try {
                val am = app.getSystemService(Context.AUDIO_SERVICE) as AudioManager
                oldVolume = am.getStreamVolume(AudioManager.STREAM_ALARM)
                am.setStreamVolume(AudioManager.STREAM_ALARM, am.getStreamMaxVolume(AudioManager.STREAM_ALARM), 0)
                val uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM) ?: RingtoneManager.getDefaultUri(RingtoneManager.TYPE_RINGTONE)
                player = MediaPlayer().apply {
                    setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_ALARM).setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build())
                    setDataSource(app, uri)
                    isLooping = true
                    prepare()
                    start()
                }
            } catch (_: Exception) { }
            try {
                val vib = app.getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator
                vib?.vibrate(VibrationEffect.createWaveform(longArrayOf(0, 500, 400), 0))
            } catch (_: Exception) { }
            try {
                val stop = PendingIntent.getBroadcast(app, 1, Intent(app, RingStopReceiver::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
                val n = NotificationCompat.Builder(app, PcPanelApp.CH_ALERTS)
                    .setSmallIcon(R.drawable.ic_stat_pcpanel)
                    .setContentTitle(Str.t("ring.title"))
                    .setContentText(Str.t("ring.text"))
                    .setPriority(NotificationCompat.PRIORITY_MAX)
                    .setCategory(NotificationCompat.CATEGORY_ALARM)
                    .setOngoing(true)
                    .setContentIntent(PcPanelApp.openAppIntent(app))
                    .setDeleteIntent(stop)
                    .addAction(0, Str.t("ring.stop"), stop)
                    .build()
                (app.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager).notify(NOTIF_ID, n)
            } catch (_: Exception) { }
            handler.postDelayed({ stop(app) }, 30_000)
        }
    }

    fun stop(ctx: Context) {
        handler.post {
            if (!ringing) return@post
            ringing = false
            handler.removeCallbacksAndMessages(null)
            try { player?.stop(); player?.release() } catch (_: Exception) { }
            player = null
            try {
                val am = ctx.applicationContext.getSystemService(Context.AUDIO_SERVICE) as AudioManager
                if (oldVolume >= 0) am.setStreamVolume(AudioManager.STREAM_ALARM, oldVolume, 0)
            } catch (_: Exception) { }
            try { (ctx.applicationContext.getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator)?.cancel() } catch (_: Exception) { }
            try { (ctx.applicationContext.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager).cancel(NOTIF_ID) } catch (_: Exception) { }
        }
    }
}

class RingStopReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent?) = Ringer.stop(context)
}
