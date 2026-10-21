package com.farukerata.kackaldi.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.asPaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.DeleteOutline
import androidx.compose.material.icons.outlined.NotificationsActive
import androidx.compose.material.icons.outlined.NotificationsNone
import androidx.compose.material3.Icon
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.farukerata.kackaldi.data.BusAlert
import com.farukerata.kackaldi.data.Watch
import com.farukerata.kackaldi.i18n.S

@Composable
fun AlertsScreen(
    running: Watch?,
    alerts: List<BusAlert>,
    onBack: () -> Unit,
    onStop: (String) -> Unit,
    onStopRunning: () -> Unit,
    onEnabled: (BusAlert, Boolean) -> Unit,
    onRemove: (BusAlert) -> Unit,
) {
    Column(
        Modifier
            .fillMaxSize()
            .background(Kit.colors.background)
            .windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal)),
    ) {
        Header(onBack = onBack) { Box(Modifier.padding(horizontal = 4.dp)) { HeaderTitle(S.alertsTitle) } }
        LazyColumn(Modifier.fillMaxSize(), contentPadding = WindowInsets.navigationBars.asPaddingValues()) {
            if (running == null && alerts.isEmpty()) {
                item { EmptyState(S.noAlerts, S.noAlertsText) }
            }
            if (running != null) {
                item { SectionTitle(S.runningNow) }
                item { RunningRow(running, onStop, onStopRunning) }
            }
            if (alerts.isNotEmpty()) item { SectionTitle(S.saved) }
            items(alerts, key = { it.id }) { alert -> AlertRow(alert, onStop, onEnabled, onRemove) }
        }
    }
}

@Composable
private fun RunningRow(watch: Watch, onStop: (String) -> Unit, onStopRunning: () -> Unit) {
    ItemRow(
        onClick = { onStop(watch.stopCode) },
        leading = { Icon(Icons.Outlined.NotificationsActive, null, Modifier.size(18.dp), tint = Kit.colors.foreground) },
        trailing = { KitButton(S.stop, onStopRunning, kind = ButtonKind.Ghost) },
    ) {
        ItemTitle("${watch.lineNames} · ${titleCase(watch.stopName)}")
        ItemDescription(S.alertWhen(triggerSummary(watch.trigger)))
    }
}

@Composable
fun AlertRow(alert: BusAlert, onStop: (String) -> Unit, onEnabled: (BusAlert, Boolean) -> Unit, onRemove: (BusAlert) -> Unit) {
    val colors = Kit.colors
    val lines = alert.lines.map { it.line }.distinct().joinToString(" / ")
    ItemRow(
        onClick = { onStop(alert.stopCode) },
        leading = { Icon(Icons.Outlined.NotificationsNone, null, Modifier.size(18.dp), tint = colors.mutedForeground) },
        trailing = {
            Switch(
                checked = alert.enabled,
                onCheckedChange = { onEnabled(alert, it) },
                colors = SwitchDefaults.colors(
                    checkedThumbColor = colors.primaryForeground,
                    checkedTrackColor = colors.primary,
                    uncheckedThumbColor = colors.mutedForeground,
                    uncheckedTrackColor = colors.secondary,
                    uncheckedBorderColor = colors.border,
                ),
            )
            GhostIcon(Icons.Outlined.DeleteOutline, S.deleteAlert, { onRemove(alert) })
        },
    ) {
        ItemTitle("$lines · ${titleCase(alert.stopName)}")
        ItemDescription(S.readyAtRepeat(alert.readyAt, S.repeat(alert.days), triggerSummary(alert.trigger)))
    }
}
