package com.farukerata.kackaldi.ui

import com.farukerata.kackaldi.data.ApiException
import com.farukerata.kackaldi.i18n.S
import java.io.IOException
import kotlinx.coroutines.CancellationException

sealed interface Load<out T> {
    data object Loading : Load<Nothing>
    data class Ready<T>(val value: T) : Load<T>
    data class Failed(val message: String) : Load<Nothing>
}

suspend fun <T> load(block: suspend () -> T): Load<T> =
    try {
        Load.Ready(block())
    } catch (e: CancellationException) {
        throw e
    } catch (e: Exception) {
        Load.Failed(friendlyError(e))
    }

fun friendlyError(error: Throwable): String = when {
    error is ApiException -> error.message ?: S.somethingWrong
    error.causes().any { it.javaClass.simpleName.contains("Timeout") } -> S.serverSlow
    error.causes().any { it is IOException } -> S.noServer
    else -> S.somethingWrong
}

private fun Throwable.causes(): Sequence<Throwable> = generateSequence(this) { it.cause?.takeIf { cause -> cause !== it } }

val <T> Load<T>.valueOrNull: T? get() = (this as? Load.Ready)?.value
