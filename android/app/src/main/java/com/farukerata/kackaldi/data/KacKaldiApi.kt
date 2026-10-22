package com.farukerata.kackaldi.data

import android.os.Build
import com.farukerata.kackaldi.BuildConfig
import com.farukerata.kackaldi.i18n.S
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.engine.okhttp.OkHttp
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.plugins.defaultRequest
import io.ktor.client.request.HttpRequestBuilder
import io.ktor.client.request.bearerAuth
import io.ktor.client.request.delete
import io.ktor.client.request.get
import io.ktor.client.request.parameter
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.http.ContentType
import io.ktor.http.HttpStatusCode
import io.ktor.http.contentType
import io.ktor.http.isSuccess
import io.ktor.serialization.kotlinx.json.json
import java.util.concurrent.TimeUnit
import kotlinx.serialization.json.Json

class ApiException(val status: Int, message: String) : Exception(message)

class KacKaldiApi(private val store: DeviceStore) {
    private val json = Json {
        ignoreUnknownKeys = true
        explicitNulls = false
    }

    private val http = HttpClient(OkHttp) {
        engine {
            config {
                connectTimeout(15, TimeUnit.SECONDS)
                readTimeout(30, TimeUnit.SECONDS)
            }
        }
        expectSuccess = false
        install(ContentNegotiation) { json(json) }
        defaultRequest {
            url(BuildConfig.SERVER_URL.trimEnd('/') + "/")
            contentType(ContentType.Application.Json)
        }
    }

    suspend fun joinWithInvite(invite: String) = register(RegisterRequest(PLATFORM, deviceName(), invite = invite.trim()))

    suspend fun joinWithLinkCode(code: String) = register(RegisterRequest(PLATFORM, deviceName(), linkCode = code))

    suspend fun favorites(): FavoritesResponse = authorized { auth -> get("api/favorites") { auth() } }

    suspend fun searchStops(query: String): List<StopSearchResult> = authorized { auth -> get("api/stops") { auth(); parameter("q", query) } }

    suspend fun searchLines(query: String): List<LineSummary> = authorized { auth -> get("api/lines") { auth(); parameter("q", query) } }

    suspend fun stopArrivals(code: String, lines: Set<String> = emptySet()): StopArrivalsView = authorized { auth ->
        get("api/stops/$code/arrivals") {
            auth()
            if (lines.isNotEmpty()) parameter("lines", lines.sorted().joinToString(","))
        }
    }

    suspend fun lineStops(code: String): LineStopsResponse = authorized { auth -> get("api/lines/$code/stops") { auth() } }

    suspend fun lineShapes(code: String): LineShapesResponse = authorized { auth -> get("api/lines/$code/shapes") { auth() } }

    suspend fun lineLive(code: String): LineLiveResponse = authorized { auth -> get("api/lines/$code/live") { auth() } }

    suspend fun lineArrivals(code: String, stop: String, direction: String): LineArrivalsResponse = authorized { auth ->
        get("api/lines/$code/arrivals") {
            auth()
            parameter("stop", stop)
            parameter("dir", direction)
        }
    }

    suspend fun timetable(code: String, day: String?): TimetableResponse = authorized { auth ->
        get("api/lines/$code/timetable") {
            auth()
            if (day != null) parameter("day", day)
        }
    }

    suspend fun railSearch(query: String): List<RailStationView> =
        authorized { auth -> get("api/rail/stations") { auth(); parameter("q", query) } }

    suspend fun railLineSearch(query: String): List<RailLineView> =
        authorized { auth -> get("api/rail/lines") { auth(); parameter("q", query) } }

    suspend fun railLine(id: Int): RailLineResponse = authorized { auth -> get("api/rail/lines/$id") { auth() } }

    suspend fun railStations(): List<RailStationView> = authorized { auth -> get("api/rail/stations/all") { auth() } }

    suspend fun railStation(id: Int): RailStationResponse = authorized { auth -> get("api/rail/stations/$id") { auth() } }

    suspend fun addFavoriteStation(id: Int): Unit =
        authorized { auth -> post("api/favorites/stations") { auth(); setBody(AddFavoriteStationRequest(id)) } }

    suspend fun removeFavoriteStation(id: Long): Unit = authorized { auth -> delete("api/favorites/stations/$id") { auth() } }

    suspend fun busPath(code: String, bus: String, stop: String): BusPathResponse = authorized { auth ->
        get("api/lines/$code/path") {
            auth()
            parameter("bus", bus)
            parameter("stop", stop)
        }
    }

    suspend fun addFavoriteLine(code: String, direction: String?): Unit =
        authorized { auth -> post("api/favorites/lines") { auth(); setBody(AddFavoriteLineRequest(code, direction)) } }

    suspend fun removeFavoriteLine(id: Long): Unit = authorized { auth -> delete("api/favorites/lines/$id") { auth() } }

    suspend fun me(): MeResponse = authorized { auth -> get("api/me") { auth() } }

    suspend fun createLinkCode(): LinkCodeResponse = authorized { auth -> post("api/me/link-codes") { auth() } }

    suspend fun removeDevice(id: Long): Unit = authorized { auth -> delete("api/me/devices/$id") { auth() } }

    suspend fun signOutHere() = store.clear()

    suspend fun addFavoriteStop(code: String): Unit = authorized { auth -> post("api/favorites/stops") { auth(); setBody(AddFavoriteStopRequest(code)) } }

    suspend fun removeFavoriteStop(id: Long): Unit = authorized { auth -> delete("api/favorites/stops/$id") { auth() } }

    private suspend fun register(request: RegisterRequest) {
        val response = http.post("api/devices") { setBody(request) }
        if (!response.status.isSuccess()) throw response.toException()
        store.save(response.body<RegisterResponse>().key)
    }

    private suspend inline fun <reified T> authorized(
        send: HttpClient.(HttpRequestBuilder.() -> Unit) -> HttpResponse,
    ): T {
        val key = store.current() ?: throw ApiException(HttpStatusCode.Unauthorized.value, S.notSignedIn)
        val response = http.send { bearerAuth(key) }
        if (response.status == HttpStatusCode.Unauthorized) store.clear()
        if (!response.status.isSuccess()) throw response.toException()
        return if (T::class == Unit::class) Unit as T else response.body()
    }

    private suspend fun HttpResponse.toException(): ApiException {
        val detail = runCatching { body<Problem>().detail }.getOrNull()
        return ApiException(status.value, detail ?: if (status.value in GATEWAY_ERRORS) S.serverSlow else S.requestFailed(status.value))
    }

    private fun deviceName() = listOf(Build.MANUFACTURER, Build.MODEL).distinct().joinToString(" ").trim().ifEmpty { S.androidPhone }

    private companion object {
        const val PLATFORM = "android"
        val GATEWAY_ERRORS = 502..504
    }
}
