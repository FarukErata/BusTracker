package com.farukerata.kackaldi.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringSetPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

private val Context.stopFilterData by preferencesDataStore(name = "stop_filters")

class StopFilters(private val context: Context) {
    fun lines(stopCode: String): Flow<Set<String>> = context.stopFilterData.data.map { it[key(stopCode)].orEmpty() }

    suspend fun save(stopCode: String, lines: Set<String>) {
        context.stopFilterData.edit { if (lines.isEmpty()) it.remove(key(stopCode)) else it[key(stopCode)] = lines }
    }

    private fun key(stopCode: String) = stringSetPreferencesKey("stop_$stopCode")
}
