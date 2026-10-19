package com.farukerata.kackaldi.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.material3.FilterChip
import androidx.compose.ui.draw.alpha
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.farukerata.kackaldi.data.StopFilters
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.stateIn
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material.icons.rounded.StarOutline
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.viewModelScope
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.LineStatusView
import com.farukerata.kackaldi.data.StopArrivalView
import com.farukerata.kackaldi.data.StopArrivalsView
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

private const val REFRESH_MILLIS = 30_000L

class StopViewModel(private val api: KacKaldiApi, private val filters: StopFilters, val code: String) : ViewModel() {
    val shownLines: StateFlow<Set<String>> = filters.lines(code).stateIn(viewModelScope, SharingStarted.Eagerly, emptySet())

    var arrivals by mutableStateOf<Load<StopArrivalsView>>(Load.Loading)
        private set
    var refreshing by mutableStateOf(false)
        private set
    var favoriteId by mutableStateOf<Long?>(null)
        private set

    suspend fun refresh() {
        val lines = filters.lines(code).first()
        val next = load { api.stopArrivals(code, lines) }
        if (next is Load.Ready || arrivals !is Load.Ready) arrivals = next
    }

    fun pullToRefresh() {
        viewModelScope.launch {
            refreshing = true
            refresh()
            refreshing = false
        }
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
fun StopScreen(viewModel: StopViewModel, onBack: () -> Unit, onLine: (String) -> Unit) {
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

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text(stop?.let { titleCase(it.name) } ?: "", maxLines = 1)
                        val place = listOfNotNull(stop?.district?.takeIf { it.isNotBlank() }?.let(::titleCase), stop?.towards?.let { "→ ${titleCase(it)}" })
                        if (place.isNotEmpty()) Text(place.joinToString("  ·  "), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                },
                navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Rounded.ArrowBack, contentDescription = "Back") } },
                actions = {
                    val saved = viewModel.favoriteId != null
                    IconButton(onClick = viewModel::toggleFavorite) {
                        Icon(if (saved) Icons.Rounded.Star else Icons.Rounded.StarOutline, contentDescription = if (saved) "Remove from favourites" else "Save stop")
                    }
                },
            )
        },
    ) { padding ->
        PullToRefreshBox(viewModel.refreshing, viewModel::pullToRefresh, Modifier.fillMaxSize().padding(padding)) {
            when (val state = viewModel.arrivals) {
                Load.Loading -> LoadingState()
                is Load.Failed -> ErrorState(state.message, viewModel::pullToRefresh)
                is Load.Ready -> ArrivalsList(state.value, shown, viewModel, onLine)
            }
        }
    }
}

@Composable
private fun ArrivalsList(data: StopArrivalsView, shown: Set<String>, viewModel: StopViewModel, onLine: (String) -> Unit) {
    val allLines = data.lines.map { it.line }.distinct()
    val visible = shown.intersect(allLines.toSet()).ifEmpty { allLines.toSet() }
    val lines = data.lines.filter { it.line in visible }
    val arrivals = data.arrivals.filter { it.line in visible }
    val bothWays = lines.groupBy { it.line }.filterValues { it.size > 1 }.keys
    val waiting = lines.filter { it.error != null }
    LazyColumn(Modifier.fillMaxSize()) {
        if (allLines.size > 1) item {
            LineFilter(allLines, visible.takeIf { it.size < allLines.size }.orEmpty(), { viewModel.toggleLine(it, allLines) }, viewModel::showAllLines)
        }
        if (arrivals.isEmpty()) item {
            MessageState("No bus on the way", "Lines here: ${visible.joinToString(", ")}.")
        }
        items(arrivals, key = { "${it.line}-${it.bus.doorNo}" }) { arrival ->
            val fetchedAt = lines.firstOrNull { it.line == arrival.line && it.direction == arrival.direction }?.data?.fetchedAt
            val age = arrival.bus.ageSeconds + (fetchedAt?.let(::secondsSince) ?: 0L)
            ArrivalRow(arrival, data.stop.name, arrival.line in bothWays, age) { onLine(arrival.line) }
        }
        if (waiting.isNotEmpty()) item {
            Text(waitingText(waiting), Modifier.padding(horizontal = 16.dp, vertical = 8.dp), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        oldestFetch(lines)?.let { fetchedAt ->
            item {
                Text(
                    "Updated ${ago(secondsSince(fetchedAt))}",
                    Modifier.fillMaxWidth().padding(16.dp),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                )
            }
        }
    }
}

@Composable
private fun LineFilter(lines: List<String>, picked: Set<String>, onToggle: (String) -> Unit, onAll: () -> Unit) {
    LazyRow(
        contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        item { FilterChip(selected = picked.isEmpty(), onClick = onAll, label = { Text("All") }) }
        items(lines, key = { it }) { line ->
            FilterChip(selected = line in picked, onClick = { onToggle(line) }, label = { Text(line) })
        }
    }
}

@Composable
private fun ArrivalRow(arrival: StopArrivalView, stopName: String, bothWays: Boolean, ageSeconds: Long, onClick: () -> Unit) {
    val age = reportAge(ageSeconds)
    val faded = Modifier.alpha(if (age == ReportAge.Fresh) 1f else 0.55f)
    val bus = arrival.bus
    val endsHere = arrival.directionName.equals(stopName, ignoreCase = true)
    val where = buildList {
        add(busWhere(bus.notDeparted, bus.stopsAway))
        when {
            endsHere -> add("ends here")
            bothWays -> add("to ${titleCase(arrival.directionName)}")
        }
        if (!bus.notDeparted && bus.stopsAway > 0) bus.lastStopName?.let { add("now at ${titleCase(it)}") }
        if (age != ReportAge.Fresh) add(seenAgo(ageSeconds))
    }
    ListItem(
        leadingContent = { Box(faded) { RowLineBadge(arrival.line) } },
        headlineContent = { Text(where.first()) },
        supportingContent = if (where.size > 1) ({ Text(where.drop(1).joinToString(" · ").replaceFirstChar { it.uppercase() }) }) else null,
        trailingContent = { Box(faded) { Minutes(bus.etaSeconds, bus.notDeparted) } },
        modifier = Modifier.clickable(onClick = onClick),
    )
}

@Composable
private fun Minutes(etaSeconds: Int?, notDeparted: Boolean) {
    if (etaSeconds == null) {
        Text(if (notDeparted) "Not left" else "–", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
        return
    }
    Column(horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy((-2).dp)) {
        Text(minutes(etaSeconds), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)
        Text("min", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

private fun waitingText(lines: List<LineStatusView>): String {
    val names = lines.map { it.line }.distinct().joinToString(", ")
    val soonest = lines.mapNotNull { it.retryInSeconds }.minOrNull() ?: return "Waiting for İBB: $names"
    val next = if (soonest < 60) "in about $soonest s" else "in about ${(soonest + 59) / 60} min"
    return "Waiting for İBB: $names · next try $next"
}

private fun oldestFetch(lines: List<LineStatusView>): String? = lines.mapNotNull { it.data?.fetchedAt }.minOrNull()
