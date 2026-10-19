package com.farukerata.kackaldi

import android.app.Application
import com.farukerata.kackaldi.data.DeviceStore
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.StopFilters
import com.farukerata.kackaldi.update.AppUpdater
import kotlinx.coroutines.flow.MutableStateFlow
import org.maplibre.android.MapLibre

class KacKaldiApplication : Application() {
    val deviceStore by lazy { DeviceStore(this) }
    val api by lazy { KacKaldiApi(deviceStore) }
    val updater by lazy { AppUpdater(this) }
    val stopFilters by lazy { StopFilters(this) }
    val pendingLinkCode = MutableStateFlow<String?>(null)

    override fun onCreate() {
        super.onCreate()
        MapLibre.getInstance(this)
    }
}
