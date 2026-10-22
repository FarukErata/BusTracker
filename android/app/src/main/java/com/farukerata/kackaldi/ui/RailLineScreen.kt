package com.farukerata.kackaldi.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.StarOutline
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.viewModelScope
import com.farukerata.kackaldi.data.AnnouncementView
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.RailLineResponse
import com.farukerata.kackaldi.data.RailStationResponse
import com.farukerata.kackaldi.i18n.S
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

private const val STATION_REFRESH_MILLIS = 60_000L

class RailLineViewModel(private val api: KacKaldiApi, val id: Int, initialStation: Int?) : ViewModel() {
    var line by mutableStateOf<Load<RailLineResponse>>(Load.Loading)
        private set
    var picked by mutableStateOf(initialStation)
        private set
    var station by mutableStateOf<Load<RailStationResponse>?>(null)
        private set
    var favoriteId by mutableStateOf<Long?>(null)
        private set

    suspend fun load() {
        if (line !is Load.Ready) line = load { api.railLine(id) }
    }

    suspend fun refreshStation() {
        val current = picked ?: return
        val next = load { api.railStation(current) }
        if (picked != current) return
        if (next is Load.Ready || station !is Load.Ready) station = next
    }

    fun pick(stationId: Int?) {
        picked = stationId
        station = if (stationId == null) null else Load.Loading
        favoriteId = null
        viewModelScope.launch {
            refreshStation()
            loadFavorite()
        }
    }

    fun loadFavorite() {
        val current = picked ?: return
        viewModelScope.launch {
            val favorites = load { api.favorites() }.valueOrNull ?: return@launch
            if (picked == current) favoriteId = favorites.stations.firstOrNull { it.favorite.stationId == current }?.favorite?.id
        }
    }

    fun toggleFavorite() {
        val current = picked ?: return
        viewModelScope.launch {
            val saved = favoriteId
            load { if (saved == null) api.addFavoriteStation(current) else api.removeFavoriteStation(saved) }
            loadFavorite()
        }
    }
}

@Composable
fun RailLineScreen(viewModel: RailLineViewModel, onBack: () -> Unit) {
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(viewModel.id) {
        viewModel.load()
        viewModel.loadFavorite()
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                viewModel.refreshStation()
                delay(STATION_REFRESH_MILLIS)
            }
        }
    }
    val colors = Kit.colors
    val data = viewModel.line.valueOrNull
    val picked = viewModel.picked
    val pickedStation = data?.stations?.firstOrNull { it.id == picked }
    val list = rememberLazyListState()
    val scope = rememberCoroutineScope()
    val pick = { id: Int? ->
        viewModel.pick(id)
        if (id != null) scope.launch { list.animateScrollToItem(0) }
        Unit
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
                if (picked != null) {
                    val saved = viewModel.favoriteId != null
                    GhostIcon(
                        if (saved) Icons.Rounded.Star else Icons.Outlined.StarOutline,
                        if (saved) S.unsaveStation else S.saveStation,
                        viewModel::toggleFavorite,
                        tint = if (saved) colors.star else colors.foreground,
                    )
                }
            },
        ) {
            Row(Modifier.padding(horizontal = 4.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (data != null) {
                    RailBadge(data.line.name, data.line.color)
                    Text(
                        pickedStation?.name ?: data.line.description,
                        style = Kit.base.copy(fontWeight = FontWeight.SemiBold, letterSpacing = Kit.tight),
                        color = colors.foreground,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                } else {
                    Box(Modifier.fillMaxWidth(0.6f).height(18.dp).background(colors.secondary, RowShape))
                }
            }
        }
        Box(
            Modifier
                .padding(start = 12.dp, end = 12.dp, top = 8.dp)
                .fillMaxWidth()
                .height(220.dp)
                .clip(CardShape)
                .background(colors.secondary)
                .border(1.dp, colors.border, CardShape),
        ) {
            val points = data?.stations.orEmpty().mapNotNull { s -> if (s.lat == null || s.lng == null) null else listOf(s.lat, s.lng) }
            if (data != null && points.size >= 2) {
                TransitMap(
                    MapScene(
                        frame = MapFrame.Fit("rail-line-${data.line.id}", points),
                        rail = railMarkers(data.stations, except = picked),
                        railRoute = points,
                        railRouteColor = data.line.color,
                        pinned = pickedStation?.let { s -> if (s.lat == null || s.lng == null) null else MapStop("rail-${s.id}", s.lat, s.lng) },
                        pinColor = data.line.color,
                    ),
                    Modifier.fillMaxSize(),
                    onRail = { pick(it) },
                )
            }
        }
        when (val state = viewModel.line) {
            Load.Loading -> RowsSkeleton(rows = 4)
            is Load.Failed -> ErrorState(state.message, { })
            is Load.Ready -> {
                val notices = state.value.notices.map { AnnouncementView(line = it.line, kind = "notice", message = it.text) }
                LazyColumn(Modifier.fillMaxSize(), state = list, contentPadding = WindowInsets.navigationBars.asPaddingValues()) {
                    if (notices.isNotEmpty()) item { AnnouncementsCard(notices, showLines = false) }
                    if (picked != null) item(key = "station-$picked") {
                        when (val details = viewModel.station) {
                            null, Load.Loading -> RowsSkeleton(rows = 2)
                            is Load.Failed -> ErrorState(details.message, { viewModel.pick(picked) })
                            is Load.Ready -> StationDetails(details.value)
                        }
                    }
                    item { SectionTitle(S.stations) }
                    itemsIndexed(state.value.stations, key = { _, s -> "station-row-${s.id}" }) { index, station ->
                        val selected = station.id == picked
                        Row(
                            Modifier
                                .fillMaxWidth()
                                .pressable({ pick(if (selected) null else station.id) })
                                .background(if (selected) colors.secondary.copy(alpha = 0.6f) else Color.Transparent)
                                .padding(horizontal = 10.dp, vertical = 10.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                        ) {
                            Text("${index + 1}", Modifier.width(24.dp), style = Kit.xs, color = colors.mutedForeground, textAlign = TextAlign.End)
                            Text(
                                station.name,
                                Modifier.weight(1f),
                                style = Kit.sm.copy(fontWeight = if (selected) FontWeight.Medium else FontWeight.Normal),
                                color = colors.foreground,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                        }
                    }
                }
            }
        }
    }
}
