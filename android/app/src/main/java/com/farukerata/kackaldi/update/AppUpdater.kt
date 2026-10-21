package com.farukerata.kackaldi.update

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.net.Uri
import android.os.Build
import android.provider.Settings
import com.farukerata.kackaldi.BuildConfig
import com.farukerata.kackaldi.i18n.S
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.engine.okhttp.OkHttp
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.client.request.prepareGet
import io.ktor.client.statement.bodyAsChannel
import io.ktor.http.HttpHeaders
import io.ktor.http.contentLength
import io.ktor.http.isSuccess
import io.ktor.serialization.kotlinx.json.json
import io.ktor.utils.io.jvm.javaio.toInputStream
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

data class AppUpdate(val versionName: String, val versionCode: Int, val apkUrl: String, val size: Long)

sealed interface UpdateState {
    val update: AppUpdate?

    data object None : UpdateState {
        override val update: AppUpdate? = null
    }

    data class Available(override val update: AppUpdate) : UpdateState
    data class Downloading(override val update: AppUpdate, val progress: Float) : UpdateState
    data class NeedsPermission(override val update: AppUpdate) : UpdateState
    data class Installing(override val update: AppUpdate) : UpdateState
    data class Failed(override val update: AppUpdate, val message: String) : UpdateState
}

@Serializable
private data class GitHubAsset(val name: String, @SerialName("browser_download_url") val url: String, val size: Long)

@Serializable
private data class GitHubRelease(@SerialName("tag_name") val tag: String, val assets: List<GitHubAsset> = emptyList())

class AppUpdater(private val context: Context) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private val http = HttpClient(OkHttp) {
        expectSuccess = false
        install(ContentNegotiation) { json(Json { ignoreUnknownKeys = true }) }
    }
    private val _state = MutableStateFlow<UpdateState>(UpdateState.None)
    private var lastCheck = 0L

    val state: StateFlow<UpdateState> = _state.asStateFlow()

    val currentVersion: String = BuildConfig.VERSION_NAME

    fun onAppResumed() {
        if (!continueAfterPermission()) checkIfDue()
    }

    private fun continueAfterPermission(): Boolean {
        val waiting = _state.value as? UpdateState.NeedsPermission ?: return false
        if (!context.packageManager.canRequestPackageInstalls()) return false
        _state.value = UpdateState.Available(waiting.update)
        start()
        return true
    }

    private fun checkIfDue() {
        if (BuildConfig.DEBUG || _state.value != UpdateState.None) return
        val now = System.currentTimeMillis()
        if (now - lastCheck < CHECK_EVERY_MILLIS) return
        lastCheck = now
        scope.launch {
            val update = runCatching { latest() }.getOrNull() ?: return@launch
            if (_state.value is UpdateState.None) _state.value = UpdateState.Available(update)
        }
    }

    fun dismiss() {
        _state.value = UpdateState.None
    }

    fun start() {
        val update = _state.value.update ?: return
        if (!context.packageManager.canRequestPackageInstalls()) {
            _state.value = UpdateState.NeedsPermission(update)
            context.startActivity(
                Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}"))
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            )
            return
        }
        scope.launch {
            try {
                val apk = download(update)
                _state.value = UpdateState.Installing(update)
                withContext(Dispatchers.IO) { install(apk) }
            } catch (e: Exception) {
                _state.value = UpdateState.Failed(update, S.downloadFailed)
            }
        }
    }

    internal fun onInstallFinished(status: Int, message: String?) {
        val update = _state.value.update ?: return
        if (status != PackageInstaller.STATUS_SUCCESS) {
            _state.value = UpdateState.Failed(update, message?.takeIf { it.isNotBlank() } ?: S.notInstalled)
        }
    }

    private suspend fun latest(): AppUpdate? {
        val response = http.get("https://api.github.com/repos/${BuildConfig.UPDATE_REPO}/releases/latest") {
            header(HttpHeaders.Accept, "application/vnd.github+json")
        }
        if (!response.status.isSuccess()) return null
        val release = response.body<GitHubRelease>()
        val name = release.tag.removePrefix(TAG_PREFIX)
        val code = versionCode(name) ?: return null
        val apk = release.assets.firstOrNull { it.name == APK_NAME } ?: return null
        return AppUpdate(name, code, apk.url, apk.size).takeIf { code > BuildConfig.VERSION_CODE }
    }

    private suspend fun download(update: AppUpdate): File = withContext(Dispatchers.IO) {
        val folder = File(context.cacheDir, "updates").apply { mkdirs() }
        folder.listFiles()?.forEach { it.delete() }
        val file = File(folder, "kackaldi-${update.versionName}.apk")
        http.prepareGet(update.apkUrl).execute { response ->
            if (!response.status.isSuccess()) throw IOException("Download failed (${response.status.value})")
            val total = (response.contentLength() ?: update.size).coerceAtLeast(1)
            response.bodyAsChannel().toInputStream().use { input ->
                file.outputStream().use { output ->
                    copyWithProgress(input, output, total) { _state.value = UpdateState.Downloading(update, it) }
                }
            }
        }
        file
    }

    private fun copyWithProgress(input: InputStream, output: OutputStream, total: Long, onProgress: (Float) -> Unit) {
        val buffer = ByteArray(64 * 1024)
        var copied = 0L
        var reported = -1L
        while (true) {
            val read = input.read(buffer)
            if (read < 0) return
            output.write(buffer, 0, read)
            copied += read
            val percent = copied * 100 / total
            if (percent == reported) continue
            reported = percent
            onProgress((copied.toFloat() / total).coerceIn(0f, 1f))
        }
    }

    private fun install(apk: File) {
        val installer = context.packageManager.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
            setAppPackageName(context.packageName)
            setSize(apk.length())
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
            }
        }
        val sessionId = installer.createSession(params)
        installer.openSession(sessionId).use { session ->
            session.openWrite(APK_NAME, 0, apk.length()).use { output ->
                apk.inputStream().use { it.copyTo(output) }
                session.fsync(output)
            }
            val intent = Intent(context, UpdateReceiver::class.java)
            val pending = PendingIntent.getBroadcast(context, sessionId, intent, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE)
            session.commit(pending.intentSender)
        }
    }

    private companion object {
        const val TAG_PREFIX = "android-v"
        const val APK_NAME = "kackaldi.apk"
        const val CHECK_EVERY_MILLIS = 6 * 60 * 60 * 1000L

        fun versionCode(name: String): Int? {
            val parts = name.split('.').map { it.toIntOrNull() ?: return null }
            if (parts.size != 3) return null
            return parts[0] * 10000 + parts[1] * 100 + parts[2]
        }
    }
}
