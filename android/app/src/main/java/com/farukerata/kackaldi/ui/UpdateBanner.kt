package com.farukerata.kackaldi.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.SystemUpdate
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.farukerata.kackaldi.update.AppUpdate
import com.farukerata.kackaldi.update.AppUpdater
import com.farukerata.kackaldi.update.UpdateState

@Composable
fun UpdateBanner(updater: AppUpdater, modifier: Modifier = Modifier) {
    val state by updater.state.collectAsStateWithLifecycle()
    val update = state.update ?: return
    val colors = Kit.colors
    Column(
        modifier
            .fillMaxWidth()
            .padding(start = 12.dp, end = 12.dp, top = 8.dp)
            .border(1.dp, colors.border, CardShape)
            .background(colors.card, CardShape)
            .padding(12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Icon(Icons.Outlined.SystemUpdate, null, Modifier.size(16.dp), tint = colors.foreground)
            Text(title(state, update), style = Kit.sm.copy(fontWeight = FontWeight.Medium), color = colors.foreground)
        }
        Text(body(state), style = Kit.sm, color = colors.mutedForeground)
        val downloading = state as? UpdateState.Downloading
        if (downloading != null) {
            Box(Modifier.fillMaxWidth().height(4.dp).clip(CircleShape).background(colors.secondary)) {
                Box(Modifier.fillMaxWidth(downloading.progress).height(4.dp).clip(CircleShape).background(colors.primary))
            }
        }
        Actions(state, updater)
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
        KitButton("Later", updater::dismiss, kind = ButtonKind.Ghost)
        KitButton(action, updater::start)
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
