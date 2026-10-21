package com.farukerata.kackaldi

import android.app.Application
import com.farukerata.kackaldi.data.AlertStore
import com.farukerata.kackaldi.data.DeviceStore
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.Recents
import com.farukerata.kackaldi.data.Settings
import com.farukerata.kackaldi.data.StopFilters
import com.farukerata.kackaldi.i18n.L10n
import com.farukerata.kackaldi.data.Watch
import com.farukerata.kackaldi.update.AppUpdater
import kotlinx.coroutines.flow.MutableStateFlow
import org.maplibre.android.MapLibre

class KacKaldiApplication : Application() {
    val deviceStore by lazy { DeviceStore(this) }
    val api by lazy { KacKaldiApi(deviceStore) }
    val updater by lazy { AppUpdater(this) }
    val stopFilters by lazy { StopFilters(this) }
    val settings by lazy { Settings(this) }
    val alerts by lazy { AlertStore(this) }
    val recents by lazy { Recents(this) }
    val pendingLinkCode = MutableStateFlow<String?>(null)
    val pendingStop = MutableStateFlow<String?>(null)
    val watch = MutableStateFlow<Watch?>(null)

    override fun onCreate() {
        super.onCreate()
        L10n.init(this)
        MapLibre.getInstance(this)
    }
}
