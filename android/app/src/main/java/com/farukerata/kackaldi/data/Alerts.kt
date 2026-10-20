package com.farukerata.kackaldi.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.serialization.json.Json

private val Context.alertsData by preferencesDataStore(name = "alerts")

class AlertStore(private val context: Context) {
    private val key = stringPreferencesKey("alerts")
    private val json = Json { ignoreUnknownKeys = true }

    val alerts: Flow<List<BusAlert>> = context.alertsData.data.map { prefs ->
        prefs[key]?.let { runCatching { json.decodeFromString<List<BusAlert>>(it) }.getOrNull() }.orEmpty()
    }

    suspend fun all(): List<BusAlert> = alerts.first()

    suspend fun find(id: String): BusAlert? = all().firstOrNull { it.id == id }

    suspend fun save(alert: BusAlert) = update { list -> list.filterNot { it.id == alert.id } + alert }

    suspend fun remove(id: String) = update { list -> list.filterNot { it.id == id } }

    private suspend fun update(change: (List<BusAlert>) -> List<BusAlert>) {
        context.alertsData.edit { prefs ->
            val current = prefs[key]?.let { runCatching { json.decodeFromString<List<BusAlert>>(it) }.getOrNull() }.orEmpty()
            prefs[key] = json.encodeToString(change(current))
        }
    }
}
