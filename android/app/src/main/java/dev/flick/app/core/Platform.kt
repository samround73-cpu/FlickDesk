package dev.flick.app.core

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.app.ServiceCompat
import dev.flick.app.Haptic
import dev.flick.app.MainActivity
import dev.flick.app.R
import dev.flick.app.ReceivedFile

/** Notification channels and the notifications Flick posts. */
object Notifications {
    private const val ARRIVALS = "arrivals"
    private const val SERVICE = "receiver"
    private const val TRANSFERS = "transfers"
    const val SERVICE_ID = 1
    private var nextId = 100

    fun createChannels(context: Context) {
        val nm = context.getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel(ARRIVALS, "Files from your PC", NotificationManager.IMPORTANCE_DEFAULT).apply {
            description = "When a file or text arrives while Flick is in the background"
        })
        nm.createNotificationChannel(NotificationChannel(TRANSFERS, "Big transfers", NotificationManager.IMPORTANCE_LOW).apply {
            description = "Progress while a large file or folder is on its way"
            setShowBadge(false)
        })
        nm.createNotificationChannel(NotificationChannel(SERVICE, "Ready to receive", NotificationManager.IMPORTANCE_MIN).apply {
            description = "Keeps Flick listening for your PC. You can hide this channel."
            setShowBadge(false)
        })
    }

    fun service(context: Context): Notification =
        NotificationCompat.Builder(context, SERVICE)
            .setSmallIcon(R.drawable.ic_stat_flick)
            .setContentTitle("Ready to receive from your PC")
            .setContentText("Files you flick from FlickDesk arrive even with Flick closed")
            .setOngoing(true)
            .setSilent(true)
            .setContentIntent(openApp(context, null, null))
            .build()

    fun arrival(context: Context, file: ReceivedFile, count: Int) {
        val title = if (count > 1) "$count files from ${file.fromPc}" else file.name
        val text = if (count > 1) "Latest: ${file.name}" else "From ${file.fromPc} · tap to open"
        val view = Intent(Intent.ACTION_VIEW).setDataAndType(file.uri, file.mime)
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        val tap = PendingIntent.getActivity(context, nextId, view, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        post(context, NotificationCompat.Builder(context, ARRIVALS)
            .setSmallIcon(R.drawable.ic_stat_flick)
            .setContentTitle(title)
            .setContentText(text)
            .setAutoCancel(true)
            .setContentIntent(tap)
            .build())
    }

    /** An ongoing progress notification for a big transfer; [fraction] < 0 = indeterminate. */
    fun progress(context: Context, key: String, title: String, text: String, fraction: Float) {
        val n = NotificationCompat.Builder(context, TRANSFERS)
            .setSmallIcon(R.drawable.ic_stat_flick)
            .setContentTitle(title)
            .setContentText(text)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setProgress(1000, (fraction.coerceIn(0f, 1f) * 1000).toInt(), fraction < 0)
            .setContentIntent(openApp(context, null, null))
            .build()
        try {
            NotificationManagerCompat.from(context).notify(key, PROGRESS_ID, n)
        } catch (_: SecurityException) { }
    }

    /** Replaces a progress notification with a final line ("Paused – …"), or removes it. */
    fun endProgress(context: Context, key: String, title: String? = null, text: String? = null) {
        val nm = NotificationManagerCompat.from(context)
        if (title == null) {
            nm.cancel(key, PROGRESS_ID)
            return
        }
        try {
            nm.notify(key, PROGRESS_ID, NotificationCompat.Builder(context, TRANSFERS)
                .setSmallIcon(R.drawable.ic_stat_flick)
                .setContentTitle(title)
                .setContentText(text)
                .setAutoCancel(true)
                .setSilent(true)
                .setContentIntent(openApp(context, null, null))
                .build())
        } catch (_: SecurityException) { }
    }

    private const val PROGRESS_ID = 7

    fun clip(context: Context, pcName: String, text: String, copied: Boolean) {
        post(context, NotificationCompat.Builder(context, ARRIVALS)
            .setSmallIcon(R.drawable.ic_stat_flick)
            .setContentTitle(if (copied) "Copied from $pcName" else "$pcName sent text")
            .setContentText(text.take(200))
            .setStyle(NotificationCompat.BigTextStyle().bigText(text.take(1000)))
            .setAutoCancel(true)
            .setContentIntent(openApp(context, "clipboard", null))
            .build())
    }

    private fun openApp(context: Context, tab: String?, unused: String?): PendingIntent {
        val intent = Intent(context, MainActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)
        if (tab != null) intent.putExtra(MainActivity.EXTRA_TAB, tab)
        return PendingIntent.getActivity(context, tab.hashCode(), intent, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
    }

    private fun post(context: Context, n: Notification) {
        try {
            NotificationManagerCompat.from(context).notify(nextId++, n)
        } catch (_: SecurityException) {
            // notifications not allowed – the history tab still has it
        }
    }
}

/** Short vibration patterns for "something just arrived / left". */
object Haptics {
    fun play(context: Context, style: Haptic) {
        if (style == Haptic.OFF) return
        val vibrator: Vibrator = if (Build.VERSION.SDK_INT >= 31) {
            context.getSystemService(VibratorManager::class.java)?.defaultVibrator ?: return
        } else {
            @Suppress("DEPRECATION")
            context.getSystemService(Vibrator::class.java) ?: return
        }
        if (!vibrator.hasVibrator()) return
        val effect = when (style) {
            Haptic.TICK -> VibrationEffect.createPredefined(VibrationEffect.EFFECT_TICK)
            Haptic.THUD -> VibrationEffect.createPredefined(VibrationEffect.EFFECT_HEAVY_CLICK)
            else -> if (vibrator.hasAmplitudeControl()) {
                // three fading pulses – a ripple you can feel
                VibrationEffect.createWaveform(longArrayOf(0, 14, 45, 12, 55, 9), intArrayOf(0, 200, 0, 120, 0, 60), -1)
            } else {
                VibrationEffect.createWaveform(longArrayOf(0, 14, 45, 10, 55, 7), -1)
            }
        }
        runCatching { vibrator.vibrate(effect) }
    }
}

/**
 * Foreground service that keeps the process (and so FlickCore's receiver) alive while Flick
 * isn't on screen. It owns nothing itself.
 */
class ReceiveService : Service() {
    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        FlickCore.init(application)
        ServiceCompat.startForeground(
            this,
            Notifications.SERVICE_ID,
            Notifications.service(this),
            if (Build.VERSION.SDK_INT >= 29) ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE else 0,
        )
        return START_STICKY
    }
}
