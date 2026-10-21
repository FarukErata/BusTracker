package com.farukerata.kackaldi.data

import java.time.LocalTime
import java.time.ZonedDateTime
import kotlinx.serialization.Serializable

@Serializable
enum class TriggerKind { Minutes, Stops }

@Serializable
data class WatchTrigger(val kind: TriggerKind, val value: Int, val atStopName: String? = null) {
    fun reached(bus: ArrivalView): Boolean = when (kind) {
        TriggerKind.Minutes -> !bus.notDeparted && bus.etaSeconds != null && bus.etaSeconds <= value * 60
        TriggerKind.Stops -> bus.stopsAway <= value
    }

    fun near(bus: ArrivalView): Boolean = when (kind) {
        TriggerKind.Minutes -> bus.etaSeconds != null && bus.etaSeconds <= value * 60 + NEAR_EXTRA_SECONDS
        TriggerKind.Stops -> bus.stopsAway <= value + NEAR_EXTRA_STOPS
    }
}

private const val NEAR_EXTRA_SECONDS = 180
private const val NEAR_EXTRA_STOPS = 2

@Serializable
data class AlertLine(val line: String, val direction: String, val directionName: String)

@Serializable
data class BusAlert(
    val id: String,
    val stopCode: String,
    val stopName: String,
    val lines: List<AlertLine>,
    val trigger: WatchTrigger,
    val readyAt: String,
    val days: Set<Int> = emptySet(),
    val enabled: Boolean = true,
) {
    val repeats: Boolean get() = days.isNotEmpty()

    fun readyTime(): LocalTime = LocalTime.parse(readyAt)

    fun nextReady(now: ZonedDateTime): ZonedDateTime? {
        if (!enabled) return null
        return (0L..7L).asSequence()
            .map { now.toLocalDate().plusDays(it) }
            .filter { !repeats || it.dayOfWeek.value in days }
            .map { it.atTime(readyTime()).atZone(now.zone) }
            .firstOrNull { it.minusMinutes(LEAD_MINUTES).isAfter(now) }
    }

    fun dueNow(now: ZonedDateTime): ZonedDateTime? {
        if (!enabled) return null
        val today = now.toLocalDate()
        if (repeats && today.dayOfWeek.value !in days) return null
        val ready = today.atTime(readyTime()).atZone(now.zone)
        return ready.takeIf { !it.minusMinutes(LEAD_MINUTES).isAfter(now) && it.plusMinutes(LATE_MINUTES).isAfter(now) }
    }

    companion object {
        const val LEAD_MINUTES = 15L
        const val LATE_MINUTES = 30L
    }
}

@Serializable
data class Watch(
    val stopCode: String,
    val stopName: String,
    val lines: List<AlertLine>,
    val trigger: WatchTrigger,
    val readyAtMillis: Long,
    val startedAt: Long,
    val alertId: String? = null,
) {
    val lineNames: String get() = lines.map { it.line }.distinct().joinToString(" / ")
}

data class SeenBus(val line: String, val bus: ArrivalView)

data class WatchState(val skipped: Set<String>, val following: SeenBus?)

data class WatchStep(
    val state: WatchState,
    val shown: SeenBus?,
    val fire: SeenBus?,
    val next: SeenBus?,
    val gone: SeenBus? = null,
    val lost: SeenBus? = null,
    val perLine: List<SeenBus> = emptyList(),
)

fun alreadyInside(trigger: WatchTrigger, approaching: List<ArrivalView>): Set<String> =
    approaching.filter(trigger::reached).map { it.doorNo }.toSet()

private fun soonestFirst(buses: List<SeenBus>): List<SeenBus> =
    buses.sortedWith(compareBy({ it.bus.etaSeconds ?: Int.MAX_VALUE }, { it.bus.stopsAway }))

fun nextStep(trigger: WatchTrigger, approaching: List<SeenBus>, passed: Set<String>, previous: WatchState?, armed: Boolean): WatchStep {
    val buses = approaching.map { it.bus }
    var start = previous ?: WatchState(alreadyInside(trigger, buses), null)
    var lost: SeenBus? = null
    val following = start.following
    if (following != null) {
        val door = following.bus.doorNo
        val others = soonestFirst(approaching.filter { it.bus.doorNo != door && it.bus.doorNo !in start.skipped })
        val now = approaching.firstOrNull { it.bus.doorNo == door }
        if (now != null) return WatchStep(start.copy(following = now), now, null, others.firstOrNull())
        if (door in passed) return WatchStep(start, null, null, others.firstOrNull(), gone = following)
        lost = following
        start = WatchState(start.skipped + door, null)
    }
    val present = buses.map { it.doorNo }.toSet()
    val tooEarly = if (armed) emptySet() else alreadyInside(trigger, buses)
    val skipped = start.skipped.intersect(present) + tooEarly
    val waiting = soonestFirst(approaching.filter { it.bus.doorNo !in skipped })
    val fire = if (armed) waiting.firstOrNull { trigger.reached(it.bus) } else null
    val shown = fire ?: waiting.firstOrNull()
    val next = waiting.firstOrNull { it != shown }
    return WatchStep(WatchState(skipped, fire), shown, fire, next, lost = lost, perLine = nearestPerLine(waiting))
}

private fun nearestPerLine(waiting: List<SeenBus>): List<SeenBus> =
    waiting.groupBy { it.line }.values.map { it.first() }

fun missed(state: WatchState): WatchState =
    WatchState(state.skipped + listOfNotNull(state.following?.bus?.doorNo), null)
