package com.farukerata.kackaldi

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import com.farukerata.kackaldi.ui.KacKaldiRoot
import com.farukerata.kackaldi.ui.KacKaldiTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (savedInstanceState == null) receive(intent)
        enableEdgeToEdge()
        setContent {
            KacKaldiTheme { KacKaldiRoot() }
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
