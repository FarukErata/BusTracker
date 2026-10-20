package com.farukerata.kackaldi.watch

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
import android.os.PowerManager
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.farukerata.kackaldi.KacKaldiApplication
import com.farukerata.kackaldi.MainActivity
import com.farukerata.kackaldi.R
import com.farukerata.kackaldi.data.ArrivalView
import com.farukerata.kackaldi.data.TriggerKind
import com.farukerata.kackaldi.data.Watch
import com.farukerata.kackaldi.data.WatchState
import com.farukerata.kackaldi.data.WatchTrigger
import com.farukerata.kackaldi.data.nextStep
import com.farukerata.kackaldi.ui.busWhere
import com.farukerata.kackaldi.ui.minutes
import com.farukerata.kackaldi.ui.titleCase
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json

class WatchService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var job: Job? = null
    private var wakeLock: PowerManager.WakeLock? = null
    private val app get() = application as KacKaldiApplication
    private val notifications get() = getSystemService(NotificationManager::class.java)

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> finish()
            ACTION_START -> intent.getStringExtra(EXTRA_WATCH)?.let { start(Json.decodeFromString<Watch>(it)) } ?: finish()
            else -> finish()
        }
        return START_REDELIVER_INTENT
    }

    override fun onDestroy() {
        scope.cancel()
        releaseCpu()
        if (app.watch.value != null) app.watch.value = null
        super.onDestroy()
    }

    private fun start(watch: Watch) {
        createChannels()
        val first = notification(watch, null, "Looking for ${watch.line}…")
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startForeground(ONGOING_ID, first, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
        } else {
            startForeground(ONGOING_ID, first)
        }
        app.watch.value = watch
        holdCpu()
        job?.cancel()
        job = scope.launch { follow(watch) }
    }

    private suspend fun follow(watch: Watch) {
        var state: WatchState? = null
        var shown: ArrivalView? = null
        while (scope.isActive) {
            if (System.currentTimeMillis() - watch.startedAt > MAX_WATCH_MILLIS) {
                finish()
                return
            }
            val response = runCatching { app.api.lineArrivals(watch.line, watch.stopCode, watch.direction) }.getOrNull()
            if (response == null) {
                notifications.notify(ONGOING_ID, notification(watch, shown, "Can’t reach the server, trying again", alerted = state?.alerted != null))
            } else {
                val step = nextStep(watch.trigger, response.approaching, state)
                state = step.state
                shown = step.shown
                if (step.done) {
                    finish()
                    return
                }
                notifications.notify(ONGOING_ID, notification(watch, shown, null, alerted = state.alerted != null, loud = step.fire != null))
            }
            val close = state?.alerted != null || shown?.let(watch.trigger::near) == true
            delay(if (close) NEAR_REFRESH_MILLIS else FAR_REFRESH_MILLIS)
        }
    }

    private fun holdCpu() {
        if (wakeLock?.isHeld == true) return
        wakeLock = getSystemService(PowerManager::class.java)
            .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "KacKaldi:watch")
            .apply { acquire(MAX_WATCH_MILLIS + WAKE_LOCK_SLACK_MILLIS) }
    }

    private fun releaseCpu() {
        wakeLock?.takeIf { it.isHeld }?.release()
        wakeLock = null
    }

    private fun finish() {
        job?.cancel()
        releaseCpu()
        app.watch.value = null
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun notification(watch: Watch, bus: ArrivalView?, problem: String?, alerted: Boolean = false, loud: Boolean = false): Notification {
        val text = problem ?: bus?.let(::describe) ?: "No ${watch.line} on the way yet"
        val title = if (alerted && bus != null) alertTitle(watch, bus) else "${watch.line} · ${titleCase(watch.stopName)}"
        val builder = NotificationCompat.Builder(this, if (alerted) ALERT_CHANNEL else ONGOING_CHANNEL)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(title)
            .setContentText(text)
            .setSubText(if (alerted) "Time to go to ${titleCase(watch.stopName)}" else "Alert ${triggerText(watch.trigger)}")
            .setOngoing(true)
            .setOnlyAlertOnce(!loud)
            .setSilent(!loud)
            .setPriority(if (alerted) NotificationCompat.PRIORITY_HIGH else NotificationCompat.PRIORITY_LOW)
            .setCategory(if (alerted) NotificationCompat.CATEGORY_REMINDER else NotificationCompat.CATEGORY_PROGRESS)
            .setContentIntent(openStop(watch))
            .setDeleteIntent(stopIntent())
            .addAction(0, "Stop watching", stopIntent())
        val eta = bus?.etaSeconds?.takeIf { !bus.notDeparted && problem == null }
        if (eta != null) {
            builder.setWhen(System.currentTimeMillis() + eta * 1000L).setUsesChronometer(true).setChronometerCountDown(true).setShowWhen(true)
        } else {
            builder.setShowWhen(false)
        }
        return builder.build()
    }

    private fun alertTitle(watch: Watch, bus: ArrivalView): String {
        val eta = bus.etaSeconds?.takeIf { !bus.notDeparted }
        return when {
            watch.trigger.atStopName != null && bus.stopsAway > 0 -> "${watch.line} passed ${titleCase(watch.trigger.atStopName)}"
            eta != null -> "${watch.line} is ${minutes(eta)} min away"
            else -> "${watch.line}: ${busWhere(bus.notDeparted, bus.stopsAway).lowercase()}"
        }
    }

    private fun describe(bus: ArrivalView): String = buildList {
        bus.etaSeconds?.takeIf { !bus.notDeparted }?.let { add("${minutes(it)} min") }
        add(busWhere(bus.notDeparted, bus.stopsAway))
        if (!bus.notDeparted) bus.lastStopName?.let { add("now at ${titleCase(it)}") }
    }.joinToString(" · ")

    private fun triggerText(trigger: WatchTrigger): String = when {
        trigger.atStopName != null -> "at ${titleCase(trigger.atStopName)}"
        trigger.kind == TriggerKind.Minutes -> "at ${trigger.value} min"
        trigger.value == 1 -> "at 1 stop"
        else -> "at ${trigger.value} stops"
    }

    private fun openStop(watch: Watch): PendingIntent = PendingIntent.getActivity(
        this,
        0,
        Intent(this, MainActivity::class.java).putExtra(EXTRA_STOP, watch.stopCode).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    private fun stopIntent(): PendingIntent = PendingIntent.getService(
        this,
        1,
        Intent(this, WatchService::class.java).setAction(ACTION_STOP),
        PendingIntent.FLAG_IMMUTABLE,
    )

    private fun createChannels() {
        notifications.createNotificationChannel(
            NotificationChannel(ONGOING_CHANNEL, "Watched bus", NotificationManager.IMPORTANCE_LOW).apply {
                description = "Live countdown while you wait for a bus"
            },
        )
        notifications.createNotificationChannel(
            NotificationChannel(ALERT_CHANNEL, "Bus alerts", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "Tells you when it is time to go"
                enableVibration(true)
            },
        )
    }

    companion object {
        const val EXTRA_STOP = "com.farukerata.kackaldi.extra.STOP"
        private const val EXTRA_WATCH = "com.farukerata.kackaldi.extra.WATCH"
        private const val ACTION_START = "com.farukerata.kackaldi.action.START_WATCH"
        private const val ACTION_STOP = "com.farukerata.kackaldi.action.STOP_WATCH"
        private const val ONGOING_CHANNEL = "watch"
        private const val ALERT_CHANNEL = "alerts"
        private const val ONGOING_ID = 41
        private const val NEAR_REFRESH_MILLIS = 30_000L
        private const val FAR_REFRESH_MILLIS = 60_000L
        private const val WAKE_LOCK_SLACK_MILLIS = 60_000L
        private const val MAX_WATCH_MILLIS = 2 * 60 * 60 * 1000L

        fun start(context: Context, watch: Watch) {
            val intent = Intent(context, WatchService::class.java).setAction(ACTION_START).putExtra(EXTRA_WATCH, Json.encodeToString(watch))
            ContextCompat.startForegroundService(context, intent)
        }

        fun stop(context: Context) {
            context.startService(Intent(context, WatchService::class.java).setAction(ACTION_STOP))
        }
    }
}
