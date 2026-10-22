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
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.asPaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.KeyboardArrowDown
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material.icons.outlined.StarOutline
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.viewModelScope
import com.farukerata.kackaldi.data.AnnouncementView
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.RailDepartureView
import com.farukerata.kackaldi.data.RailDirectionView
import com.farukerata.kackaldi.data.RailStationResponse
import com.farukerata.kackaldi.data.RailStationView
import com.farukerata.kackaldi.i18n.S
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

private const val RAIL_REFRESH_MILLIS = 60_000L

fun railColor(hex: String?): Color =
    hex?.let { runCatching { Color(android.graphics.Color.parseColor(it)) }.getOrNull() } ?: Color.Gray

@Composable
fun RailBadge(line: String, color: String?, modifier: Modifier = Modifier) {
    Box(
        modifier.height(20.dp).clip(RoundedCornerShape(6.dp)).background(railColor(color)).padding(horizontal = 6.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(line, style = Kit.xs.copy(fontWeight = FontWeight.SemiBold), color = Color.White, maxLines = 1)
    }
}

fun minutesUntil(iso: String, now: Long): Long = ((epochMillis(iso) - now) / 60_000.0).let { Math.round(it).coerceAtLeast(0) }

class RailStationViewModel(private val api: KacKaldiApi, val id: Int) : ViewModel() {
    var station by mutableStateOf<Load<RailStationResponse>>(Load.Loading)
        private set
    var favoriteId by mutableStateOf<Long?>(null)
        private set

    suspend fun refresh() {
        val next = load { api.railStation(id) }
        if (next is Load.Ready || station !is Load.Ready) station = next
    }

    fun loadFavorite() {
        viewModelScope.launch {
            val favorites = load { api.favorites() }.valueOrNull ?: return@launch
            favoriteId = favorites.stations.firstOrNull { it.favorite.stationId == id }?.favorite?.id
        }
    }

    fun toggleFavorite() {
        viewModelScope.launch {
            val current = favoriteId
            load { if (current == null) api.addFavoriteStation(id) else api.removeFavoriteStation(current) }
            loadFavorite()
        }
    }
}

@Composable
fun RailStationScreen(viewModel: RailStationViewModel, rail: List<RailStationView>, onBack: () -> Unit, onRail: (Int) -> Unit) {
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(viewModel.id) {
        viewModel.loadFavorite()
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                viewModel.refresh()
                delay(RAIL_REFRESH_MILLIS)
            }
        }
    }
    val colors = Kit.colors
    val station = viewModel.station.valueOrNull?.station
    Column(
        Modifier
            .fillMaxSize()
            .background(colors.background)
            .windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal)),
    ) {
        Header(
            onBack = onBack,
            actions = {
                val saved = viewModel.favoriteId != null
                GhostIcon(
                    if (saved) Icons.Rounded.Star else Icons.Outlined.StarOutline,
                    if (saved) S.unsaveStation else S.saveStation,
                    viewModel::toggleFavorite,
                    tint = if (saved) colors.star else colors.foreground,
                )
            },
        ) {
            Row(Modifier.padding(horizontal = 4.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (station != null) {
                    RailBadge(station.line, station.color)
                    Text(station.name, style = Kit.base.copy(fontWeight = FontWeight.SemiBold, letterSpacing = Kit.tight), color = colors.foreground, maxLines = 1, overflow = TextOverflow.Ellipsis)
                } else {
                    Box(Modifier.fillMaxWidth(0.5f).height(18.dp).background(colors.secondary, RowShape))
                }
            }
        }
        StationMap(station, rail, onRail)
        when (val state = viewModel.station) {
            Load.Loading -> RowsSkeleton(rows = 2)
            is Load.Failed -> ErrorState(state.message, { })
            is Load.Ready -> StationBody(state.value)
        }
    }
}

@Composable
private fun StationMap(station: RailStationView?, rail: List<RailStationView>, onRail: (Int) -> Unit) {
    val colors = Kit.colors
    Box(
        Modifier
            .padding(start = 12.dp, end = 12.dp, top = 8.dp)
            .fillMaxWidth()
            .height(160.dp)
            .clip(CardShape)
            .background(colors.secondary)
            .border(1.dp, colors.border, CardShape),
    ) {
        val lat = station?.lat ?: return@Box
        val lng = station.lng ?: return@Box
        TransitMap(
            MapScene(
                frame = MapFrame.Center("rail-${station.id}", lat, lng, 16.0),
                pinned = MapStop("rail-${station.id}", lat, lng),
                pinColor = station.color,
                rail = railMarkers(rail, except = station.id),
            ),
            Modifier.fillMaxSize(),
            onRail = onRail,
        )
    }
}

fun railMarkers(rail: List<RailStationView>, except: Int? = null): List<MapRail> =
    rail.mapNotNull { s -> if (s.id == except || s.lat == null || s.lng == null) null else MapRail(s.id, s.lat, s.lng, s.color) }

@Composable
private fun StationBody(data: RailStationResponse) {
    val notices = data.notices.map { AnnouncementView(line = it.line, kind = "notice", message = it.text) }
    val nextDate = data.directions.firstOrNull { d -> d.next.any { it.nextDay } }?.nextDate
    LazyColumn(Modifier.fillMaxSize(), contentPadding = WindowInsets.navigationBars.asPaddingValues()) {
        if (notices.isNotEmpty()) item { AnnouncementsCard(notices, showLines = false) }
        items(data.directions, key = { "direction-${it.id}" }) { DirectionRow(it) }
        if (nextDate != null) item {
            Row(Modifier.padding(horizontal = 12.dp, vertical = 4.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                NextDayDot()
                Text(S.nextDayTrips(dayWithDate(nextDate)), style = Kit.xs, color = Kit.colors.mutedForeground)
            }
        }
        item { Text(S.railPlanned, Modifier.padding(horizontal = 12.dp, vertical = 4.dp), style = Kit.xs, color = Kit.colors.mutedForeground) }
        item { StationTimetable(data.directions) }
    }
}

@Composable
private fun DirectionRow(direction: RailDirectionView) {
    val colors = Kit.colors
    Column(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(S.railTowards(direction.to), style = Kit.sm.copy(fontWeight = FontWeight.Medium), color = colors.foreground)
            Text(S.railFrom(direction.from), style = Kit.xs, color = colors.mutedForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        if (direction.next.isEmpty()) {
            Text(S.railNoTrains, style = Kit.sm, color = colors.mutedForeground)
        } else {
            Row(horizontalArrangement = Arrangement.spacedBy(20.dp)) { direction.next.forEach { Departure(it) } }
        }
    }
}

@Composable
private fun Departure(departure: RailDepartureView) {
    val now = rememberNow(15_000)
    val colors = Kit.colors
    val minutes = minutesUntil(departure.at, now)
    Column {
        Box {
            Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(2.dp)) {
                if (minutes == 0L) {
                    Text(S.railNow, style = Kit.lg.copy(fontWeight = FontWeight.SemiBold, lineHeight = Kit.lg.fontSize), color = colors.foreground)
                } else {
                    Text("$minutes", style = Kit.lg.copy(fontWeight = FontWeight.SemiBold, lineHeight = Kit.lg.fontSize), color = colors.foreground)
                    Text(S.min, style = Kit.xs, color = colors.mutedForeground)
                }
            }
            if (departure.nextDay) NextDayDot(Modifier.align(Alignment.TopStart).offset(x = (-6).dp, y = (-2).dp))
        }
        Text(clock(departure.at), style = Kit.xs, color = colors.mutedForeground)
    }
}

@Composable
private fun StationTimetable(directions: List<RailDirectionView>) {
    if (directions.isEmpty()) return
    var open by remember { mutableStateOf(false) }
    var picked by remember { mutableIntStateOf(0) }
    val direction = directions[picked.coerceAtMost(directions.lastIndex)]
    val turn by animateFloatAsState(if (open) 180f else 0f, label = "rail-timetable")
    Column(Modifier.padding(top = 12.dp).animateContentSize(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(
            Modifier
                .padding(horizontal = 12.dp)
                .fillMaxWidth()
                .border(1.dp, Kit.colors.border, CardShape)
                .pressable({ open = !open }, shape = CardShape)
                .padding(horizontal = 12.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Icon(Icons.Outlined.Schedule, null, Modifier.size(18.dp), tint = Kit.colors.foreground)
            Text(if (open) S.hideTimetable else S.showTimetable, Modifier.weight(1f), style = Kit.sm.copy(fontWeight = FontWeight.Medium), color = Kit.colors.foreground)
            Icon(Icons.Outlined.KeyboardArrowDown, null, Modifier.size(18.dp).rotate(turn), tint = Kit.colors.mutedForeground)
        }
        if (open) {
            Column(Modifier.padding(horizontal = 12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                if (directions.size > 1) {
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        directions.forEachIndexed { i, d -> Chip(S.railTowards(d.to), i == picked) { picked = i } }
                    }
                }
                HourGrid(direction.trips.map { GridTrip(it.time, it.at) }, today = true)
            }
        }
    }
}
