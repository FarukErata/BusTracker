package com.farukerata.kackaldi.i18n

import android.content.Context
import android.content.SharedPreferences
import androidx.compose.runtime.mutableStateOf
import java.util.Locale
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

enum class Lang(val code: String) { TR("tr"), EN("en") }

object L10n {
    private const val KEY = "lang"
    private var prefs: SharedPreferences? = null
    private val current = MutableStateFlow(deviceLang())
    private val shown = mutableStateOf(current.value)

    val lang: StateFlow<Lang> = current.asStateFlow()

    val strings: Strings get() = if (shown.value == Lang.TR) Tr else En

    fun init(context: Context) {
        val store = context.getSharedPreferences(KEY, Context.MODE_PRIVATE)
        prefs = store
        current.value = Lang.entries.firstOrNull { it.code == store.getString(KEY, null) } ?: deviceLang()
        shown.value = current.value
    }

    fun set(lang: Lang) {
        current.value = lang
        shown.value = lang
        prefs?.edit()?.putString(KEY, lang.code)?.apply()
    }

    private fun deviceLang(): Lang = if (Locale.getDefault().language == "tr") Lang.TR else Lang.EN
}

val S: Strings get() = L10n.strings
