package com.farukerata.kackaldi.data

import kotlinx.serialization.Serializable

@Serializable
enum class TriggerKind { Minutes, Stops }

@Serializable
data class WatchTrigger(val kind: TriggerKind, val value: Int, val atStopName: String? = null) {
    fun reached(bus: ArrivalView): Boolean = when (kind) {
        TriggerKind.Minutes -> !bus.notDeparted && bus.etaSeconds != null && bus.etaSeconds <= value * 60
        TriggerKind.Stops -> bus.stopsAway <= value
    }
}

@Serializable
data class Watch(
    val line: String,
    val direction: String,
    val directionName: String,
    val stopCode: String,
    val stopName: String,
    val trigger: WatchTrigger,
    val startedAt: Long,
)

data class WatchState(val skipped: Set<String>, val alerted: String?)

data class WatchStep(val state: WatchState, val shown: ArrivalView?, val fire: ArrivalView?, val done: Boolean)

fun alreadyInside(trigger: WatchTrigger, approaching: List<ArrivalView>): Set<String> =
    approaching.filter(trigger::reached).map { it.doorNo }.toSet()

fun nextStep(trigger: WatchTrigger, approaching: List<ArrivalView>, previous: WatchState?): WatchStep {
    val state = previous ?: WatchState(alreadyInside(trigger, approaching), null)
    val alerted = state.alerted
    if (alerted != null) {
        val followed = approaching.firstOrNull { it.doorNo == alerted }
        return WatchStep(state, followed, null, done = followed == null)
    }
    val present = approaching.map { it.doorNo }.toSet()
    val skipped = state.skipped.intersect(present)
    val waiting = approaching.filter { it.doorNo !in skipped }.sortedBy { it.stopsAway }
    val fire = waiting.firstOrNull(trigger::reached)
    val next = WatchState(skipped, fire?.doorNo)
    return WatchStep(next, fire ?: waiting.firstOrNull(), fire, done = false)
}
