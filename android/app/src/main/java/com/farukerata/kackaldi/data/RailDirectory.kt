package com.farukerata.kackaldi.data

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

class RailDirectory(private val api: KacKaldiApi) {
    private val loading = Mutex()
    private val current = MutableStateFlow<List<RailStationView>>(emptyList())

    val stations: StateFlow<List<RailStationView>> = current.asStateFlow()

    suspend fun ensureLoaded() {
        if (current.value.isNotEmpty()) return
        loading.withLock {
            if (current.value.isNotEmpty()) return
            runCatching { api.railStations() }.getOrNull()?.let { current.value = it }
        }
    }
}
