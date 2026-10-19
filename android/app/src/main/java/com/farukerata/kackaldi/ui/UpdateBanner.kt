package com.farukerata.kackaldi.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.SystemUpdate
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.farukerata.kackaldi.update.AppUpdate
import com.farukerata.kackaldi.update.AppUpdater
import com.farukerata.kackaldi.update.UpdateState

@Composable
fun UpdateBanner(updater: AppUpdater, modifier: Modifier = Modifier) {
    val state by updater.state.collectAsStateWithLifecycle()
    val update = state.update ?: return
    Surface(
        modifier = modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
        shape = RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.secondaryContainer,
        contentColor = MaterialTheme.colorScheme.onSecondaryContainer,
    ) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Icon(Icons.Rounded.SystemUpdate, contentDescription = null)
                Text(title(state, update), style = MaterialTheme.typography.titleSmall)
            }
            Text(body(state), style = MaterialTheme.typography.bodyMedium)
            val downloading = state as? UpdateState.Downloading
            if (downloading != null) LinearProgressIndicator(progress = { downloading.progress }, modifier = Modifier.fillMaxWidth())
            Actions(state, updater)
        }
    }
}

@Composable
private fun Actions(state: UpdateState, updater: AppUpdater) {
    val action = when (state) {
        is UpdateState.Available -> "Update"
        is UpdateState.NeedsPermission -> "Open settings"
        is UpdateState.Failed -> "Try again"
        else -> return
    }
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.End)) {
        TextButton(onClick = updater::dismiss) { Text("Later") }
        Button(onClick = updater::start) { Text(action) }
    }
}

private fun title(state: UpdateState, update: AppUpdate): String = when (state) {
    is UpdateState.Downloading -> "Downloading ${update.versionName}… ${(state.progress * 100).toInt()}%"
    is UpdateState.Installing -> "Installing ${update.versionName}…"
    is UpdateState.Failed -> "Update didn’t finish"
    else -> "Kaç Kaldı ${update.versionName} is ready"
}

private fun body(state: UpdateState): String = when (state) {
    is UpdateState.Available -> "Download and install it now. Your saved stops stay as they are."
    is UpdateState.Downloading -> "You can keep using the app while it downloads."
    is UpdateState.NeedsPermission -> "Allow Kaç Kaldı to install updates, then come back here."
    is UpdateState.Installing -> "The app closes while it updates. Open it again afterwards."
    is UpdateState.Failed -> state.message
    UpdateState.None -> ""
}
