package com.farukerata.kackaldi.ui

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.toRoute
import com.farukerata.kackaldi.KacKaldiApplication
import com.farukerata.kackaldi.data.BusAlert
import com.farukerata.kackaldi.data.Recent
import com.farukerata.kackaldi.i18n.S
import com.farukerata.kackaldi.watch.AlertScheduler
import com.farukerata.kackaldi.watch.WatchService
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable

@Serializable
data object HomeRoute

@Serializable
data class StopRoute(val code: String)

@Serializable
data class LineRoute(val code: String, val direction: String? = null, val stop: String? = null)

@Serializable
data object DevicesRoute

@Serializable
data object AlertsRoute

@Composable
fun KacKaldiRoot(dark: Boolean, onToggleTheme: () -> Unit) {
    val app = LocalContext.current.applicationContext as KacKaldiApplication
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(Unit) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) { app.updater.onAppResumed() }
    }
    val linkFromWeb by app.pendingLinkCode.collectAsStateWithLifecycle()
    val signedIn by remember { app.deviceStore.key.map { it != null } }.collectAsStateWithLifecycle(initialValue = null)
    val context = LocalContext.current
    LaunchedEffect(signedIn, linkFromWeb) {
        if (signedIn != true || linkFromWeb == null) return@LaunchedEffect
        app.pendingLinkCode.value = null
        Toast.makeText(context, S.alreadySignedIn, Toast.LENGTH_LONG).show()
    }
    when (signedIn) {
        null -> Box(Modifier.fillMaxSize().background(Kit.colors.background))
        false -> WelcomeScreen(app.api, linkFromWeb) { app.pendingLinkCode.value = null }
        true -> AppNavigation(app, dark, onToggleTheme)
    }
}

@Composable
private fun AppNavigation(app: KacKaldiApplication, dark: Boolean, onToggleTheme: () -> Unit) {
    val nav = rememberNavController()
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val alerts by app.alerts.alerts.collectAsStateWithLifecycle(initialValue = emptyList())
    val running by app.watch.collectAsStateWithLifecycle()
    val recents by app.recents.items.collectAsStateWithLifecycle(initialValue = emptyList())
    val setEnabled = { alert: BusAlert, enabled: Boolean -> scope.launch { AlertScheduler.save(context, app.alerts, alert.copy(enabled = enabled)) }; Unit }
    val remove = { alert: BusAlert -> scope.launch { AlertScheduler.remove(context, app.alerts, alert.id) }; Unit }
    val openStop by app.pendingStop.collectAsStateWithLifecycle()
    LaunchedEffect(openStop) {
        val code = openStop ?: return@LaunchedEffect
        app.pendingStop.value = null
        nav.navigate(StopRoute(code)) { launchSingleTop = true }
    }
    NavHost(nav, startDestination = HomeRoute) {
        composable<HomeRoute> {
            HomeScreen(
                viewModel = viewModel { HomeViewModel(app.api) },
                updater = app.updater,
                dark = dark,
                onToggleTheme = onToggleTheme,
                onStop = { nav.navigate(StopRoute(it)) },
                onLine = { code, direction -> nav.navigate(LineRoute(code, direction)) },
                onDevices = { nav.navigate(DevicesRoute) },
                onAlerts = { nav.navigate(AlertsRoute) },
                alertRunning = running != null,
                alerts = alerts,
                onAlertEnabled = setEnabled,
                onAlertRemove = remove,
                recents = recents,
                onClearRecents = { scope.launch { app.recents.clear() } },
            )
        }
        composable<AlertsRoute> {
            AlertsScreen(
                running = running,
                alerts = alerts,
                onBack = { nav.popBackStack() },
                onStop = { nav.navigate(StopRoute(it)) },
                onStopRunning = { WatchService.stop(context) },
                onEnabled = setEnabled,
                onRemove = remove,
            )
        }
        composable<DevicesRoute> {
            DevicesScreen(viewModel = viewModel { DevicesViewModel(app.api) }, onBack = { nav.popBackStack() })
        }
        composable<StopRoute> { entry ->
            val code = entry.toRoute<StopRoute>().code
            StopScreen(
                viewModel = viewModel { StopViewModel(app.api, app.stopFilters, code) },
                onBack = { nav.popBackStack() },
                onLine = { code, direction, stop -> nav.navigate(LineRoute(code, direction, stop)) },
            )
        }
        composable<LineRoute> { entry ->
            val route = entry.toRoute<LineRoute>()
            LineScreen(
                viewModel = viewModel { LineViewModel(app.api, route.code, route.direction, route.stop) },
                onBack = { nav.popBackStack() },
                onDirectionShown = { direction ->
                    val title = directionLabel(direction.stops.firstOrNull()?.name.orEmpty(), direction.name)
                    scope.launch { app.recents.remember(Recent(Recent.LINE, route.code, title, direction = direction.direction)) }
                },
            )
        }
    }
}
