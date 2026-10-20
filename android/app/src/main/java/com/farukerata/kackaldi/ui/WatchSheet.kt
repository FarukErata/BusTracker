package com.farukerata.kackaldi.ui

import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
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
import com.farukerata.kackaldi.data.ArrivalView
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.LineStop
import com.farukerata.kackaldi.data.TriggerKind
import com.farukerata.kackaldi.data.WatchTrigger
import com.farukerata.kackaldi.data.alreadyInside

private enum class WatchMode(val label: String) { Minutes("Minutes"), Stops("Stops"), AtStop("At a stop") }

private const val MAX_MINUTES = 120
private const val MAX_STOPS = 60

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WatchSheet(
    api: KacKaldiApi,
    line: String,
    direction: String,
    directionName: String,
    stopCode: String,
    approaching: List<ArrivalView>,
    initial: WatchTrigger,
    onStart: (WatchTrigger) -> Unit,
    onDismiss: () -> Unit,
) {
    val colors = Kit.colors
    var mode by remember { mutableStateOf(if (initial.kind == TriggerKind.Stops) WatchMode.Stops else WatchMode.Minutes) }
    var minutesText by remember { mutableStateOf(if (initial.kind == TriggerKind.Minutes) initial.value.toString() else "5") }
    var stopsText by remember { mutableStateOf(if (initial.kind == TriggerKind.Stops) initial.value.toString() else "2") }
    var picked by remember { mutableStateOf<LineStop?>(null) }
    var earlier by remember { mutableStateOf<Load<EarlierStops>>(Load.Loading) }
    LaunchedEffect(line, direction, stopCode) {
        earlier = load { earlierStops(api, line, direction, stopCode) }
    }
    val trigger = when (mode) {
        WatchMode.Minutes -> minutesText.toIntOrNull()?.takeIf { it in 1..MAX_MINUTES }?.let { WatchTrigger(TriggerKind.Minutes, it) }
        WatchMode.Stops -> stopsText.toIntOrNull()?.takeIf { it in 1..MAX_STOPS }?.let { WatchTrigger(TriggerKind.Stops, it) }
        WatchMode.AtStop -> picked?.let { stop ->
            earlier.valueOrNull?.let { WatchTrigger(TriggerKind.Stops, it.mySeq - stop.seq, atStopName = stop.name) }
        }
    }
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true), containerColor = colors.background) {
        Column(Modifier.fillMaxWidth().navigationBarsPadding().padding(horizontal = 16.dp).padding(bottom = 16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text("Notify me · $line", style = Kit.base.copy(fontWeight = FontWeight.SemiBold), color = colors.foreground)
                Text("Any $line to ${titleCase(directionName)}, whichever comes first", style = Kit.sm, color = colors.mutedForeground)
            }
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                WatchMode.entries.forEach { Chip(it.label, mode == it) { mode = it } }
            }
            when (mode) {
                WatchMode.Minutes -> NumberField(minutesText, { minutesText = it }, "min before it reaches my stop")
                WatchMode.Stops -> NumberField(stopsText, { stopsText = it }, "stops before my stop")
                WatchMode.AtStop -> EarlierStopPicker(earlier, picked) { picked = it }
            }
            trigger?.let { insideText(line, it, approaching) }?.let { Note(it) }
            KitButton(
                "Start",
                { trigger?.let(onStart) },
                Modifier.fillMaxWidth(),
                enabled = trigger != null && trigger.value > 0,
                large = true,
            )
        }
    }
}

data class EarlierStops(val mySeq: Int, val stops: List<LineStop>)

private suspend fun earlierStops(api: KacKaldiApi, line: String, direction: String, stopCode: String): EarlierStops {
    val stops = api.lineStops(line).directions.firstOrNull { it.direction == direction }?.stops.orEmpty().sortedBy { it.seq }
    val mine = stops.firstOrNull { it.code == stopCode } ?: return EarlierStops(0, emptyList())
    return EarlierStops(mine.seq, stops.filter { it.seq < mine.seq }.reversed())
}

private fun insideText(line: String, trigger: WatchTrigger, approaching: List<ArrivalView>): String? {
    val inside = alreadyInside(trigger, approaching)
    if (inside.isEmpty()) return null
    val which = if (inside.size == 1) "The next $line is" else "${inside.size} buses are"
    return "$which already closer than this, so you’ll be told about the one after."
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
            Text("Tell me when a bus reaches", style = Kit.sm, color = Kit.colors.mutedForeground)
            LazyColumn(Modifier.fillMaxWidth().heightIn(max = 280.dp).border(1.dp, Kit.colors.border, CardShape)) {
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
