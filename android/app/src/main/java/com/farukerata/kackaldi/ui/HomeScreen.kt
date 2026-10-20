package com.farukerata.kackaldi.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.asPaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.DarkMode
import androidx.compose.material.icons.outlined.LightMode
import androidx.compose.material.icons.outlined.PersonOutline
import androidx.compose.material.icons.outlined.Place
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.viewModelScope
import com.farukerata.kackaldi.data.FavoriteLineEntry
import com.farukerata.kackaldi.data.FavoriteStopEntry
import com.farukerata.kackaldi.data.FavoritesResponse
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.LineSummary
import com.farukerata.kackaldi.data.StopSearchResult
import com.farukerata.kackaldi.update.AppUpdater
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.mapLatest
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

data class SearchResults(val lines: List<LineSummary>, val stops: List<StopSearchResult>)

@OptIn(FlowPreview::class, ExperimentalCoroutinesApi::class)
class HomeViewModel(private val api: KacKaldiApi) : ViewModel() {
    val query = MutableStateFlow("")

    var favorites by mutableStateOf<Load<FavoritesResponse>>(Load.Loading)
        private set

    val results: StateFlow<Load<SearchResults>?> = query
        .debounce(250)
        .mapLatest { search(it.trim()) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), null)

    fun refreshFavorites() {
        viewModelScope.launch {
            val next = load { api.favorites() }
            if (next is Load.Ready || favorites !is Load.Ready) favorites = next
        }
    }

    private suspend fun search(text: String): Load<SearchResults>? {
        if (text.isEmpty()) return null
        return load {
            SearchResults(
                lines = api.searchLines(text),
                stops = if (text.length >= 2) api.searchStops(text) else emptyList(),
            )
        }
    }
}

@Composable
fun HomeScreen(
    viewModel: HomeViewModel,
    updater: AppUpdater,
    dark: Boolean,
    onToggleTheme: () -> Unit,
    onStop: (String) -> Unit,
    onLine: (String, String?) -> Unit,
    onDevices: () -> Unit,
) {
    val query by viewModel.query.collectAsStateWithLifecycle()
    val results by viewModel.results.collectAsStateWithLifecycle()
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(Unit) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) { viewModel.refreshFavorites() }
    }

    Column(
        Modifier
            .fillMaxSize()
            .background(Kit.colors.background)
            .windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal)),
    ) {
        Header(
            actions = {
                GhostIcon(Icons.Outlined.PersonOutline, "Your devices", onDevices)
                GhostIcon(if (dark) Icons.Outlined.LightMode else Icons.Outlined.DarkMode, if (dark) "Light theme" else "Dark theme", onToggleTheme)
            },
        ) { HeaderTitle("Kaç Kaldı") }
        UpdateBanner(updater)
        SearchField(
            value = query,
            onChange = { viewModel.query.value = it },
            placeholder = "Stop or line, like Fabrikalar or 500T",
            modifier = Modifier.fillMaxWidth().padding(start = 12.dp, end = 12.dp, top = 8.dp),
        )
        LazyColumn(
            Modifier.fillMaxSize(),
            contentPadding = WindowInsets.navigationBars.asPaddingValues(),
        ) {
            val current = results
            if (query.isBlank() || current == null) favoritesSection(viewModel.favorites, viewModel::refreshFavorites, onStop, onLine)
            else resultsSection(current, onStop, onLine)
            item { VersionFooter(updater.currentVersion) }
        }
    }
}

private fun LazyListScope.favoritesSection(
    favorites: Load<FavoritesResponse>, onRetry: () -> Unit, onStop: (String) -> Unit, onLine: (String, String?) -> Unit,
) {
    when (favorites) {
        Load.Loading -> item { RowsSkeleton() }
        is Load.Failed -> item { ErrorState(favorites.message, onRetry) }
        is Load.Ready -> {
            val (lines, stops) = favorites.value
            if (lines.isEmpty() && stops.isEmpty()) {
                item { EmptyState("Save the stops and lines you use", "Search above, open a stop or a line, and tap the star.") }
                return
            }
            if (stops.isNotEmpty()) item { SectionTitle("Your stops") }
            items(stops, key = { "stop-${it.favorite.id}" }) { entry -> FavoriteStopRow(entry, onStop) }
            if (lines.isNotEmpty()) item { SectionTitle("Your lines") }
            items(lines, key = { "line-${it.favorite.id}" }) { entry -> FavoriteLineRow(entry, onLine) }
        }
    }
}

private fun LazyListScope.resultsSection(results: Load<SearchResults>, onStop: (String) -> Unit, onLine: (String, String?) -> Unit) {
    when (results) {
        Load.Loading -> item { RowsSkeleton(rows = 3) }
        is Load.Failed -> item { EmptyState("Search isn’t available right now", results.message) }
        is Load.Ready -> {
            val (lines, stops) = results.value
            if (lines.isEmpty() && stops.isEmpty()) {
                item { EmptyState("Nothing found", "Try part of a stop name or a line code.") }
                return
            }
            if (lines.isNotEmpty()) item { SectionTitle("Lines") }
            items(lines, key = { "line-${it.lineCode}" }) { line ->
                val first = line.directions.firstOrNull()
                ItemRow(
                    onClick = { onLine(line.lineCode, null) },
                    leading = { LineBadge(line.lineCode) },
                    trailing = { Chevron() },
                ) { ItemTitle(first?.let { directionLabel(it.from, it.to) } ?: line.lineCode) }
            }
            if (stops.isNotEmpty()) item { SectionTitle("Stops") }
            items(stops, key = { "stop-${it.stop.code}" }) { result ->
                StopRow(result.stop.name, result.stop.district, result.stop.towards, result.lines) { onStop(result.stop.code) }
            }
        }
    }
}

@Composable
private fun FavoriteStopRow(entry: FavoriteStopEntry, onStop: (String) -> Unit) {
    StopRow(entry.stop?.name ?: entry.favorite.stopName, entry.stop?.district, entry.stop?.towards, entry.lines, entry.favorite.label) {
        onStop(entry.favorite.stopCode)
    }
}

@Composable
private fun FavoriteLineRow(entry: FavoriteLineEntry, onLine: (String, String?) -> Unit) {
    val direction = entry.favorite.direction?.let { entry.directions[it] }
    ItemRow(
        onClick = { onLine(entry.favorite.lineCode, entry.favorite.direction) },
        leading = { LineBadge(entry.favorite.lineCode) },
        trailing = { Chevron() },
    ) { ItemTitle(entry.favorite.label ?: direction?.let(::titleCase) ?: "Both directions") }
}

@Composable
private fun StopRow(name: String, district: String?, towards: String?, lines: List<String>, label: String? = null, onClick: () -> Unit) {
    ItemRow(
        onClick = onClick,
        alignTop = true,
        leading = { Icon(Icons.Outlined.Place, null, Modifier.padding(top = 1.dp).size(18.dp), tint = Kit.colors.mutedForeground) },
        trailing = { Box(Modifier.padding(top = 20.dp)) { Chevron() } },
    ) {
        if (label != null) {
            ItemTitle(label)
            StopName(name, district, Kit.sm)
        } else {
            StopName(name, district)
        }
        Towards(towards)
        if (lines.isNotEmpty()) {
            Box(Modifier.padding(top = 4.dp)) { LineBadges(lines) }
        }
    }
}

@Composable
private fun VersionFooter(version: String) {
    Text(
        "Kaç Kaldı $version",
        Modifier.fillMaxWidth().padding(24.dp),
        style = Kit.xs,
        color = Kit.colors.mutedForeground,
        textAlign = TextAlign.Center,
    )
}
