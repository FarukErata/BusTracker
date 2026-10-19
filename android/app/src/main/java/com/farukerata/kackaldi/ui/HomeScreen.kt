package com.farukerata.kackaldi.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.Place
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
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
        viewModelScope.launch { favorites = load { api.favorites() } }
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

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen(viewModel: HomeViewModel, updater: AppUpdater, onStop: (String) -> Unit, onLine: (String) -> Unit, onDevices: () -> Unit) {
    val query by viewModel.query.collectAsStateWithLifecycle()
    val results by viewModel.results.collectAsStateWithLifecycle()
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(Unit) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) { viewModel.refreshFavorites() }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Kaç Kaldı") },
                actions = {
                    IconButton(onClick = onDevices) { Icon(Icons.Outlined.Devices, contentDescription = "Your devices") }
                },
            )
        },
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            UpdateBanner(updater)
            OutlinedTextField(
                value = query,
                onValueChange = { viewModel.query.value = it },
                placeholder = { Text("Stop or line, like Fabrikalar or 500T") },
                leadingIcon = { Icon(Icons.Outlined.Search, contentDescription = null) },
                trailingIcon = {
                    if (query.isNotEmpty()) IconButton(onClick = { viewModel.query.value = "" }) {
                        Icon(Icons.Rounded.Close, contentDescription = "Clear search")
                    }
                },
                singleLine = true,
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp),
            )
            LazyColumn(Modifier.fillMaxSize()) {
                val current = results
                if (query.isBlank() || current == null) favoritesSection(viewModel.favorites, viewModel::refreshFavorites, onStop, onLine)
                else resultsSection(current, onStop, onLine)
                item { VersionFooter(updater.currentVersion) }
            }
        }
    }
}

private fun LazyListScope.favoritesSection(
    favorites: Load<FavoritesResponse>, onRetry: () -> Unit, onStop: (String) -> Unit, onLine: (String) -> Unit,
) {
    when (favorites) {
        Load.Loading -> item { LoadingState() }
        is Load.Failed -> item { ErrorState(favorites.message, onRetry) }
        is Load.Ready -> {
            val (lines, stops) = favorites.value
            if (lines.isEmpty() && stops.isEmpty()) {
                item { MessageState("Save the stops and lines you use", "Search above, open a stop, and tap the star.") }
                return
            }
            if (stops.isNotEmpty()) item { SectionTitle("Your stops") }
            items(stops, key = { "stop-${it.favorite.id}" }) { entry ->
                StopRow(entry.stop?.name ?: entry.favorite.stopName, entry.stop?.district, entry.stop?.towards, entry.lines) {
                    onStop(entry.favorite.stopCode)
                }
            }
            if (lines.isNotEmpty()) item { SectionTitle("Your lines") }
            items(lines, key = { "line-${it.favorite.id}" }) { entry ->
                val direction = entry.favorite.direction?.let { entry.directions[it] }
                ListItem(
                    leadingContent = { RowLineBadge(entry.favorite.lineCode) },
                    headlineContent = { Text(entry.favorite.label ?: direction?.let(::titleCase) ?: "Both directions") },
                    modifier = Modifier.clickable { onLine(entry.favorite.lineCode) },
                )
            }
        }
    }
}

private fun LazyListScope.resultsSection(results: Load<SearchResults>, onStop: (String) -> Unit, onLine: (String) -> Unit) {
    when (results) {
        Load.Loading -> item { LoadingState() }
        is Load.Failed -> item { MessageState("Search isn’t available right now", results.message) }
        is Load.Ready -> {
            val (lines, stops) = results.value
            if (lines.isEmpty() && stops.isEmpty()) {
                item { MessageState("Nothing found", "Try part of a stop name or a line code.") }
                return
            }
            if (lines.isNotEmpty()) item { SectionTitle("Lines") }
            items(lines, key = { "line-${it.lineCode}" }) { line ->
                val first = line.directions.firstOrNull()
                ListItem(
                    leadingContent = { RowLineBadge(line.lineCode) },
                    headlineContent = { Text(first?.let { directionLabel(it.from, it.to) } ?: line.lineCode) },
                    modifier = Modifier.clickable { onLine(line.lineCode) },
                )
            }
            if (stops.isNotEmpty()) item { SectionTitle("Stops") }
            items(stops, key = { "stop-${it.stop.code}" }) { result ->
                StopRow(result.stop.name, result.stop.district, result.stop.towards, result.lines) { onStop(result.stop.code) }
            }
        }
    }
}

@Composable
private fun VersionFooter(version: String) {
    Text(
        "Kaç Kaldı $version",
        Modifier.fillMaxWidth().padding(24.dp),
        style = MaterialTheme.typography.labelSmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        textAlign = TextAlign.Center,
    )
}

@Composable
private fun StopRow(name: String, district: String?, towards: String?, lines: List<String>, onClick: () -> Unit) {
    ListItem(
        leadingContent = { Icon(Icons.Outlined.Place, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant) },
        headlineContent = { Text(titleCase(name)) },
        supportingContent = {
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                val place = listOfNotNull(district?.takeIf { it.isNotBlank() }?.let(::titleCase), towards?.let { "→ ${titleCase(it)}" })
                if (place.isNotEmpty()) Text(place.joinToString("  ·  "))
                if (lines.isNotEmpty()) FlowRow(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    lines.take(6).forEach { LineBadge(it) }
                    if (lines.size > 6) Text("+${lines.size - 6}", style = MaterialTheme.typography.labelMedium)
                }
            }
        },
        modifier = Modifier.clickable(onClick = onClick),
    )
}
