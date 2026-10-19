package com.farukerata.kackaldi.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.rounded.DirectionsBus
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SegmentedButtonDefaults
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.viewModelScope
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.LineDirection
import com.farukerata.kackaldi.data.LineLiveResponse
import com.farukerata.kackaldi.data.LineStopsResponse
import com.farukerata.kackaldi.data.LiveBus
import com.farukerata.kackaldi.data.RouteShape
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

private const val LIVE_REFRESH_MILLIS = 30_000L
private val ShownStatuses = setOf("ok", "at-terminal", "stale")

class LineViewModel(private val api: KacKaldiApi, val code: String) : ViewModel() {
    var stops by mutableStateOf<Load<LineStopsResponse>>(Load.Loading)
        private set
    var shapes by mutableStateOf<List<RouteShape>>(emptyList())
        private set
    var live by mutableStateOf<LineLiveResponse?>(null)
        private set
    var direction by mutableIntStateOf(0)

    fun reload() {
        viewModelScope.launch { stops = load { api.lineStops(code) } }
        viewModelScope.launch { load { api.lineShapes(code) }.valueOrNull?.let { shapes = it.shapes } }
    }

    suspend fun refreshLive() {
        load { api.lineLive(code) }.valueOrNull?.let { live = it }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LineScreen(viewModel: LineViewModel, onBack: () -> Unit, onStop: (String) -> Unit) {
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(viewModel.code) {
        if (viewModel.stops !is Load.Ready) viewModel.reload()
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                viewModel.refreshLive()
                delay(LIVE_REFRESH_MILLIS)
            }
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { LineBadge(viewModel.code) },
                navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Rounded.ArrowBack, contentDescription = "Back") } },
            )
        },
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            when (val state = viewModel.stops) {
                Load.Loading -> LoadingState()
                is Load.Failed -> ErrorState(state.message, viewModel::reload)
                is Load.Ready -> LineContent(viewModel, state.value.directions, onStop)
            }
        }
    }
}

@Composable
private fun ColumnScope.LineContent(viewModel: LineViewModel, directions: List<LineDirection>, onStop: (String) -> Unit) {
    val current = directions.getOrNull(viewModel.direction) ?: return
    val buses = busesFor(viewModel.live, current)
    LineMap(
        data = LineMapData(
            key = "${viewModel.code}-${current.direction}-${viewModel.shapes.isNotEmpty()}",
            route = routeFor(viewModel.shapes, current),
            stops = current.stops,
            buses = buses,
        ),
        onStop = onStop,
        modifier = Modifier.fillMaxWidth().weight(0.42f),
    )
    if (directions.size > 1) DirectionChooser(directions, viewModel.direction) { viewModel.direction = it }
    StopList(current, busStops(viewModel.live, current), onStop, Modifier.weight(0.58f))
}

private fun routeFor(shapes: List<RouteShape>, direction: LineDirection): List<List<Double>> {
    val own = shapes.filter { it.direction == direction.direction }
    return (own.firstOrNull { it.variant == "D0" } ?: own.firstOrNull())?.points.orEmpty()
}

private fun liveIn(live: LineLiveResponse?, direction: LineDirection): List<LiveBus> =
    live?.buses.orEmpty().filter { it.direction == direction.direction && it.status in ShownStatuses }

private fun busesFor(live: LineLiveResponse?, direction: LineDirection): List<MapBus> =
    liveIn(live, direction).map { MapBus(it.doorNo, it.lat, it.lng, reportAge(secondsSince(it.reportedAt))) }

private fun busStops(live: LineLiveResponse?, direction: LineDirection): Map<Int, ReportAge> =
    liveIn(live, direction)
        .mapNotNull { bus -> bus.lastStopSeq?.let { it to reportAge(secondsSince(bus.reportedAt)) } }
        .groupBy({ it.first }, { it.second })
        .mapValues { (_, ages) -> ages.minBy { it.ordinal } }

@Composable
private fun DirectionChooser(directions: List<LineDirection>, selected: Int, onSelect: (Int) -> Unit) {
    SingleChoiceSegmentedButtonRow(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp)) {
        directions.forEachIndexed { index, direction ->
            SegmentedButton(
                selected = index == selected,
                onClick = { onSelect(index) },
                shape = SegmentedButtonDefaults.itemShape(index, directions.size),
                label = { Text("To ${titleCase(direction.stops.lastOrNull()?.name ?: direction.name)}", maxLines = 1, overflow = TextOverflow.Ellipsis) },
            )
        }
    }
}

@Composable
private fun StopList(direction: LineDirection, busAt: Map<Int, ReportAge>, onStop: (String) -> Unit, modifier: Modifier) {
    LazyColumn(modifier.fillMaxSize()) {
        items(direction.stops, key = { "${direction.direction}-${it.seq}-${it.code}" }) { stop ->
            val bus = busAt[stop.seq]
            ListItem(
                leadingContent = { Text("${stop.seq}", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant) },
                headlineContent = { Text(titleCase(stop.name)) },
                supportingContent = stop.district.takeIf { it.isNotBlank() }?.let { { Text(titleCase(it)) } },
                trailingContent = bus?.let { { BusHere(it) } },
                modifier = Modifier.clickable { onStop(stop.code) },
            )
        }
    }
}

@Composable
private fun BusHere(age: ReportAge) {
    Icon(
        Icons.Rounded.DirectionsBus,
        contentDescription = if (age == ReportAge.Fresh) "Bus just left this stop" else "Bus left this stop, last seen a while ago",
        tint = if (age == ReportAge.Stale) MaterialTheme.colorScheme.outline else MaterialTheme.colorScheme.primary,
        modifier = Modifier.size(20.dp).alpha(if (age == ReportAge.Fresh) 1f else 0.55f),
    )
}
