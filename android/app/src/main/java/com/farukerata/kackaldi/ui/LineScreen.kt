package com.farukerata.kackaldi.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
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
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.LineDirection
import com.farukerata.kackaldi.data.LineStopsResponse
import kotlinx.coroutines.launch

class LineViewModel(private val api: KacKaldiApi, val code: String) : ViewModel() {
    var stops by mutableStateOf<Load<LineStopsResponse>>(Load.Loading)
        private set
    var direction by mutableIntStateOf(0)

    fun reload() {
        viewModelScope.launch { stops = load { api.lineStops(code) } }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LineScreen(viewModel: LineViewModel, onBack: () -> Unit, onStop: (String) -> Unit) {
    LaunchedEffect(viewModel.code) { if (viewModel.stops !is Load.Ready) viewModel.reload() }

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
                is Load.Ready -> {
                    val directions = state.value.directions
                    if (directions.size > 1) DirectionChooser(directions, viewModel.direction) { viewModel.direction = it }
                    directions.getOrNull(viewModel.direction)?.let { StopList(it, onStop) }
                }
            }
        }
    }
}

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
private fun StopList(direction: LineDirection, onStop: (String) -> Unit) {
    LazyColumn(Modifier.fillMaxSize()) {
        items(direction.stops, key = { "${direction.direction}-${it.seq}-${it.code}" }) { stop ->
            ListItem(
                leadingContent = { Text("${stop.seq}", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant) },
                headlineContent = { Text(titleCase(stop.name)) },
                supportingContent = stop.district.takeIf { it.isNotBlank() }?.let { { Text(titleCase(it)) } },
                modifier = Modifier.clickable { onStop(stop.code) },
            )
        }
    }
}
