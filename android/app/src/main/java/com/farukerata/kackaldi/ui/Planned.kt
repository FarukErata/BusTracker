package com.farukerata.kackaldi.ui

import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Campaign
import androidx.compose.material.icons.outlined.KeyboardArrowDown
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.layout.SubcomposeLayout
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.farukerata.kackaldi.data.AnnouncementView
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.PlannedTripView
import com.farukerata.kackaldi.data.TimetableResponse
import com.farukerata.kackaldi.i18n.S

private const val NEXT_TRIPS_SHOWN = 5
private val DayTypes = listOf("I", "C", "P")

fun announcementText(a: AnnouncementView): String {
    val stop = a.fromStop
    val time = a.departure
    return if (a.kind == "cancelled" && stop != null && time != null) S.cancelledTrip(titleCase(stop), time) else sentenceCase(a.message)
}

@Composable
fun AnnouncementsCard(items: List<AnnouncementView>, modifier: Modifier = Modifier, showLines: Boolean = true) {
    if (items.isEmpty()) return
    var open by remember { mutableStateOf(false) }
    val colors = Kit.colors
    val turn by animateFloatAsState(if (open) 180f else 0f, label = "announcements")
    Column(
        modifier
            .padding(horizontal = 12.dp, vertical = 4.dp)
            .fillMaxWidth()
            .clip(CardShape)
            .border(1.dp, colors.border, CardShape)
            .background(colors.secondary.copy(alpha = 0.4f))
            .animateContentSize(),
    ) {
        Row(
            Modifier.fillMaxWidth().pressable({ open = !open }, shape = CardShape).padding(horizontal = 10.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Icon(Icons.Outlined.Campaign, null, Modifier.size(18.dp), tint = colors.mutedForeground)
            Text(
                if (open || items.size > 1) S.announcementCount(items.size) else announcementText(items[0]),
                Modifier.weight(1f),
                style = Kit.sm,
                color = colors.foreground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Icon(Icons.Outlined.KeyboardArrowDown, null, Modifier.size(18.dp).rotate(turn), tint = colors.mutedForeground)
        }
        if (open) AnnouncementList(items, showLines)
    }
}

@Composable
private fun BadgeAligned(lines: List<String>, modifier: Modifier = Modifier, content: @Composable (badgeWidth: Dp) -> Unit) {
    SubcomposeLayout(modifier) { constraints ->
        val badgeWidth = subcompose("badges") { lines.distinct().forEach { LineBadge(it) } }
            .maxOfOrNull { it.measure(Constraints()).width } ?: 0
        val rows = subcompose("rows") { content(badgeWidth.toDp()) }.map { it.measure(constraints) }
        layout(constraints.maxWidth, rows.sumOf { it.height }) {
            var y = 0
            rows.forEach { it.place(0, y); y += it.height }
        }
    }
}

@Composable
private fun AnnouncementList(items: List<AnnouncementView>, showLines: Boolean) {
    BadgeAligned(if (showLines) items.map { it.line } else emptyList(), Modifier.padding(start = 10.dp, end = 10.dp, bottom = 10.dp)) { width ->
        AnnouncementRows(items, showLines, width)
    }
}

@Composable
private fun AnnouncementRows(items: List<AnnouncementView>, showLines: Boolean, badgeWidth: Dp) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        items.forEach { a ->
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (showLines) Box(Modifier.width(badgeWidth)) { LineBadge(a.line) }
                Text(
                    if (a.postedAt.isBlank()) announcementText(a) else "${announcementText(a)}  ${a.postedAt}",
                    Modifier.weight(1f),
                    style = Kit.sm,
                    color = Kit.colors.foreground,
                )
            }
        }
    }
}

@Composable
fun PlannedAt(iso: String) {
    Column(horizontalAlignment = Alignment.End) {
        Text("~${clock(iso)}", style = Kit.sm.copy(fontWeight = FontWeight.SemiBold), color = Kit.colors.foreground)
        Text(S.planned, style = Kit.xs.copy(fontSize = 10.sp), color = Kit.colors.mutedForeground)
    }
}

@Composable
fun ArrivalTime(etaSeconds: Int?, planned: PlannedTripView?, receivedAt: Long) {
    val atStop = planned?.atStop
    when {
        etaSeconds != null -> Minutes(etaSeconds, receivedAt)
        atStop != null -> PlannedAt(atStop)
        else -> NotLeft()
    }
}

fun plannedDetail(planned: PlannedTripView?): String? = planned?.let { S.leaves(clock(it.departs)) }

@Composable
fun TripTimes(trips: List<PlannedTripView>, atStop: Boolean) {
    val colors = Kit.colors
    FlowRow(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
        trips.forEach { trip ->
            val stopTime = trip.atStop
            val time = if (atStop && stopTime != null) "~${clock(stopTime)}" else clock(trip.departs)
            Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(
                    time,
                    style = Kit.sm.copy(textDecoration = if (trip.cancelled) TextDecoration.LineThrough else null),
                    color = if (trip.cancelled) colors.mutedForeground else colors.foreground,
                )
                if (trip.cancelled) Text(S.cancelled, style = Kit.xs, color = colors.destructive)
            }
        }
    }
}

data class NextTripsRow(val key: String, val line: String, val label: String?, val trips: List<PlannedTripView>)

private fun firstTripAt(row: NextTripsRow, atStop: Boolean): Long {
    val trip = row.trips.firstOrNull { !it.cancelled } ?: row.trips.first()
    return epochMillis((if (atStop) trip.atStop else null) ?: trip.departs)
}

@Composable
fun NextTrips(rows: List<NextTripsRow>, atStop: Boolean) {
    var all by remember { mutableStateOf(false) }
    val sorted = rows.filter { it.trips.isNotEmpty() }.sortedBy { firstTripAt(it, atStop) }
    if (sorted.isEmpty()) return
    val shown = if (all) sorted else sorted.take(NEXT_TRIPS_SHOWN)
    Column(Modifier.animateContentSize()) {
        SectionTitle(S.nextTrips)
        BadgeAligned(shown.map { it.line }) { width ->
            Column {
                shown.forEach { row ->
                    ItemRow(onClick = null, leading = { Box(Modifier.width(width)) { LineBadge(row.line) } }) {
                        row.label?.let { Text(it, style = Kit.xs, color = Kit.colors.mutedForeground, maxLines = 1, overflow = TextOverflow.Ellipsis) }
                        TripTimes(row.trips, atStop)
                    }
                }
            }
        }
        if (sorted.size > NEXT_TRIPS_SHOWN) {
            KitButton(
                if (all) S.showLess else S.showMoreLines(sorted.size - shown.size),
                { all = !all },
                Modifier.padding(horizontal = 4.dp),
                kind = ButtonKind.Ghost,
            )
        }
        if (atStop) Text(S.estimatedHere, Modifier.padding(horizontal = 12.dp, vertical = 4.dp), style = Kit.xs, color = Kit.colors.mutedForeground)
    }
}

private fun dayName(day: String) = when (day) {
    "I" -> S.weekdays
    "C" -> S.saturday
    else -> S.sunday
}

@Composable
fun TimetableSection(api: KacKaldiApi, line: String, direction: String) {
    var open by remember { mutableStateOf(false) }
    var day by remember { mutableStateOf<String?>(null) }
    var state by remember { mutableStateOf<Load<TimetableResponse>?>(null) }
    LaunchedEffect(open, day) {
        if (!open) return@LaunchedEffect
        val next = load { api.timetable(line, day) }
        if (next is Load.Ready || state !is Load.Ready) state = next
    }
    val turn by animateFloatAsState(if (open) 180f else 0f, label = "timetable")
    Column(Modifier.padding(top = 12.dp).animateContentSize()) {
        Row(
            Modifier.padding(horizontal = 6.dp).fillMaxWidth().pressable({ open = !open }).padding(horizontal = 8.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(if (open) S.hideTimetable else S.showTimetable, Modifier.weight(1f), style = Kit.sm.copy(fontWeight = FontWeight.Medium), color = Kit.colors.mutedForeground)
            Icon(Icons.Outlined.KeyboardArrowDown, null, Modifier.size(18.dp).rotate(turn), tint = Kit.colors.mutedForeground)
        }
        if (open) when (val current = state) {
            null, Load.Loading -> RowsSkeleton(rows = 2)
            is Load.Failed -> Muted(S.timetableUnavailable)
            is Load.Ready -> TimetableBody(current.value, direction) { day = it }
        }
    }
}

@Composable
private fun Muted(text: String) {
    Text(text, Modifier.padding(horizontal = 12.dp, vertical = 4.dp), style = Kit.sm, color = Kit.colors.mutedForeground)
}

@Composable
private fun TimetableBody(data: TimetableResponse, direction: String, onDay: (String) -> Unit) {
    if (!data.available) {
        Muted(S.timetableUnavailable)
        return
    }
    val colors = Kit.colors
    val now = rememberNow(60_000)
    val dir = data.directions.firstOrNull { it.direction == direction }
    val today = data.dayType == data.todayDayType
    Column(Modifier.padding(horizontal = 12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            DayTypes.forEach { d -> Chip(dayName(d), d == data.dayType) { onDay(d) } }
        }
        if (data.holiday) Text(S.holidayTimetable, style = Kit.xs, color = colors.mutedForeground)
        if (dir != null) Text(S.departuresFrom(titleCase(dir.firstStop)), style = Kit.xs.copy(fontWeight = FontWeight.Medium), color = colors.mutedForeground)
        if (dir == null || dir.trips.isEmpty()) {
            Text(S.noTrips, style = Kit.sm, color = colors.mutedForeground)
            return@Column
        }
        FlowRow(verticalArrangement = Arrangement.spacedBy(4.dp)) {
            dir.trips.forEach { trip ->
                val past = today && epochMillis(trip.at) < now
                Text(
                    trip.time,
                    Modifier.width(56.dp),
                    style = Kit.sm.copy(textDecoration = if (trip.cancelled) TextDecoration.LineThrough else null),
                    color = when {
                        trip.cancelled -> colors.destructive
                        past -> colors.mutedForeground.copy(alpha = 0.5f)
                        else -> colors.foreground
                    },
                )
            }
        }
    }
}
