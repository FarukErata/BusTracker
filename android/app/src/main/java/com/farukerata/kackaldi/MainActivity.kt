package com.farukerata.kackaldi

import android.content.Intent
import android.graphics.Color
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.farukerata.kackaldi.ui.KacKaldiRoot
import com.farukerata.kackaldi.ui.KacKaldiTheme
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (savedInstanceState == null) receive(intent)
        val app = application as KacKaldiApplication
        setContent {
            val chosen by app.settings.darkTheme.collectAsStateWithLifecycle(initialValue = null)
            val dark = chosen ?: isSystemInDarkTheme()
            val scope = rememberCoroutineScope()
            DisposableEffect(dark) {
                val bars = SystemBarStyle.auto(Color.TRANSPARENT, Color.TRANSPARENT) { dark }
                enableEdgeToEdge(statusBarStyle = bars, navigationBarStyle = bars)
                onDispose {}
            }
            KacKaldiTheme(dark) {
                KacKaldiRoot(dark) { scope.launch { app.settings.setDarkTheme(!dark) } }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        receive(intent)
    }

    private fun receive(intent: Intent?) {
        val data = intent?.takeIf { it.action == Intent.ACTION_VIEW }?.data ?: return
        val code = when (data.scheme) {
            "kackaldi" -> data.pathSegments.firstOrNull()
            else -> data.pathSegments.takeIf { it.firstOrNull() == "join" }?.getOrNull(1)
        }
        if (!code.isNullOrBlank()) (application as KacKaldiApplication).pendingLinkCode.value = code
    }
}
