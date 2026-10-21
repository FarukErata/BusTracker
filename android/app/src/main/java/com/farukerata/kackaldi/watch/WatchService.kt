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
import com.farukerata.kackaldi.data.SeenBus
import com.farukerata.kackaldi.data.TriggerKind
import com.farukerata.kackaldi.data.Watch
import com.farukerata.kackaldi.data.WatchState
import com.farukerata.kackaldi.data.WatchStep
import com.farukerata.kackaldi.data.WatchTrigger
import com.farukerata.kackaldi.data.missed
import com.farukerata.kackaldi.data.nextStep
import com.farukerata.kackaldi.ui.busWhere
import com.farukerata.kackaldi.ui.minutes
import com.farukerata.kackaldi.ui.stopsAway
import com.farukerata.kackaldi.ui.titleCase
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json

class WatchService : Service() {
    private enum class Phase { Watching, Gone }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var job: Job? = null
    private var wakeLock: PowerManager.WakeLock? = null
    private var watch: Watch? = null
    private var state: WatchState? = null
    private var step: WatchStep? = null
    private var phase = Phase.Watching
    private var hidden = false
    private var shownId = ONGOING_ID
    private var ringing = false
    private val app get() = application as KacKaldiApplication
    private val notifications get() = getSystemService(NotificationManager::class.java)

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> finish()
            ACTION_MISSED -> missedIt()
            ACTION_HIDE -> if (phase == Phase.Gone) finish() else hidden = true
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

    private fun start(next: Watch) {
        createChannels()
        watch = next
        state = null
        step = null
        phase = Phase.Watching
        hidden = false
        showFresh(watching(next, "Looking for ${next.lineNames}…"))
        notifications.cancel(ENDED_ID)
        app.watch.value = next
        follow()
    }

    private fun missedIt() {
        val current = state ?: return
        if (current.following == null) return
        state = missed(current)
        step = null
        phase = Phase.Watching
        hidden = false
        follow()
    }

    private fun follow() {
        val current = watch ?: return
        holdCpu()
        job?.cancel()
        job = scope.launch { loop(current) }
    }

    private suspend fun loop(watch: Watch) {
        while (scope.isActive) {
            val now = System.currentTimeMillis()
            if (state?.following == null && now > watch.readyAtMillis + GIVE_UP_MILLIS) {
                notifications.notify(ENDED_ID, ended(watch, "No ${watch.lineNames} came by ${clock(now)}"))
                finish()
                return
            }
            val seen = arrivals(watch)
            if (seen == null) {
                post(watching(watch, "Can’t reach the server, trying again"))
            } else {
                val next = nextStep(watch.trigger, seen.buses, seen.passed, state, armed = now >= watch.readyAtMillis)
                state = next.state
                step = next
                val gone = next.gone
                if (gone != null) {
                    showGone(watch, gone, next.next)
                    return
                }
                when {
                    next.fire != null -> showFresh(watching(watch, null, loud = true)).also { ringing = true }
                    ringing -> showFresh(watching(watch, null)).also { ringing = false }
                    else -> post(watching(watch, null))
                }
            }
            val close = state?.following != null || step?.shown?.let { watch.trigger.near(it.bus) } == true
            delay(if (close) NEAR_REFRESH_MILLIS else FAR_REFRESH_MILLIS)
        }
    }

    private suspend fun showGone(watch: Watch, gone: SeenBus, next: SeenBus?) {
        phase = Phase.Gone
        hidden = false
        releaseCpu()
        ringing = false
        showFresh(goneNotification(watch, gone, next))
        delay(GONE_MILLIS)
        finish()
    }

    private fun post(notification: Notification) {
        if (!hidden) notifications.notify(shownId, notification)
    }

    private fun showFresh(notification: Notification) {
        val previous = shownId
        shownId = if (previous == ONGOING_ID) SWAP_ID else ONGOING_ID
        hidden = false
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startForeground(shownId, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
        } else {
            startForeground(shownId, notification)
        }
        notifications.cancel(previous)
    }

    private class Seen(val buses: List<SeenBus>, val passed: Set<String>)

    private suspend fun arrivals(watch: Watch): Seen? = coroutineScope {
        val results = watch.lines.map { line ->
            async { runCatching { app.api.lineArrivals(line.line, watch.stopCode, line.direction) }.getOrNull()?.let { line.line to it } }
        }.awaitAll().filterNotNull()
        if (results.isEmpty()) return@coroutineScope null
        Seen(
            results.flatMap { (line, response) -> response.approaching.map { SeenBus(line, it) } },
            results.flatMap { (_, response) -> response.recentlyPassed.map { it.doorNo } }.toSet(),
        )
    }

    private fun holdCpu() {
        if (wakeLock?.isHeld == true) return
        wakeLock = getSystemService(PowerManager::class.java)
            .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "KacKaldi:watch")
            .apply { acquire(MAX_WAKE_MILLIS) }
    }

    private fun releaseCpu() {
        wakeLock?.takeIf { it.isHeld }?.release()
        wakeLock = null
    }

    private fun finish() {
        job?.cancel()
        releaseCpu()
        watch = null
        state = null
        app.watch.value = null
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun watching(watch: Watch, problem: String?, loud: Boolean = false): Notification {
        val alerted = state?.following != null
        val seen = step?.shown
        val builder = base(watch, if (alerted) ALERT_CHANNEL else ONGOING_CHANNEL)
            .setContentTitle(if (alerted && seen != null) alertTitle(watch, seen) else "${watch.lineNames} · ${titleCase(watch.stopName)}")
            .setContentText(problem ?: watchingText(watch, alerted))
            .setSubText(watchingSubText(watch, alerted))
            .setOnlyAlertOnce(!loud)
            .setSilent(!loud)
            .setPriority(if (alerted) NotificationCompat.PRIORITY_HIGH else NotificationCompat.PRIORITY_LOW)
            .setCategory(if (alerted) NotificationCompat.CATEGORY_ALARM else NotificationCompat.CATEGORY_PROGRESS)
        if (alerted) builder.addAction(0, "Missed it", serviceIntent(ACTION_MISSED, 2))
        builder.addAction(0, "Stop", serviceIntent(ACTION_STOP, 1))
        countdown(builder, seen?.takeIf { problem == null })
        return builder.build().apply { if (loud) flags = flags or Notification.FLAG_INSISTENT }
    }

    private fun watchingText(watch: Watch, alerted: Boolean): String {
        val current = step
        return buildList {
            current?.lost?.let { add("Lost track of that ${it.line}") }
            add(perLineText(watch, current) ?: current?.shown?.let { describe(watch, it) } ?: nothingYet(watch))
            if (alerted) current?.next?.let { add("then ${it.line} ${eta(it)}") }
        }.joinToString(" · ")
    }

    private fun perLineText(watch: Watch, current: WatchStep?): String? {
        val buses = current?.perLine.orEmpty()
        if (state?.following != null || watch.lines.distinctBy { it.line }.size < 2 || buses.size < 2) return null
        return buses.joinToString(" · ", transform = ::brief)
    }

    private fun brief(seen: SeenBus): String {
        val bus = seen.bus
        if (bus.notDeparted) return "${seen.line} at the first stop"
        val eta = bus.etaSeconds?.let { "${minutes(it)} min " }.orEmpty()
        return "${seen.line} $eta(${stopsAway(bus.stopsAway).lowercase()})"
    }

    private fun nothingYet(watch: Watch): String =
        if (state?.skipped?.isNotEmpty() == true) "Waiting for the next ${watch.lineNames}" else "No ${watch.lineNames} on the way yet"

    private fun watchingSubText(watch: Watch, alerted: Boolean): String = when {
        alerted -> "Time to go to ${titleCase(watch.stopName)}"
        System.currentTimeMillis() >= watch.readyAtMillis -> "Alert ${triggerText(watch.trigger)}"
        else -> "Ready at ${clock(watch.readyAtMillis)} · alert ${triggerText(watch.trigger)}"
    }

    private fun countdown(builder: NotificationCompat.Builder, seen: SeenBus?) {
        val seconds = seen?.bus?.etaSeconds?.takeIf { !seen.bus.notDeparted }
        if (seconds == null) {
            builder.setShowWhen(false)
            return
        }
        builder.setWhen(System.currentTimeMillis() + seconds * 1000L).setUsesChronometer(true).setChronometerCountDown(true).setShowWhen(true)
        builder.setShortCriticalText("${minutes(seconds)} min")
    }

    private fun goneNotification(watch: Watch, gone: SeenBus, next: SeenBus?): Notification =
        base(watch, ONGOING_CHANNEL)
            .setContentTitle("Your ${gone.line} left ${titleCase(watch.stopName)} at ${clock(System.currentTimeMillis())}")
            .setContentText(next?.let { "Next: ${it.line} ${eta(it)}" } ?: "No other ${watch.lineNames} on the way yet")
            .setSilent(true)
            .setShowWhen(false)
            .setRequestPromotedOngoing(false)
            .addAction(0, "OK", serviceIntent(ACTION_STOP, 1))
            .addAction(0, "Missed it", serviceIntent(ACTION_MISSED, 2))
            .build()

    private fun base(watch: Watch, channel: String): NotificationCompat.Builder =
        NotificationCompat.Builder(this, channel)
            .setSmallIcon(R.drawable.ic_notification)
            .setOngoing(true)
            .setRequestPromotedOngoing(true)
            .setContentIntent(openStop(watch))
            .setDeleteIntent(serviceIntent(ACTION_HIDE, 3))

    private fun ended(watch: Watch, text: String): Notification =
        NotificationCompat.Builder(this, ONGOING_CHANNEL)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle("${watch.lineNames} · ${titleCase(watch.stopName)}")
            .setContentText(text)
            .setAutoCancel(true)
            .setContentIntent(openStop(watch))
            .build()

    private fun eta(seen: SeenBus): String =
        seen.bus.etaSeconds?.takeIf { !seen.bus.notDeparted }?.let { "in ${minutes(it)} min" } ?: busWhere(seen.bus.notDeparted, seen.bus.stopsAway).lowercase()

    private fun alertTitle(watch: Watch, seen: SeenBus): String {
        val bus = seen.bus
        val eta = bus.etaSeconds?.takeIf { !bus.notDeparted }
        return when {
            watch.trigger.atStopName != null && bus.stopsAway > 0 -> "Leave now · ${seen.line} passed ${titleCase(watch.trigger.atStopName)}"
            eta != null -> "Leave now · ${seen.line} is ${minutes(eta)} min away"
            else -> "Leave now · ${seen.line} ${busWhere(bus.notDeparted, bus.stopsAway).lowercase()}"
        }
    }

    private fun describe(watch: Watch, seen: SeenBus): String = buildList {
        val bus = seen.bus
        if (watch.lines.size > 1) add(seen.line)
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

    private fun clock(millis: Long): String =
        Instant.ofEpochMilli(millis).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("HH:mm"))

    private fun openStop(watch: Watch): PendingIntent = PendingIntent.getActivity(
        this,
        0,
        Intent(this, MainActivity::class.java).putExtra(EXTRA_STOP, watch.stopCode).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    private fun serviceIntent(action: String, code: Int): PendingIntent = PendingIntent.getService(
        this,
        code,
        Intent(this, WatchService::class.java).setAction(action),
        PendingIntent.FLAG_IMMUTABLE,
    )

    private fun createChannels() {
        notifications.createNotificationChannel(
            NotificationChannel(ONGOING_CHANNEL, "Watched bus", NotificationManager.IMPORTANCE_LOW).apply {
                description = "Live countdown while you wait for a bus"
            },
        )
        notifications.deleteNotificationChannel(OLD_ALERT_CHANNEL)
        notifications.createNotificationChannel(
            NotificationChannel(ALERT_CHANNEL, "Time to go", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "Rings until you look, when it is time to leave for your bus"
                enableVibration(true)
                vibrationPattern = LEAVE_VIBRATION
            },
        )
    }

    companion object {
        const val EXTRA_STOP = "com.farukerata.kackaldi.extra.STOP"
        private const val EXTRA_WATCH = "com.farukerata.kackaldi.extra.WATCH"
        private const val ACTION_START = "com.farukerata.kackaldi.action.START_WATCH"
        private const val ACTION_STOP = "com.farukerata.kackaldi.action.STOP_WATCH"
        private const val ACTION_MISSED = "com.farukerata.kackaldi.action.MISSED_BUS"
        private const val ACTION_HIDE = "com.farukerata.kackaldi.action.HIDE_WATCH"
        private const val ONGOING_CHANNEL = "watch"
        private const val ALERT_CHANNEL = "leave"
        private const val OLD_ALERT_CHANNEL = "alerts"
        private const val ONGOING_ID = 41
        private const val SWAP_ID = 44
        private val LEAVE_VIBRATION = longArrayOf(0, 700, 300, 700, 300, 700)
        private const val ENDED_ID = 43
        private const val NEAR_REFRESH_MILLIS = 30_000L
        private const val FAR_REFRESH_MILLIS = 60_000L
        private const val GIVE_UP_MILLIS = 90 * 60 * 1000L
        private const val GONE_MILLIS = 10 * 60 * 1000L
        private const val MAX_WAKE_MILLIS = 3 * 60 * 60 * 1000L

        fun start(context: Context, watch: Watch) {
            val intent = Intent(context, WatchService::class.java).setAction(ACTION_START).putExtra(EXTRA_WATCH, Json.encodeToString(watch))
            ContextCompat.startForegroundService(context, intent)
        }

        fun stop(context: Context) {
            context.startService(Intent(context, WatchService::class.java).setAction(ACTION_STOP))
        }
    }
}
