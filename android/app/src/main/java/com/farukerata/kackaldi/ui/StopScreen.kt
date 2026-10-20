package com.farukerata.kackaldi.ui

import androidx.compose.animation.animateContentSize
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.asPaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowForward
import androidx.compose.material.icons.outlined.StarOutline
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.viewModelScope
import com.farukerata.kackaldi.data.BusPathResponse
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.LineStatusView
import com.farukerata.kackaldi.data.PathStop
import com.farukerata.kackaldi.data.StopArrivalView
import com.farukerata.kackaldi.data.StopArrivalsView
import com.farukerata.kackaldi.data.StopFilters
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

private const val REFRESH_MILLIS = 30_000L

data class TrackedSelection(val line: String, val direction: String, val doorNo: String)

class StopViewModel(private val api: KacKaldiApi, private val filters: StopFilters, val code: String) : ViewModel() {
    val shownLines: StateFlow<Set<String>> = filters.lines(code).stateIn(viewModelScope, SharingStarted.Eagerly, emptySet())

    var arrivals by mutableStateOf<Load<StopArrivalsView>>(Load.Loading)
        private set
    var refreshing by mutableStateOf(false)
        private set
    var receivedAt by mutableLongStateOf(0L)
        private set
    var favoriteId by mutableStateOf<Long?>(null)
        private set
    var selection by mutableStateOf<TrackedSelection?>(null)
        private set
    var tracking by mutableStateOf<BusPathResponse?>(null)
        private set

    suspend fun refresh() {
        val lines = filters.lines(code).first()
        val next = load { api.stopArrivals(code, lines) }
        if (next is Load.Ready) receivedAt = System.currentTimeMillis()
        if (next is Load.Ready || arrivals !is Load.Ready) arrivals = next
        refreshTracking()
    }

    fun pullToRefresh() {
        viewModelScope.launch {
            refreshing = true
            refresh()
            refreshing = false
        }
    }

    fun toggle(arrival: StopArrivalView) {
        val picked = TrackedSelection(arrival.line, arrival.direction, arrival.bus.doorNo)
        selection = if (selection == picked) null else picked
        tracking = null
        viewModelScope.launch { refreshTracking() }
    }

    private suspend fun refreshTracking() {
        val current = selection ?: return
        val path = load { api.busPath(current.line, current.doorNo, code) }.valueOrNull
        if (selection == current) tracking = path?.takeIf { it.bus.doorNo == current.doorNo }
    }

    fun toggleLine(line: String, all: List<String>) {
        val next = shownLines.value.let { if (line in it) it - line else it + line }
        viewModelScope.launch {
            filters.save(code, if (next.containsAll(all)) emptySet() else next)
            refresh()
        }
    }

    fun showAllLines() {
        viewModelScope.launch {
            filters.save(code, emptySet())
            refresh()
        }
    }

    fun loadFavorite() {
        viewModelScope.launch {
            val favorites = load { api.favorites() }.valueOrNull ?: return@launch
            favoriteId = favorites.stops.firstOrNull { it.favorite.stopCode == code }?.favorite?.id
        }
    }

    fun toggleFavorite() {
        viewModelScope.launch {
            val id = favoriteId
            load { if (id == null) api.addFavoriteStop(code) else api.removeFavoriteStop(id) }
            loadFavorite()
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun StopScreen(viewModel: StopViewModel, onBack: () -> Unit, onLine: (String, String?, String?) -> Unit) {
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(viewModel.code) {
        viewModel.loadFavorite()
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                viewModel.refresh()
                delay(REFRESH_MILLIS)
            }
        }
    }
    val stop = viewModel.arrivals.valueOrNull?.stop
    val shown by viewModel.shownLines.collectAsStateWithLifecycle()
    val colors = Kit.colors

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
                    if (saved) "Remove from your stops" else "Save to your stops",
                    viewModel::toggleFavorite,
                    tint = if (saved) colors.star else colors.foreground,
                )
            },
        ) {
            Column(Modifier.padding(horizontal = 4.dp)) {
                if (stop != null) {
                    StopName(stop.name, stop.district, Kit.base.copy(fontWeight = FontWeight.SemiBold, letterSpacing = Kit.tight))
                    Towards(stop.towards, Kit.xs)
                } else {
                    Box(Modifier.fillMaxWidth(0.5f).height(18.dp).background(colors.secondary, RowShape))
                }
            }
        }
        StopMapCard(viewModel, stop?.lat, stop?.lng)
        PullToRefreshBox(viewModel.refreshing, viewModel::pullToRefresh, Modifier.fillMaxSize()) {
            when (val state = viewModel.arrivals) {
                Load.Loading -> RowsSkeleton()
                is Load.Failed -> ErrorState(state.message, viewModel::pullToRefresh)
                is Load.Ready -> ArrivalsList(state.value, shown, viewModel, onLine)
            }
        }
    }
}

@Composable
private fun StopMapCard(viewModel: StopViewModel, lat: Double?, lng: Double?) {
    val colors = Kit.colors
    val tracking = viewModel.tracking
    val selected = viewModel.selection != null
    Box(
        Modifier
            .padding(start = 12.dp, end = 12.dp, top = 8.dp)
            .fillMaxWidth()
            .animateContentSize()
            .height(if (selected) 256.dp else 160.dp)
            .clip(CardShape)
            .background(colors.secondary)
            .border(1.dp, colors.border, CardShape),
    ) {
        if (lat == null || lng == null) return@Box
        val stop = MapStop(viewModel.code, lat, lng)
        val scene = if (tracking != null) {
            MapScene(
                frame = MapFrame.Fit("track-${tracking.bus.doorNo}", tracking.path + listOf(listOf(lat, lng))),
                route = tracking.path,
                stops = tracking.remainingStops.dropLast(1).map { MapStop(it.code, it.lat, it.lng) },
                pinned = stop,
                buses = listOf(MapBus(tracking.bus.doorNo, tracking.bus.lat, tracking.bus.lng, heavyTraffic = tracking.bus.heavyTraffic)),
                selectedBus = tracking.bus.doorNo,
            )
        } else {
            MapScene(frame = MapFrame.Center("stop-${viewModel.code}", lat, lng, 16.5), pinned = stop)
        }
        TransitMap(scene, Modifier.fillMaxSize())
    }
}

@Composable
private fun ArrivalsList(data: StopArrivalsView, shown: Set<String>, viewModel: StopViewModel, onLine: (String, String?, String?) -> Unit) {
    val allLines = data.lines.map { it.line }.distinct()
    val visible = shown.intersect(allLines.toSet()).ifEmpty { allLines.toSet() }
    val lines = data.lines.filter { it.line in visible }
    val arrivals = data.arrivals.filter { it.line in visible }
    val bothWays = lines.groupBy { it.line }.filterValues { it.size > 1 }.keys
    val waiting = lines.filter { it.error != null }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = WindowInsets.navigationBars.asPaddingValues()) {
        if (allLines.size > 1) item {
            LineFilter(allLines, visible.takeIf { it.size < allLines.size }.orEmpty(), { viewModel.toggleLine(it, allLines) }, viewModel::showAllLines)
        }
        if (arrivals.isEmpty()) item {
            EmptyState("No bus on the way", "Lines here: ${visible.joinToString(", ")}.")
        }
        items(arrivals, key = { "${it.line}-${it.bus.doorNo}" }) { arrival ->
            val fetchedAt = lines.firstOrNull { it.line == arrival.line && it.direction == arrival.direction }?.data?.fetchedAt
            val age = arrival.bus.ageSeconds + (fetchedAt?.let(::secondsSince) ?: 0L)
            val isSelected = viewModel.selection?.let { it.line == arrival.line && it.direction == arrival.direction && it.doorNo == arrival.bus.doorNo } == true
            Column(Modifier.animateContentSize().background(if (isSelected) Kit.colors.secondary.copy(alpha = 0.6f) else androidx.compose.ui.graphics.Color.Transparent)) {
                ArrivalRow(arrival, data.stop.name, arrival.line in bothWays, age, viewModel.receivedAt, false) { viewModel.toggle(arrival) }
                if (isSelected) TrackingDetails(arrival, viewModel.tracking?.remainingStops) { onLine(arrival.line, arrival.direction, viewModel.code) }
            }
        }
        if (waiting.isNotEmpty()) item {
            Text(waitingText(waiting), Modifier.padding(horizontal = 12.dp, vertical = 8.dp), style = Kit.xs, color = Kit.colors.mutedForeground)
        }
        item { UpdatedAgo(oldestFetch(lines)) }
    }
}

@Composable
private fun LineFilter(lines: List<String>, picked: Set<String>, onToggle: (String) -> Unit, onAll: () -> Unit) {
    LazyRow(
        contentPadding = PaddingValues(horizontal = 12.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        item { Chip("All", picked.isEmpty(), onAll) }
        items(lines, key = { it }) { line -> Chip(line, line in picked) { onToggle(line) } }
    }
}

@Composable
private fun ArrivalRow(
    arrival: StopArrivalView, stopName: String, bothWays: Boolean, ageSeconds: Long, receivedAt: Long, selected: Boolean, onClick: () -> Unit,
) {
    val age = reportAge(ageSeconds)
    val faded = Modifier.alpha(if (age == ReportAge.Fresh) 1f else 0.6f)
    val bus = arrival.bus
    val endsHere = arrival.directionName.equals(stopName, ignoreCase = true)
    val detail = buildList {
        if (endsHere) add("Ends here")
        if (bothWays && !endsHere) add("To ${titleCase(arrival.directionName)}")
        if (!bus.notDeparted) bus.lastStopName?.let { add("now at ${titleCase(it)}") }
        if (age != ReportAge.Fresh) add(seenAgo(ageSeconds))
    }.joinToString(" · ").replaceFirstChar { it.uppercase() }
    ItemRow(
        onClick = onClick,
        selected = selected,
        leading = { Box(faded) { LineBadge(arrival.line) } },
        trailing = {
            Box(faded) {
                val eta = bus.etaSeconds
                if (eta != null) Minutes(eta, receivedAt) else NotLeft()
            }
        },
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ItemTitle(busWhere(bus.notDeparted, bus.stopsAway), Modifier.weight(1f, fill = false))
            if (bus.heavyTraffic) TrafficBadge()
        }
        if (detail.isNotEmpty()) ItemDescription(detail)
    }
}

@Composable
private fun TrackingDetails(arrival: StopArrivalView, remaining: List<PathStop>?, onSeeLine: () -> Unit) {
    val colors = Kit.colors
    Column(
        Modifier.fillMaxWidth().padding(start = 56.dp, end = 12.dp, bottom = 12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        if (remaining == null) {
            Box(Modifier.fillMaxWidth(0.75f).height(14.dp).background(colors.secondary, RowShape))
        } else {
            val names = stopsSummary(remaining)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                names.forEachIndexed { index, name ->
                    val last = index == names.lastIndex
                    Text(
                        name,
                        style = Kit.xs.copy(fontWeight = if (last) FontWeight.Medium else FontWeight.Normal),
                        color = if (last) colors.foreground else colors.mutedForeground,
                    )
                    if (!last) Icon(Icons.AutoMirrored.Outlined.ArrowForward, null, Modifier.size(12.dp).padding(top = 2.dp), tint = colors.mutedForeground)
                }
            }
        }
        Text(
            "See all of line ${arrival.line} →",
            Modifier.pressable(onSeeLine),
            style = Kit.xs.copy(fontWeight = FontWeight.Medium),
            color = colors.pinFill,
        )
    }
}

private fun stopsSummary(remaining: List<PathStop>): List<String> {
    val names = remaining.map { titleCase(it.name) }
    if (names.size <= 4) return names
    return listOf(names[0], names[1], "${names.size - 3} more", names.last())
}

private fun waitingText(lines: List<LineStatusView>): String {
    val names = lines.map { it.line }.distinct().joinToString(", ")
    val soonest = lines.mapNotNull { it.retryInSeconds }.minOrNull() ?: return "Waiting for İBB: $names"
    val next = if (soonest < 60) "in about $soonest s" else "in about ${(soonest + 59) / 60} min"
    return "Waiting for İBB: $names · next try $next"
}

private fun oldestFetch(lines: List<LineStatusView>): String? = lines.mapNotNull { it.data?.fetchedAt }.minOrNull()
