package com.farukerata.kackaldi.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.toRoute
import com.farukerata.kackaldi.KacKaldiApplication
import kotlinx.coroutines.flow.map
import kotlinx.serialization.Serializable

@Serializable
data object HomeRoute

@Serializable
data class StopRoute(val code: String)

@Serializable
data class LineRoute(val code: String)

@Composable
fun KacKaldiRoot() {
    val app = LocalContext.current.applicationContext as KacKaldiApplication
    val signedIn by remember { app.deviceStore.key.map { it != null } }.collectAsStateWithLifecycle(initialValue = null)
    when (signedIn) {
        null -> Box(Modifier.fillMaxSize())
        false -> WelcomeScreen(app.api)
        true -> AppNavigation(app)
    }
}

@Composable
private fun AppNavigation(app: KacKaldiApplication) {
    val nav = rememberNavController()
    NavHost(nav, startDestination = HomeRoute) {
        composable<HomeRoute> {
            HomeScreen(
                viewModel = viewModel { HomeViewModel(app.api) },
                onStop = { nav.navigate(StopRoute(it)) },
                onLine = { nav.navigate(LineRoute(it)) },
            )
        }
        composable<StopRoute> { entry ->
            val code = entry.toRoute<StopRoute>().code
            StopScreen(
                viewModel = viewModel { StopViewModel(app.api, code) },
                onBack = { nav.popBackStack() },
                onLine = { nav.navigate(LineRoute(it)) },
            )
        }
        composable<LineRoute> { entry ->
            val code = entry.toRoute<LineRoute>().code
            LineScreen(
                viewModel = viewModel { LineViewModel(app.api, code) },
                onBack = { nav.popBackStack() },
                onStop = { nav.navigate(StopRoute(it)) },
            )
        }
    }
}
