package com.farukerata.kackaldi.ui

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.provider.Settings
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
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
import androidx.compose.material.icons.outlined.NotificationsActive
import androidx.compose.material.icons.outlined.NotificationsNone
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
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.core.net.toUri
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.viewModelScope
import com.farukerata.kackaldi.KacKaldiApplication
import com.farukerata.kackaldi.data.AlertLine
import com.farukerata.kackaldi.data.BusAlert
import com.farukerata.kackaldi.data.BusPathResponse
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.LineStatusView
import com.farukerata.kackaldi.data.PathStop
import com.farukerata.kackaldi.data.Recent
import com.farukerata.kackaldi.data.SeenBus
import com.farukerata.kackaldi.data.StopArrivalView
import com.farukerata.kackaldi.data.StopArrivalsView
import com.farukerata.kackaldi.data.StopFilters
import com.farukerata.kackaldi.data.TriggerKind
import com.farukerata.kackaldi.data.Watch
import com.farukerata.kackaldi.data.WatchTrigger
import com.farukerata.kackaldi.i18n.S
import com.farukerata.kackaldi.watch.AlertScheduler
import com.farukerata.kackaldi.watch.WatchService
import java.util.UUID
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

private const val REFRESH_MILLIS = 30_000L
private const val WAITING_RETRY_MILLIS = 5_000L

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
                delay(nextRefreshMillis(viewModel.arrivals.valueOrNull))
            }
        }
    }
    val stop = viewModel.arrivals.valueOrNull?.stop
    val shown by viewModel.shownLines.collectAsStateWithLifecycle()
    val colors = Kit.colors
    val context = LocalContext.current
    val app = context.applicationContext as KacKaldiApplication
    val scope = rememberCoroutineScope()
    val activeWatch by app.watch.collectAsStateWithLifecycle()
    LaunchedEffect(stop?.code) {
        stop?.let { app.recents.remember(Recent(Recent.STOP, it.code, it.name, it.district)) }
    }
    val lastTrigger by app.settings.lastTrigger.collectAsStateWithLifecycle(initialValue = WatchTrigger(TriggerKind.Minutes, 5))
    var watchFor by remember { mutableStateOf<List<AlertLine>?>(null) }
    var pending by remember { mutableStateOf<AlertDraft?>(null) }
    val apply = { draft: AlertDraft, stopName: String ->
        scope.launch {
            app.settings.rememberTrigger(draft.trigger)
            applyDraft(context, app, viewModel.code, stopName, draft)
        }
    }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        val draft = pending
        pending = null
        val name = viewModel.arrivals.valueOrNull?.stop?.name.orEmpty()
        if (granted && draft != null) apply(draft, name)
        else Toast.makeText(context, S.allowNotifications, Toast.LENGTH_LONG).show()
    }

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
                    if (saved) S.unsaveStop else S.saveStop,
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
                is Load.Ready -> ArrivalsList(state.value, shown, viewModel, onLine, activeWatch, { watchFor = it }, { WatchService.stop(context) })
            }
        }
    }
    val target = watchFor
    val data = viewModel.arrivals.valueOrNull
    if (target != null && data != null) {
        AlertSheet(
            api = app.api,
            stopCode = viewModel.code,
            options = data.lines.map { AlertLine(it.line, it.direction, it.directionName) }.distinct(),
            preselected = target,
            approaching = data.arrivals.map { SeenBus(it.line, it.bus) },
            initial = lastTrigger,
            onSave = { draft ->
                watchFor = null
                if (needsNotificationPermission(context)) {
                    pending = draft
                    permission.launch(Manifest.permission.POST_NOTIFICATIONS)
                } else {
                    apply(draft, data.stop.name)
                }
            },
            onDismiss = { watchFor = null },
        )
    }
}

private suspend fun applyDraft(context: Context, app: KacKaldiApplication, stopCode: String, stopName: String, draft: AlertDraft) {
    val readyAt = draft.readyAt
    if (readyAt == null) {
        val now = System.currentTimeMillis()
        WatchService.start(context, Watch(stopCode, stopName, draft.lines, draft.trigger, now, now))
        return
    }
    val alert = BusAlert(UUID.randomUUID().toString(), stopCode, stopName, draft.lines, draft.trigger, clockText(readyAt), draft.days)
    AlertScheduler.save(context, app.alerts, alert)
    Toast.makeText(context, S.alertSaved(alert.readyAt, S.repeat(alert.days)), Toast.LENGTH_LONG).show()
    if (!AlertScheduler.canSchedule(context)) askForAlarms(context)
}

private fun askForAlarms(context: Context) {
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S) return
    Toast.makeText(context, S.allowAlarms, Toast.LENGTH_LONG).show()
    context.startActivity(Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM, "package:${context.packageName}".toUri()))
}

private fun nextRefreshMillis(data: StopArrivalsView?): Long {
    val waiting = data?.lines?.filter { it.error != null }.orEmpty()
    if (waiting.isEmpty()) return REFRESH_MILLIS
    val soonest = waiting.minOf { (it.retryInSeconds ?: 0) * 1000L }
    return soonest.coerceIn(WAITING_RETRY_MILLIS, REFRESH_MILLIS)
}

private fun needsNotificationPermission(context: Context): Boolean =
    Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
        ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED

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
                buses = listOf(MapBus(tracking.bus.doorNo, tracking.bus.lat, tracking.bus.lng, age = reportAge(secondsSince(tracking.bus.reportedAt)), heavyTraffic = tracking.bus.heavyTraffic)),
                selectedBus = tracking.bus.doorNo,
            )
        } else {
            MapScene(frame = MapFrame.Center("stop-${viewModel.code}", lat, lng, 16.5), pinned = stop)
        }
        TransitMap(scene, Modifier.fillMaxSize())
    }
}

@Composable
private fun ArrivalsList(
    data: StopArrivalsView,
    shown: Set<String>,
    viewModel: StopViewModel,
    onLine: (String, String?, String?) -> Unit,
    watch: Watch?,
    onAlert: (List<AlertLine>) -> Unit,
    onStopWatching: () -> Unit,
) {
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
        item {
            if (watch != null && watch.stopCode == viewModel.code) WatchBar(watch, onStopWatching)
            else NewAlertButton { onAlert(alertLinesFor(lines, arrivals, filtered = visible.size < allLines.size)) }
        }
        if (arrivals.isEmpty()) item {
            EmptyState(S.noBus, S.linesHere(visible.joinToString(", ")))
        }
        items(arrivals, key = { "${it.line}-${it.bus.doorNo}" }) { arrival ->
            val fetchedAt = lines.firstOrNull { it.line == arrival.line && it.direction == arrival.direction }?.data?.fetchedAt
            val age = arrival.bus.ageSeconds + (fetchedAt?.let(::secondsSince) ?: 0L)
            val isSelected = viewModel.selection?.let { it.line == arrival.line && it.direction == arrival.direction && it.doorNo == arrival.bus.doorNo } == true
            Column(Modifier.animateContentSize().background(if (isSelected) Kit.colors.secondary.copy(alpha = 0.6f) else androidx.compose.ui.graphics.Color.Transparent)) {
                ArrivalRow(arrival, data.stop.name, arrival.line in bothWays, age, viewModel.receivedAt, false) { viewModel.toggle(arrival) }
                if (isSelected) TrackingDetails(arrival, viewModel.tracking?.remainingStops, { onAlert(listOf(alertLine(arrival))) }) { onLine(arrival.line, arrival.direction, viewModel.code) }
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
    val pinned = remember { picked }
    val ordered = lines.sortedByDescending { it in pinned }
    LazyRow(
        contentPadding = PaddingValues(horizontal = 12.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        item { Chip(S.all, picked.isEmpty(), onAll) }
        items(ordered, key = { it }) { line -> Chip(line, line in picked) { onToggle(line) } }
    }
}

@Composable
private fun ArrivalRow(
    arrival: StopArrivalView, stopName: String, bothWays: Boolean, ageSeconds: Long, receivedAt: Long, selected: Boolean, onClick: () -> Unit,
) {
    val age = reportAge(ageSeconds)
    val bus = arrival.bus
    val endsHere = arrival.directionName.equals(stopName, ignoreCase = true)
    val detail = buildList {
        if (endsHere) add(S.endsHere)
        if (bothWays && !endsHere) add(S.towards(titleCase(arrival.directionName)))
        if (!bus.notDeparted) bus.lastStopName?.let { add(S.nowAt(titleCase(it))) }
        if (age != ReportAge.Fresh) add(seenAgo(ageSeconds))
    }.joinToString(" · ").replaceFirstChar { it.uppercase() }
    ItemRow(
        onClick = onClick,
        selected = selected,
        leading = { LineBadge(arrival.line, age = age) },
        trailing = {
            val eta = bus.etaSeconds
            if (eta != null) Minutes(eta, receivedAt) else NotLeft()
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
private fun TrackingDetails(arrival: StopArrivalView, remaining: List<PathStop>?, onNotify: () -> Unit, onSeeLine: () -> Unit) {
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
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            KitButton(S.setAlert, onNotify, kind = ButtonKind.Outline, icon = Icons.Outlined.NotificationsNone)
            Text(
                S.seeLine(arrival.line),
                Modifier.pressable(onSeeLine),
                style = Kit.xs.copy(fontWeight = FontWeight.Medium),
                color = colors.pinFill,
            )
        }
    }
}

private fun alertLine(arrival: StopArrivalView) = AlertLine(arrival.line, arrival.direction, arrival.directionName)

private fun alertLinesFor(lines: List<LineStatusView>, arrivals: List<StopArrivalView>, filtered: Boolean): List<AlertLine> =
    if (filtered) lines.map { AlertLine(it.line, it.direction, it.directionName) }.distinct()
    else listOfNotNull(arrivals.firstOrNull()?.let(::alertLine))

@Composable
private fun NewAlertButton(onClick: () -> Unit) {
    KitButton(
        S.setAnAlert,
        onClick,
        Modifier.padding(start = 12.dp, end = 12.dp, top = 4.dp, bottom = 4.dp).fillMaxWidth(),
        kind = ButtonKind.Outline,
        icon = Icons.Outlined.NotificationsNone,
    )
}

@Composable
private fun WatchBar(watch: Watch, onStop: () -> Unit) {
    val colors = Kit.colors
    Row(
        Modifier.padding(start = 12.dp, end = 12.dp, top = 8.dp).fillMaxWidth().border(1.dp, colors.border, CardShape).padding(start = 12.dp, end = 4.dp, top = 6.dp, bottom = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Icon(Icons.Outlined.NotificationsActive, null, Modifier.size(18.dp), tint = colors.foreground)
        Column(Modifier.weight(1f)) {
            Text(S.watching(watch.lineNames), style = Kit.sm.copy(fontWeight = FontWeight.Medium), color = colors.foreground)
            Text(S.alertWhen(triggerSummary(watch.trigger)), style = Kit.xs, color = colors.mutedForeground)
        }
        KitButton(S.stop, onStop, kind = ButtonKind.Ghost)
    }
}

private fun stopsSummary(remaining: List<PathStop>): List<String> {
    val names = remaining.map { titleCase(it.name) }
    if (names.size <= 4) return names
    return listOf(names[0], names[1], S.moreStops(names.size - 3), names.last())
}

private fun waitingText(lines: List<LineStatusView>): String {
    val names = lines.map { it.line }.distinct().joinToString(", ")
    val soonest = lines.mapNotNull { it.retryInSeconds }.minOrNull() ?: return S.waiting(names)
    return "${S.waiting(names)} · ${S.nextTry(soonest)}"
}

private fun oldestFetch(lines: List<LineStatusView>): String? = lines.mapNotNull { it.data?.fetchedAt }.minOrNull()
