package com.farukerata.kackaldi.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBars
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Place
import androidx.compose.material.icons.outlined.StarOutline
import androidx.compose.material.icons.outlined.SwapHoriz
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material3.BottomSheetScaffold
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.SheetValue
import androidx.compose.material3.Text
import androidx.compose.material3.rememberBottomSheetScaffoldState
import androidx.compose.material3.rememberStandardBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.viewModelScope
import com.farukerata.kackaldi.data.ArrivalView
import com.farukerata.kackaldi.data.BusPathResponse
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.LineArrivalsResponse
import com.farukerata.kackaldi.data.LineDirection
import com.farukerata.kackaldi.data.LineLiveResponse
import com.farukerata.kackaldi.data.LineStopsResponse
import com.farukerata.kackaldi.data.LiveBus
import com.farukerata.kackaldi.data.RouteShape
import com.farukerata.kackaldi.i18n.S
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

private const val LIVE_REFRESH_MILLIS = 30_000L
private val ShownStatuses = setOf("ok", "at-terminal", "stale")
private val PeekHeight = 180.dp

class LineViewModel(private val api: KacKaldiApi, val code: String, initialDirection: String?, initialStop: String?) : ViewModel() {
    var stops by mutableStateOf<Load<LineStopsResponse>>(Load.Loading)
        private set
    var shapes by mutableStateOf<List<RouteShape>>(emptyList())
        private set
    var live by mutableStateOf<LineLiveResponse?>(null)
        private set
    var arrivals by mutableStateOf<Load<LineArrivalsResponse>?>(null)
        private set
    var arrivalsReceivedAt by mutableLongStateOf(0L)
        private set
    var busPath by mutableStateOf<BusPathResponse?>(null)
        private set
    var directionCode by mutableStateOf(initialDirection)
        private set
    var pinnedStop by mutableStateOf(initialStop)
        private set
    var selectedBus by mutableStateOf<String?>(null)
        private set
    var favoriteId by mutableStateOf<Long?>(null)
        private set

    fun direction(): LineDirection? {
        val all = stops.valueOrNull?.directions.orEmpty()
        return all.firstOrNull { it.direction == directionCode } ?: all.firstOrNull()
    }

    fun reload() {
        viewModelScope.launch {
            stops = load { api.lineStops(code) }
            refreshPinned()
        }
        viewModelScope.launch { load { api.lineShapes(code) }.valueOrNull?.let { shapes = it.shapes } }
        loadFavorite()
    }

    suspend fun refreshLive() {
        load { api.lineLive(code) }.valueOrNull?.let { live = it }
        refreshPinned()
    }

    private suspend fun refreshPinned() {
        val stop = pinnedStop ?: return
        val dir = direction()?.direction ?: return
        val next = load { api.lineArrivals(code, stop, dir) }
        if (pinnedStop != stop) return
        if (next is Load.Ready) arrivalsReceivedAt = System.currentTimeMillis()
        if (next is Load.Ready || arrivals !is Load.Ready) arrivals = next
        val bus = selectedBus
        busPath = if (bus == null) null else load { api.busPath(code, bus, stop) }.valueOrNull?.takeIf { it.bus.doorNo == selectedBus }
    }

    fun pin(stop: String?) {
        pinnedStop = stop
        arrivals = if (stop == null) null else Load.Loading
        busPath = null
        viewModelScope.launch { refreshPinned() }
    }

    fun select(door: String?) {
        selectedBus = if (selectedBus == door) null else door
        busPath = null
        viewModelScope.launch { refreshPinned() }
    }

    fun toggleDirection() {
        val all = stops.valueOrNull?.directions.orEmpty()
        if (all.size < 2) return
        val index = all.indexOfFirst { it.direction == direction()?.direction }
        directionCode = all[(index + 1) % all.size].direction
        pinnedStop = null
        selectedBus = null
        arrivals = null
        busPath = null
        loadFavorite()
    }

    private fun loadFavorite() {
        viewModelScope.launch {
            val favorites = load { api.favorites() }.valueOrNull ?: return@launch
            val dir = direction()?.direction ?: directionCode
            favoriteId = favorites.lines.firstOrNull { it.favorite.lineCode == code && it.favorite.direction == dir }?.favorite?.id
        }
    }

    fun toggleFavorite() {
        val dir = direction()?.direction ?: return
        viewModelScope.launch {
            val id = favoriteId
            load { if (id == null) api.addFavoriteLine(code, dir) else api.removeFavoriteLine(id) }
            loadFavorite()
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LineScreen(viewModel: LineViewModel, onBack: () -> Unit, onDirectionShown: (LineDirection) -> Unit) {
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    val shown = viewModel.direction()
    LaunchedEffect(shown?.direction) { shown?.let(onDirectionShown) }
    LaunchedEffect(viewModel.code) {
        if (viewModel.stops !is Load.Ready) viewModel.reload()
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                viewModel.refreshLive()
                delay(LIVE_REFRESH_MILLIS)
            }
        }
    }
    val colors = Kit.colors
    val sheet = rememberStandardBottomSheetState(initialValue = SheetValue.PartiallyExpanded, skipHiddenState = true)
    val scaffold = rememberBottomSheetScaffoldState(sheet)

    BoxWithConstraints(Modifier.fillMaxSize().background(colors.background)) {
        val fullHeight = maxHeight
        val pinnedPeek = fullHeight * 0.5f
        val peek = if (viewModel.pinnedStop != null) pinnedPeek else PeekHeight
        BottomSheetScaffold(
            scaffoldState = scaffold,
            sheetPeekHeight = peek,
            sheetContainerColor = colors.background,
            sheetContentColor = colors.foreground,
            sheetShape = RoundedCornerShape(topStart = 16.dp, topEnd = 16.dp),
            sheetShadowElevation = 8.dp,
            sheetDragHandle = {
                Box(Modifier.padding(vertical = 10.dp).width(36.dp).height(4.dp).background(colors.border, CircleShape))
            },
            containerColor = colors.background,
            sheetContent = {
                LinePanel(viewModel, Modifier.height(fullHeight * 0.92f))
            },
        ) {
            Box(Modifier.fillMaxSize()) {
                when (val state = viewModel.stops) {
                    is Load.Failed -> ErrorState(state.message, viewModel::reload, Modifier.padding(top = 120.dp))
                    else -> LineMapLayer(viewModel, peek.value.toInt())
                }
                LineHeaderCard(viewModel, onBack)
            }
        }
    }
}

@Composable
private fun LineMapLayer(viewModel: LineViewModel, bottomInsetDp: Int) {
    val direction = viewModel.direction()
    val route = routeFor(viewModel.shapes, direction)
    val buses = busesIn(viewModel.live, direction)
    val selected = buses.firstOrNull { it.doorNo == viewModel.selectedBus }
    val variant = selected?.takeIf { it.variant != "D0" }?.let { bus -> viewModel.shapes.firstOrNull { it.routeCode == bus.routeCode }?.points }.orEmpty()
    val path = viewModel.busPath?.takeIf { !it.passed && it.bus.doorNo == viewModel.selectedBus }?.path.orEmpty()
    val stops = direction?.stops.orEmpty()
    val pinned = stops.firstOrNull { it.code == viewModel.pinnedStop }
    TransitMap(
        MapScene(
            frame = MapFrame.Fit("${viewModel.code}-${direction?.direction}-${route.size}", route),
            route = route,
            variant = variant,
            highlight = path,
            stops = stops.filter { it.lat != 0.0 }.map { MapStop(it.code, it.lat, it.lng) },
            pinned = pinned?.let { MapStop(it.code, it.lat, it.lng) },
            buses = buses.map { MapBus(it.doorNo, it.lat, it.lng, it.status == "at-terminal", reportAge(secondsSince(it.reportedAt)), it.heavyTraffic) },
            selectedBus = viewModel.selectedBus,
            bottomInsetDp = bottomInsetDp,
            topInsetDp = 64,
        ),
        Modifier.fillMaxSize(),
        onStop = viewModel::pin,
        onBus = viewModel::select,
    )
}

@Composable
private fun LineHeaderCard(viewModel: LineViewModel, onBack: () -> Unit) {
    val colors = Kit.colors
    val direction = viewModel.direction()
    val canToggle = (viewModel.stops.valueOrNull?.directions?.size ?: 0) > 1
    val first = direction?.stops?.firstOrNull()
    val last = direction?.stops?.lastOrNull()
    val saved = viewModel.favoriteId != null
    Row(
        Modifier
            .windowInsetsPadding(WindowInsets.statusBars)
            .padding(8.dp)
            .fillMaxWidth()
            .border(1.dp, colors.border, CardShape)
            .background(colors.background.copy(alpha = 0.95f), CardShape)
            .padding(4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        GhostIcon(Icons.AutoMirrored.Outlined.ArrowBack, S.back, onBack)
        LineBadge(viewModel.code)
        Row(
            Modifier.weight(1f).pressable(viewModel::toggleDirection, enabled = canToggle).padding(horizontal = 8.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            Text(
                if (first != null && last != null) "${titleCase(first.name)} → ${titleCase(last.name)}" else S.loading,
                Modifier.weight(1f, fill = false),
                style = Kit.sm,
                color = colors.foreground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            if (canToggle) Icon(Icons.Outlined.SwapHoriz, S.switchDirection, Modifier.size(16.dp), tint = colors.mutedForeground)
        }
        GhostIcon(
            if (saved) Icons.Rounded.Star else Icons.Outlined.StarOutline,
            if (saved) S.unsaveLine else S.saveLine,
            viewModel::toggleFavorite,
            tint = if (saved) colors.star else colors.foreground,
        )
    }
}

@Composable
private fun LinePanel(viewModel: LineViewModel, modifier: Modifier) {
    val direction = viewModel.direction()
    val loading = viewModel.stops is Load.Loading || viewModel.live == null
    LazyColumn(modifier.fillMaxWidth().windowInsetsPadding(WindowInsets.navigationBars)) {
        val pinned = viewModel.pinnedStop
        if (pinned != null && direction != null) pinnedArrivals(viewModel, direction, pinned)
        else busPositions(viewModel, direction, loading)
        item { UpdatedAgo(viewModel.live?.data?.fetchedAt) }
    }
}

private fun LazyListScope.pinnedArrivals(viewModel: LineViewModel, direction: LineDirection, pinned: String) {
    val stop = direction.stops.firstOrNull { it.code == pinned }
    item {
        val colors = Kit.colors
        Row(
            Modifier
                .padding(horizontal = 12.dp)
                .fillMaxWidth()
                .background(colors.pinFill.copy(alpha = 0.12f), RowShape)
                .padding(start = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            Icon(Icons.Outlined.Place, null, Modifier.size(14.dp), tint = colors.pinFill)
            Text(stop?.let { S.toStop(titleCase(it.name)) } ?: S.toThisStop, Modifier.weight(1f), style = Kit.sm, color = colors.pinFill, maxLines = 1, overflow = TextOverflow.Ellipsis)
            GhostIcon(Icons.Outlined.Close, S.clearStop, { viewModel.pin(null) }, tint = colors.pinFill)
        }
    }
    when (val state = viewModel.arrivals) {
        null, Load.Loading -> item { RowsSkeleton(rows = 3) }
        is Load.Failed -> item { ErrorState(state.message, { viewModel.pin(pinned) }) }
        is Load.Ready -> {
            val approaching = state.value.approaching
            if (approaching.isEmpty()) item {
                Text(S.noBusToStop, Modifier.fillMaxWidth().padding(16.dp), style = Kit.sm, color = Kit.colors.mutedForeground, textAlign = TextAlign.Center)
            }
            items(approaching, key = { "arrival-${it.doorNo}" }) { arrival ->
                ApproachingRow(arrival, viewModel.arrivalsReceivedAt, arrival.doorNo == viewModel.selectedBus) { viewModel.select(arrival.doorNo) }
            }
            val passed = state.value.recentlyPassed.size
            if (passed > 0) item {
                Text(S.passed(passed), Modifier.padding(horizontal = 12.dp, vertical = 4.dp), style = Kit.xs, color = Kit.colors.mutedForeground)
            }
        }
    }
}

@Composable
private fun ApproachingRow(arrival: ArrivalView, receivedAt: Long, selected: Boolean, onClick: () -> Unit) {
    val detail = if (arrival.notDeparted) {
        S.atFirstStop
    } else {
        listOfNotNull(stopsAway(arrival.stopsAway), distance(arrival.distanceMeters), arrival.lastStopName?.let { S.nowAt(titleCase(it)) }).joinToString(" · ")
    }
    ItemRow(
        onClick = onClick,
        selected = selected,
        trailing = { arrival.etaSeconds?.let { Minutes(it, receivedAt) } ?: NotLeft() },
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ItemTitle(arrival.doorNo)
            if (arrival.heavyTraffic) TrafficBadge()
        }
        ItemDescription(detail)
    }
}

private fun LazyListScope.busPositions(viewModel: LineViewModel, direction: LineDirection?, loading: Boolean) {
    item { Hint(S.tapStop, Icons.Outlined.Place) }
    if (loading) {
        item { RowsSkeleton(rows = 3) }
        return
    }
    val buses = busesIn(viewModel.live, direction).sortedByDescending { it.alongMeters ?: 0.0 }
    if (buses.isEmpty()) item {
        Text(S.noBusDirection, Modifier.fillMaxWidth().padding(16.dp), style = Kit.sm, color = Kit.colors.mutedForeground, textAlign = TextAlign.Center)
    }
    items(buses, key = { "bus-${it.doorNo}" }) { bus ->
        val age = secondsSince(bus.reportedAt)
        ItemRow(onClick = { viewModel.select(bus.doorNo) }, selected = bus.doorNo == viewModel.selectedBus) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                ItemTitle(bus.doorNo, Modifier.ageRing(reportAge(age), RoundedCornerShape(6.dp)).padding(horizontal = 4.dp))
                if (bus.heavyTraffic) TrafficBadge()
            }
            ItemDescription(describePosition(bus, direction, age))
        }
    }
    if (direction != null) {
        item { SectionTitle(S.stops) }
        items(direction.stops, key = { "stop-${direction.direction}-${it.seq}-${it.code}" }) { stop ->
            Row(
                Modifier.fillMaxWidth().pressable({ viewModel.pin(stop.code) }).padding(horizontal = 10.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Text("${stop.seq}", Modifier.width(24.dp), style = Kit.xs, color = Kit.colors.mutedForeground, textAlign = TextAlign.End)
                Text(titleCase(stop.name), style = Kit.sm, color = Kit.colors.foreground, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

private fun describePosition(bus: LiveBus, direction: LineDirection?, ageSeconds: Long): String {
    fun name(seq: Int?) = seq?.let { s -> direction?.stops?.firstOrNull { it.seq == s } }?.let { titleCase(it.name) }
    val place = if (bus.status == "at-terminal") {
        name(direction?.stops?.firstOrNull()?.seq)?.let(S::waitingAt) ?: S.waitingAtFirst
    } else {
        listOfNotNull(name(bus.lastStopSeq)?.let { S.after(it) }, name(bus.nextStopSeq)?.let { S.next(it) }).joinToString(" · ")
    }
    val seen = if (reportAge(ageSeconds) != ReportAge.Fresh) seenAgo(ageSeconds) else null
    return listOfNotNull(place.ifBlank { null }, seen).joinToString(" · ").replaceFirstChar { it.uppercase() }
}

private fun routeFor(shapes: List<RouteShape>, direction: LineDirection?): List<List<Double>> {
    if (direction == null) return emptyList()
    val own = shapes.filter { it.direction == direction.direction }
    val shape = own.firstOrNull { it.variant == "D0" } ?: own.firstOrNull()
    return shape?.points ?: direction.stops.filter { it.lat != 0.0 }.map { listOf(it.lat, it.lng) }
}

private fun busesIn(live: LineLiveResponse?, direction: LineDirection?): List<LiveBus> =
    live?.buses.orEmpty().filter { it.direction == direction?.direction && it.status in ShownStatuses }
