package com.farukerata.kackaldi.data

import kotlinx.serialization.Serializable

@Serializable
data class StopInfo(
    val code: String,
    val name: String,
    val district: String = "",
    val lat: Double,
    val lng: Double,
    val towards: String? = null,
)

@Serializable
data class StopSearchResult(val stop: StopInfo, val lines: List<String>)

@Serializable
data class LineEnds(val direction: String, val from: String, val to: String)

@Serializable
data class LineSummary(val lineCode: String, val directions: List<LineEnds>)

@Serializable
data class ArrivalView(
    val doorNo: String,
    val routeCode: String,
    val variant: String,
    val stopsAway: Int,
    val distanceMeters: Double,
    val etaSeconds: Int? = null,
    val notDeparted: Boolean,
    val ageSeconds: Int,
    val lastStopSeq: Int? = null,
    val lastStopName: String? = null,
)

@Serializable
data class StopArrivalView(val line: String, val direction: String, val directionName: String, val bus: ArrivalView)

@Serializable
data class Freshness(val fetchedAt: String, val ageSeconds: Int, val stale: Boolean)

@Serializable
data class LineStatusView(
    val line: String,
    val direction: String,
    val directionName: String,
    val data: Freshness? = null,
    val error: String? = null,
    val retryInSeconds: Int? = null,
)

@Serializable
data class StopArrivalsView(val stop: StopInfo, val arrivals: List<StopArrivalView>, val lines: List<LineStatusView>)

@Serializable
data class LineStop(val seq: Int, val code: String, val name: String, val district: String = "", val lat: Double = 0.0, val lng: Double = 0.0)

@Serializable
data class LineDirection(val direction: String, val name: String, val stops: List<LineStop>)

@Serializable
data class LineStopsResponse(val line: String, val directions: List<LineDirection>)

@Serializable
data class LiveBus(
    val doorNo: String,
    val lat: Double,
    val lng: Double,
    val direction: String,
    val variant: String,
    val routeCode: String,
    val reportedAt: String,
    val ageSeconds: Int,
    val status: String,
    val lastStopSeq: Int? = null,
)

@Serializable
data class LineLiveResponse(val line: String, val data: Freshness, val buses: List<LiveBus>)

@Serializable
data class RouteShape(val routeCode: String, val direction: String, val variant: String, val points: List<List<Double>>)

@Serializable
data class LineShapesResponse(val line: String, val shapes: List<RouteShape>)

@Serializable
data class FavoriteStop(val id: Long, val stopCode: String, val stopName: String, val label: String? = null)

@Serializable
data class FavoriteStopEntry(val favorite: FavoriteStop, val stop: StopInfo? = null, val lines: List<String> = emptyList())

@Serializable
data class FavoriteLine(val id: Long, val lineCode: String, val direction: String? = null, val label: String? = null)

@Serializable
data class FavoriteLineEntry(val favorite: FavoriteLine, val directions: Map<String, String> = emptyMap())

@Serializable
data class FavoritesResponse(val lines: List<FavoriteLineEntry>, val stops: List<FavoriteStopEntry>)

@Serializable
data class RegisterRequest(val platform: String, val name: String, val invite: String? = null, val linkCode: String? = null)

@Serializable
data class RegisterResponse(val key: String)

@Serializable
data class AddFavoriteStopRequest(val stop: String)

@Serializable
data class Problem(val detail: String? = null)
