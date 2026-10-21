package com.farukerata.kackaldi.ui

import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.TimeInput
import androidx.compose.material3.TimePickerDefaults
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.farukerata.kackaldi.data.AlertLine
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.LineStop
import com.farukerata.kackaldi.data.SeenBus
import com.farukerata.kackaldi.data.TriggerKind
import com.farukerata.kackaldi.data.WatchTrigger
import java.time.LocalTime

private enum class TriggerMode(val label: String) { Minutes("Minutes"), Stops("Stops"), AtStop("At a stop") }

private const val MAX_MINUTES = 120
private const val MAX_STOPS = 60
private val dayLetters = listOf("Mo", "Tu", "We", "Th", "Fr", "Sa", "Su")

data class AlertDraft(val lines: List<AlertLine>, val trigger: WatchTrigger, val readyAt: LocalTime?, val days: Set<Int>)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AlertSheet(
    api: KacKaldiApi,
    stopCode: String,
    options: List<AlertLine>,
    preselected: List<AlertLine>,
    approaching: List<SeenBus>,
    initial: WatchTrigger,
    onSave: (AlertDraft) -> Unit,
    onDismiss: () -> Unit,
) {
    val colors = Kit.colors
    var picked by remember { mutableStateOf(preselected.ifEmpty { options.take(1) }) }
    var readyAt by remember { mutableStateOf<LocalTime?>(null) }
    var days by remember { mutableStateOf(emptySet<Int>()) }
    var choosingTime by remember { mutableStateOf(false) }
    var mode by remember { mutableStateOf(initialMode(initial)) }
    var minutesText by remember { mutableStateOf(initialText(initial, TriggerKind.Minutes, "5")) }
    var stopsText by remember { mutableStateOf(initialText(initial, TriggerKind.Stops, "2")) }
    var atStop by remember { mutableStateOf<LineStop?>(null) }
    val single = picked.singleOrNull()
    if (mode == TriggerMode.AtStop && single == null) mode = TriggerMode.Minutes
    var earlier by remember { mutableStateOf<Load<EarlierStops>>(Load.Loading) }
    LaunchedEffect(single) {
        atStop = null
        earlier = Load.Loading
        if (single != null) earlier = load { earlierStops(api, single, stopCode) }
    }
    val chosen = chosenTrigger(mode, minutesText, stopsText, atStop, earlier.valueOrNull)
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true), containerColor = colors.background) {
        Column(
            Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).navigationBarsPadding().padding(horizontal = 16.dp).padding(bottom = 16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text("Set an alert", style = Kit.base.copy(fontWeight = FontWeight.SemiBold), color = colors.foreground)
            LinesPicker(options, approaching, picked) { picked = it }
            ReadyPicker(readyAt, onNow = { readyAt = null; days = emptySet() }, onChoose = { choosingTime = true })
            Section("Tell me when the bus is") {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    TriggerMode.entries.filter { it != TriggerMode.AtStop || single != null }.forEach { Chip(it.label, mode == it) { mode = it } }
                }
                when (mode) {
                    TriggerMode.Minutes -> NumberField(minutesText, { minutesText = it }, "min before it reaches my stop")
                    TriggerMode.Stops -> NumberField(stopsText, { stopsText = it }, "stops before my stop")
                    TriggerMode.AtStop -> EarlierStopPicker(earlier, atStop) { atStop = it }
                }
            }
            if (readyAt != null) RepeatPicker(days) { days = it }
            sheetHint(readyAt, chosen, approaching.filter { seen -> picked.any { it.line == seen.line } })?.let { Note(it) }
            KitButton(
                if (readyAt == null) "Start" else "Save alert",
                { chosen?.let { onSave(AlertDraft(picked, it, readyAt, days)) } },
                Modifier.fillMaxWidth(),
                enabled = chosen != null && chosen.value > 0,
                large = true,
            )
        }
    }
    if (choosingTime) TimeDialog(readyAt ?: LocalTime.now().plusMinutes(30).withSecond(0).withNano(0), { readyAt = it; choosingTime = false }) { choosingTime = false }
}

private fun initialMode(initial: WatchTrigger): TriggerMode =
    if (initial.kind == TriggerKind.Stops) TriggerMode.Stops else TriggerMode.Minutes

private fun initialText(initial: WatchTrigger, kind: TriggerKind, fallback: String): String =
    if (initial.kind == kind) initial.value.toString() else fallback

private fun chosenTrigger(mode: TriggerMode, minutesText: String, stopsText: String, atStop: LineStop?, earlier: EarlierStops?): WatchTrigger? = when (mode) {
    TriggerMode.Minutes -> minutesText.toIntOrNull()?.takeIf { it in 1..MAX_MINUTES }?.let { WatchTrigger(TriggerKind.Minutes, it) }
    TriggerMode.Stops -> stopsText.toIntOrNull()?.takeIf { it in 1..MAX_STOPS }?.let { WatchTrigger(TriggerKind.Stops, it) }
    TriggerMode.AtStop -> if (atStop == null || earlier == null) null else WatchTrigger(TriggerKind.Stops, earlier.mySeq - atStop.seq, atStopName = atStop.name)
}

private fun sheetHint(readyAt: LocalTime?, chosen: WatchTrigger?, approaching: List<SeenBus>): String? = when {
    readyAt != null -> "It starts watching 15 min before you’re ready and tells you about the first bus you can catch."
    chosen != null -> insideText(chosen, approaching)
    else -> null
}

@Composable
private fun LinesPicker(options: List<AlertLine>, approaching: List<SeenBus>, picked: List<AlertLine>, onPicked: (List<AlertLine>) -> Unit) {
    var showAll by remember { mutableStateOf(false) }
    val sameLine = options.groupBy { it.line }
    val onTheWay = approaching.map { it.line }.toSet()
    val ordered = options.sortedByDescending { it.line in onTheWay }
    val visible = if (showAll) ordered else ordered.filter { it.line in onTheWay || it in picked }
    Section("Lines") {
        FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            visible.forEach { option ->
                val label = if ((sameLine[option.line]?.size ?: 0) > 1) "${option.line} → ${titleCase(option.directionName)}" else option.line
                Chip(label, option in picked) { onPicked(toggled(picked, option)) }
            }
            if (visible.size < ordered.size) Chip("+${ordered.size - visible.size} more", false) { showAll = true }
        }
    }
}

private fun toggled(picked: List<AlertLine>, option: AlertLine): List<AlertLine> =
    if (option in picked) (picked - option).ifEmpty { picked } else picked + option

@Composable
private fun ReadyPicker(readyAt: LocalTime?, onNow: () -> Unit, onChoose: () -> Unit) {
    Section("I’m ready") {
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Chip("Now", readyAt == null, onNow)
            Chip(readyAt?.let { "At ${clockText(it)}" } ?: "At a time", readyAt != null, onChoose)
        }
    }
}

@Composable
private fun RepeatPicker(days: Set<Int>, onDays: (Set<Int>) -> Unit) {
    Section("Repeat") {
        Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            dayLetters.forEachIndexed { index, letter ->
                val day = index + 1
                Chip(letter, day in days) { onDays(if (day in days) days - day else days + day) }
            }
        }
        Note(if (days.isEmpty()) "No days picked: it runs once, then goes away." else "Runs every ${dayList(days)}.")
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TimeDialog(start: LocalTime, onPick: (LocalTime) -> Unit, onDismiss: () -> Unit) {
    val state = rememberTimePickerState(start.hour, start.minute, is24Hour = true)
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = Kit.colors.background,
        confirmButton = { KitButton("Set", { onPick(LocalTime.of(state.hour, state.minute)) }) },
        dismissButton = { KitButton("Cancel", onDismiss, kind = ButtonKind.Ghost) },
        title = { Text("I’m ready at", style = Kit.base.copy(fontWeight = FontWeight.SemiBold), color = Kit.colors.foreground) },
        text = {
            val colors = Kit.colors
            TimeInput(
                state,
                colors = TimePickerDefaults.colors(
                    timeSelectorSelectedContainerColor = colors.secondary,
                    timeSelectorSelectedContentColor = colors.foreground,
                    timeSelectorUnselectedContainerColor = colors.secondary,
                    timeSelectorUnselectedContentColor = colors.foreground,
                ),
            )
        },
    )
}

@Composable
private fun Section(title: String, content: @Composable () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(title, style = Kit.sm.copy(fontWeight = FontWeight.Medium), color = Kit.colors.mutedForeground)
        content()
    }
}

data class EarlierStops(val mySeq: Int, val stops: List<LineStop>)

private suspend fun earlierStops(api: KacKaldiApi, line: AlertLine, stopCode: String): EarlierStops {
    val stops = api.lineStops(line.line).directions.firstOrNull { it.direction == line.direction }?.stops.orEmpty().sortedBy { it.seq }
    val mine = stops.firstOrNull { it.code == stopCode } ?: return EarlierStops(0, emptyList())
    return EarlierStops(mine.seq, stops.filter { it.seq < mine.seq }.reversed())
}

private fun insideText(trigger: WatchTrigger, approaching: List<SeenBus>): String? {
    val inside = approaching.filter { trigger.reached(it.bus) }
    if (inside.isEmpty()) return null
    val which = if (inside.size == 1) "The next ${inside.first().line} is" else "${inside.size} buses are"
    return "$which already closer than this, so you’ll be told about the one after."
}

fun triggerSummary(trigger: WatchTrigger): String = when {
    trigger.atStopName != null -> "when a bus reaches ${titleCase(trigger.atStopName)}"
    trigger.kind == TriggerKind.Minutes -> "${trigger.value} min before"
    trigger.value == 1 -> "1 stop before"
    else -> "${trigger.value} stops before"
}

fun clockText(time: LocalTime): String = "%02d:%02d".format(time.hour, time.minute)

fun dayList(days: Set<Int>): String = when (days) {
    setOf(1, 2, 3, 4, 5) -> "weekday"
    setOf(6, 7) -> "weekend day"
    (1..7).toSet() -> "day"
    else -> days.sorted().joinToString(", ") { dayLetters[it - 1] }
}

@Composable
private fun Note(text: String) {
    Text(text, style = Kit.sm, color = Kit.colors.mutedForeground)
}

@Composable
private fun NumberField(value: String, onChange: (String) -> Unit, suffix: String) {
    val colors = Kit.colors
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        BasicTextField(
            value = value,
            onValueChange = { text -> onChange(text.filter(Char::isDigit).take(3)) },
            singleLine = true,
            textStyle = Kit.base.copy(color = colors.foreground, fontWeight = FontWeight.SemiBold),
            cursorBrush = SolidColor(colors.foreground),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            decorationBox = { field ->
                Box(Modifier.width(72.dp).height(40.dp).border(1.dp, colors.input, FieldShape).padding(horizontal = 12.dp), contentAlignment = Alignment.CenterStart) {
                    field()
                }
            },
        )
        Text(suffix, style = Kit.sm, color = colors.mutedForeground)
    }
}

@Composable
private fun EarlierStopPicker(earlier: Load<EarlierStops>, picked: LineStop?, onPick: (LineStop) -> Unit) {
    when (earlier) {
        Load.Loading -> RowsSkeleton(rows = 3)
        is Load.Failed -> Note(earlier.message)
        is Load.Ready -> {
            if (earlier.value.stops.isEmpty()) {
                Note("This is the first stop of the line.")
                return
            }
            LazyColumn(Modifier.fillMaxWidth().heightIn(max = 240.dp).border(1.dp, Kit.colors.border, CardShape)) {
                items(earlier.value.stops, key = { "${it.seq}-${it.code}" }) { stop ->
                    val away = earlier.value.mySeq - stop.seq
                    ItemRow(
                        onClick = { onPick(stop) },
                        selected = picked == stop,
                        trailing = if (picked == stop) ({ Icon(Icons.Outlined.Check, null, tint = Kit.colors.foreground) }) else null,
                    ) {
                        ItemTitle(titleCase(stop.name))
                        ItemDescription(if (away == 1) "1 stop before yours" else "$away stops before yours")
                    }
                }
            }
        }
    }
}
