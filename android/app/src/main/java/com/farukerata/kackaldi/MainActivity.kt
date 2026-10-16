package com.farukerata.kackaldi

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import com.farukerata.kackaldi.ui.KacKaldiRoot
import com.farukerata.kackaldi.ui.KacKaldiTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            KacKaldiTheme { KacKaldiRoot() }
        }
    }
}
