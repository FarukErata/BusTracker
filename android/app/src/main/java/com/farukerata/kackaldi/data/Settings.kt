package com.farukerata.kackaldi.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.intPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

private val Context.settingsData by preferencesDataStore(name = "settings")

class Settings(private val context: Context) {
    private val themeKey = stringPreferencesKey("theme")
    private val triggerKindKey = stringPreferencesKey("watch_trigger_kind")
    private val triggerValueKey = intPreferencesKey("watch_trigger_value")

    val lastTrigger: Flow<WatchTrigger> = context.settingsData.data.map { prefs ->
        val kind = TriggerKind.entries.firstOrNull { it.name == prefs[triggerKindKey] } ?: TriggerKind.Minutes
        WatchTrigger(kind, prefs[triggerValueKey] ?: 5)
    }

    suspend fun rememberTrigger(trigger: WatchTrigger) {
        if (trigger.atStopName != null) return
        context.settingsData.edit {
            it[triggerKindKey] = trigger.kind.name
            it[triggerValueKey] = trigger.value
        }
    }

    val darkTheme: Flow<Boolean?> = context.settingsData.data.map { prefs ->
        when (prefs[themeKey]) {
            "dark" -> true
            "light" -> false
            else -> null
        }
    }

    suspend fun setDarkTheme(dark: Boolean) {
        context.settingsData.edit { it[themeKey] = if (dark) "dark" else "light" }
    }
}
