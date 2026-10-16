package com.farukerata.kackaldi.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map

private val Context.deviceData by preferencesDataStore(name = "device")

class DeviceStore(private val context: Context) {
    private val keyName = stringPreferencesKey("device_key")

    val key: Flow<String?> = context.deviceData.data.map { it[keyName] }

    suspend fun current(): String? = key.first()

    suspend fun save(key: String) {
        context.deviceData.edit { it[keyName] = key }
    }

    suspend fun clear() {
        context.deviceData.edit { it.remove(keyName) }
    }
}
