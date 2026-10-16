package com.farukerata.kackaldi.ui

import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime
import java.util.Locale
import kotlin.math.roundToInt

private val turkish: Locale = Locale.forLanguageTag("tr")
private val wordStart = Regex("""(^|[\s(\-/.])(\p{L})""")

fun titleCase(text: String?): String =
    text.orEmpty().trim().lowercase(turkish).replace(wordStart) { it.groupValues[1] + it.groupValues[2].uppercase(turkish) }

fun minutes(etaSeconds: Int): String = if (etaSeconds < 60) "<1" else (etaSeconds / 60.0).roundToInt().toString()

fun stopsAway(count: Int): String = when (count) {
    0 -> "At your stop"
    1 -> "1 stop away"
    else -> "$count stops away"
}

fun busWhere(notDeparted: Boolean, stops: Int): String = if (notDeparted) "At the first stop" else stopsAway(stops)

fun directionLabel(from: String, to: String): String = "${titleCase(from)} → ${titleCase(to)}"

fun ago(seconds: Long): String = when {
    seconds < 10 -> "just now"
    seconds < 60 -> "$seconds s ago"
    seconds < 3600 -> "${seconds / 60} min ago"
    else -> "${seconds / 3600} h ago"
}

fun secondsSince(iso: String, now: Instant = Instant.now()): Long =
    runCatching { Duration.between(OffsetDateTime.parse(iso).toInstant(), now).seconds.coerceAtLeast(0) }.getOrDefault(0)

fun linkCodeInput(text: String): String {
    val compact = text.uppercase(Locale.ROOT).filter { it.isLetterOrDigit() }.take(6)
    return if (compact.length > 3) "${compact.take(3)}-${compact.drop(3)}" else compact
}

fun isCompleteLinkCode(code: String): Boolean = Regex("^[A-Z0-9]{3}-[A-Z0-9]{3}$").matches(code)
