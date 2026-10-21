package com.farukerata.kackaldi.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

private val Context.recentsData by preferencesDataStore(name = "recents")

@Serializable
data class Recent(val kind: String, val code: String, val title: String, val district: String = "", val direction: String? = null) {
    val isStop: Boolean get() = kind == STOP

    fun sameAs(other: Recent): Boolean = kind == other.kind && code == other.code && (isStop || direction == other.direction)

    companion object {
        const val STOP = "stop"
        const val LINE = "line"
    }
}

class Recents(private val context: Context) {
    private val key = stringPreferencesKey("items")
    private val json = Json { ignoreUnknownKeys = true }

    val items: Flow<List<Recent>> = context.recentsData.data.map { decode(it[key]) }

    suspend fun remember(item: Recent) {
        context.recentsData.edit { prefs ->
            val current = decode(prefs[key])
            prefs[key] = json.encodeToString((listOf(item) + current.filterNot(item::sameAs)).take(MAX))
        }
    }

    suspend fun clear() {
        context.recentsData.edit { it.remove(key) }
    }

    private fun decode(value: String?): List<Recent> =
        value?.let { runCatching { json.decodeFromString<List<Recent>>(it) }.getOrNull() }.orEmpty()

    private companion object {
        const val MAX = 5
    }
}
