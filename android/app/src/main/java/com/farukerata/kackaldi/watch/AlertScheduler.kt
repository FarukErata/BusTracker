package com.farukerata.kackaldi.watch

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import com.farukerata.kackaldi.data.AlertStore
import com.farukerata.kackaldi.data.BusAlert
import com.farukerata.kackaldi.data.Watch
import java.time.ZonedDateTime

object AlertScheduler {
    fun canSchedule(context: Context): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.S || context.getSystemService(AlarmManager::class.java).canScheduleExactAlarms()

    suspend fun sync(context: Context, store: AlertStore) {
        val now = ZonedDateTime.now()
        store.all().forEach { alert ->
            val next = alert.nextReady(now)
            when {
                next != null -> schedule(context, alert, next)
                alert.repeats -> cancel(context, alert.id)
                alert.dueNow(now) == null -> {
                    cancel(context, alert.id)
                    store.remove(alert.id)
                }
            }
        }
    }

    suspend fun save(context: Context, store: AlertStore, alert: BusAlert) {
        val now = ZonedDateTime.now()
        val due = alert.dueNow(now)
        if (due != null) WatchService.start(context, watchFor(alert, due))
        if (due != null && !alert.repeats) {
            cancel(context, alert.id)
            return
        }
        store.save(alert)
        alert.nextReady(now)?.let { schedule(context, alert, it) } ?: cancel(context, alert.id)
    }

    suspend fun remove(context: Context, store: AlertStore, id: String) {
        cancel(context, id)
        store.remove(id)
    }

    suspend fun fire(context: Context, store: AlertStore, id: String) {
        val alert = store.find(id) ?: return
        val now = ZonedDateTime.now()
        alert.dueNow(now)?.let { WatchService.start(context, watchFor(alert, it)) }
        if (!alert.repeats) {
            store.remove(id)
            return
        }
        alert.nextReady(now)?.let { schedule(context, alert, it) }
    }

    fun watchFor(alert: BusAlert, ready: ZonedDateTime): Watch =
        Watch(alert.stopCode, alert.stopName, alert.lines, alert.trigger, ready.toInstant().toEpochMilli(), System.currentTimeMillis(), alert.id)

    private fun schedule(context: Context, alert: BusAlert, ready: ZonedDateTime) {
        val alarms = context.getSystemService(AlarmManager::class.java)
        val at = ready.minusMinutes(BusAlert.LEAD_MINUTES).toInstant().toEpochMilli()
        val intent = pending(context, alert.id)
        if (canSchedule(context)) alarms.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, intent)
        else alarms.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, intent)
    }

    private fun cancel(context: Context, id: String) {
        context.getSystemService(AlarmManager::class.java).cancel(pending(context, id))
    }

    private fun pending(context: Context, id: String): PendingIntent = PendingIntent.getBroadcast(
        context,
        id.hashCode(),
        Intent(context, AlertReceiver::class.java).setAction(AlertReceiver.ACTION_FIRE).putExtra(AlertReceiver.EXTRA_ID, id),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )
}
