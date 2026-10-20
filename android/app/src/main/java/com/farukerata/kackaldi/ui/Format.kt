package com.farukerata.kackaldi.ui

import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.input.OffsetMapping
import androidx.compose.ui.text.input.TransformedText
import androidx.compose.ui.text.input.VisualTransformation
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

fun linkCodeChars(text: String): String = text.uppercase(Locale.ROOT).filter { it.isLetterOrDigit() }.take(6)

fun linkCodeInput(text: String): String {
    val compact = linkCodeChars(text)
    return if (compact.length > 3) "${compact.take(3)}-${compact.drop(3)}" else compact
}

fun isCompleteLinkCode(code: String): Boolean = Regex("^[A-Z0-9]{3}-[A-Z0-9]{3}$").matches(linkCodeInput(code))

object LinkCodeTransformation : VisualTransformation {
    override fun filter(text: AnnotatedString): TransformedText {
        val raw = text.text
        val mapping = object : OffsetMapping {
            override fun originalToTransformed(offset: Int) = if (offset > 3) offset + 1 else offset
            override fun transformedToOriginal(offset: Int) = (if (offset > 3) offset - 1 else offset).coerceAtMost(raw.length)
        }
        return TransformedText(AnnotatedString(linkCodeInput(raw)), mapping)
    }
}

enum class ReportAge { Fresh, Old, Stale }

fun reportAge(seconds: Long): ReportAge = when {
    seconds >= STALE_REPORT_SECONDS -> ReportAge.Stale
    seconds >= OLD_REPORT_SECONDS -> ReportAge.Old
    else -> ReportAge.Fresh
}

fun seenAgo(seconds: Long): String = "seen ${ago(seconds)}"

private const val OLD_REPORT_SECONDS = 90L
private const val STALE_REPORT_SECONDS = 180L

fun countdown(seconds: Long): String {
    val s = seconds.coerceAtLeast(0)
    return "${s / 60}:${(s % 60).toString().padStart(2, '0')}"
}

fun distance(meters: Double): String =
    if (meters < 1000) "${(Math.round(meters / 10) * 10)} m" else String.format(java.util.Locale.ROOT, "%.1f km", meters / 1000)
