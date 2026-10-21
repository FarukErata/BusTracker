package com.farukerata.kackaldi.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.asPaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.DarkMode
import androidx.compose.material.icons.outlined.History
import androidx.compose.material.icons.outlined.LightMode
import androidx.compose.material.icons.outlined.NotificationsActive
import androidx.compose.material.icons.outlined.NotificationsNone
import androidx.compose.material.icons.outlined.PersonOutline
import androidx.compose.material.icons.outlined.Place
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.viewModelScope
import com.farukerata.kackaldi.data.BusAlert
import com.farukerata.kackaldi.data.FavoriteLineEntry
import com.farukerata.kackaldi.data.FavoriteStopEntry
import com.farukerata.kackaldi.data.AnnouncementView
import com.farukerata.kackaldi.data.FavoritesResponse
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.LineSummary
import com.farukerata.kackaldi.data.Recent
import com.farukerata.kackaldi.data.StopSearchResult
import com.farukerata.kackaldi.i18n.S
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
    var announcements by mutableStateOf<List<AnnouncementView>>(emptyList())
        private set

    val results: StateFlow<Load<SearchResults>?> = query
        .debounce(250)
        .mapLatest { search(it.trim()) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), null)

    fun refreshFavorites() {
        viewModelScope.launch {
            val next = load { api.favorites() }
            if (next is Load.Ready || favorites !is Load.Ready) favorites = next
            val saved = next.valueOrNull ?: return@launch
            val lines = saved.lines.map { it.favorite.lineCode }
            val stops = saved.stops.map { it.favorite.stopCode }
            announcements = if (lines.isEmpty() && stops.isEmpty()) emptyList()
            else load { api.announcements(lines, stops) }.valueOrNull?.announcements ?: announcements
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
    onAlerts: () -> Unit,
    alertRunning: Boolean,
    alerts: List<BusAlert>,
    onAlertEnabled: (BusAlert, Boolean) -> Unit,
    onAlertRemove: (BusAlert) -> Unit,
    recents: List<Recent>,
    onClearRecents: () -> Unit,
) {
    val query by viewModel.query.collectAsStateWithLifecycle()
    val results by viewModel.results.collectAsStateWithLifecycle()
    var searchFocused by remember { mutableStateOf(false) }
    val focusManager = LocalFocusManager.current
    BackHandler(enabled = searchFocused) { focusManager.clearFocus() }
    val leaveSearch = { focusManager.clearFocus() }
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
                LanguageMenu()
                GhostIcon(if (alertRunning) Icons.Outlined.NotificationsActive else Icons.Outlined.NotificationsNone, S.alerts, onAlerts)
                GhostIcon(Icons.Outlined.PersonOutline, S.devices, onDevices)
                GhostIcon(if (dark) Icons.Outlined.LightMode else Icons.Outlined.DarkMode, if (dark) S.lightTheme else S.darkTheme, onToggleTheme)
            },
        ) { HeaderTitle("Kaç Kaldı") }
        UpdateBanner(updater)
        SearchField(
            value = query,
            onChange = { viewModel.query.value = it },
            placeholder = S.searchPlaceholder,
            modifier = Modifier.fillMaxWidth().padding(start = 12.dp, end = 12.dp, top = 8.dp),
            onFocusChange = { searchFocused = it },
        )
        LazyColumn(
            Modifier.fillMaxSize(),
            contentPadding = WindowInsets.navigationBars.asPaddingValues(),
        ) {
            val current = results
            val showRecents = searchFocused && query.isBlank() && recents.isNotEmpty()
            when {
                showRecents -> recentsSection(recents, onClearRecents, { leaveSearch(); onStop(it) }, { code, dir -> leaveSearch(); onLine(code, dir) })
                query.isNotBlank() && current != null -> resultsSection(current, onStop, onLine)
                else -> {
                    if (query.isBlank() && alerts.isNotEmpty()) {
                        item { SectionTitle(S.yourAlerts) }
                        items(alerts, key = { "alert-${it.id}" }) { alert -> AlertRow(alert, onStop, onAlertEnabled, onAlertRemove) }
                    }
                    favoritesSection(viewModel.favorites, viewModel.announcements, viewModel::refreshFavorites, onStop, onLine)
                }
            }
            item { VersionFooter(updater.currentVersion) }
        }
    }
}

private fun LazyListScope.recentsSection(
    recents: List<Recent>, onClear: () -> Unit, onStop: (String) -> Unit, onLine: (String, String?) -> Unit,
) {
    item {
        Row(verticalAlignment = Alignment.Bottom) {
            Box(Modifier.weight(1f)) { SectionTitle(S.recent) }
            KitButton(S.clear, onClear, Modifier.padding(end = 8.dp), kind = ButtonKind.Ghost)
        }
    }
    items(recents, key = { "recent-${it.kind}-${it.code}-${it.direction}" }) { recent ->
        ItemRow(
            onClick = { if (recent.isStop) onStop(recent.code) else onLine(recent.code, recent.direction) },
            leading = {
                if (recent.isStop) Icon(Icons.Outlined.History, null, Modifier.size(18.dp), tint = Kit.colors.mutedForeground)
                else LineBadge(recent.code)
            },
            trailing = { Chevron() },
        ) {
            if (recent.isStop) StopName(recent.title, recent.district) else ItemTitle(recent.title)
        }
    }
}

private fun LazyListScope.favoritesSection(
    favorites: Load<FavoritesResponse>, announcements: List<AnnouncementView>, onRetry: () -> Unit, onStop: (String) -> Unit, onLine: (String, String?) -> Unit,
) {
    when (favorites) {
        Load.Loading -> item { RowsSkeleton() }
        is Load.Failed -> item { ErrorState(favorites.message, onRetry) }
        is Load.Ready -> {
            val (lines, stops) = favorites.value
            if (lines.isEmpty() && stops.isEmpty()) {
                item { EmptyState(S.emptyTitle, S.emptyText) }
                return
            }
            if (announcements.isNotEmpty()) {
                item { SectionTitle(S.announcementsTitle) }
                item { AnnouncementsCard(announcements) }
            }
            if (stops.isNotEmpty()) item { SectionTitle(S.yourStops) }
            items(stops, key = { "stop-${it.favorite.id}" }) { entry -> FavoriteStopRow(entry, onStop) }
            if (lines.isNotEmpty()) item { SectionTitle(S.yourLines) }
            items(lines, key = { "line-${it.favorite.id}" }) { entry -> FavoriteLineRow(entry, onLine) }
        }
    }
}

private fun LazyListScope.resultsSection(results: Load<SearchResults>, onStop: (String) -> Unit, onLine: (String, String?) -> Unit) {
    when (results) {
        Load.Loading -> item { RowsSkeleton(rows = 3) }
        is Load.Failed -> item { EmptyState(S.searchDown, results.message) }
        is Load.Ready -> {
            val (lines, stops) = results.value
            if (lines.isEmpty() && stops.isEmpty()) {
                item { EmptyState(S.nothingFound, S.nothingFoundText) }
                return
            }
            if (lines.isNotEmpty()) item { SectionTitle(S.lines) }
            items(lines, key = { "line-${it.lineCode}" }) { line ->
                val first = line.directions.firstOrNull()
                ItemRow(
                    onClick = { onLine(line.lineCode, null) },
                    leading = { LineBadge(line.lineCode) },
                    trailing = { Chevron() },
                ) { ItemTitle(first?.let { directionLabel(it.from, it.to) } ?: line.lineCode) }
            }
            if (stops.isNotEmpty()) item { SectionTitle(S.stops) }
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
    ) { ItemTitle(entry.favorite.label ?: direction?.let(::titleCase) ?: S.bothDirections) }
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
