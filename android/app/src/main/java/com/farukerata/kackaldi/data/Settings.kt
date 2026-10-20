package com.farukerata.kackaldi.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

private val Context.settingsData by preferencesDataStore(name = "settings")

class Settings(private val context: Context) {
    private val themeKey = stringPreferencesKey("theme")

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
