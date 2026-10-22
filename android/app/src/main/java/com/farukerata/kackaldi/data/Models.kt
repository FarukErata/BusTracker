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
data class PlannedTripView(
    val departs: String,
    val atStop: String? = null,
    val routeCode: String = "",
    val variant: String = "",
    val cancelled: Boolean = false,
    val nextDay: Boolean = false,
)

@Serializable
data class PlannedView(
    val direction: String,
    val firstStop: String = "",
    val dayType: String = "",
    val holiday: Boolean = false,
    val next: List<PlannedTripView> = emptyList(),
    val nextDate: String? = null,
)

@Serializable
data class AnnouncementView(
    val line: String,
    val kind: String,
    val message: String,
    val postedAt: String = "",
    val direction: String? = null,
    val fromStop: String? = null,
    val departure: String? = null,
)

@Serializable
data class TimetableTripView(val time: String, val at: String, val routeCode: String = "", val cancelled: Boolean = false)

@Serializable
data class TimetableDirectionView(val direction: String, val name: String = "", val firstStop: String = "", val trips: List<TimetableTripView> = emptyList())

@Serializable
data class TimetableResponse(
    val line: String,
    val dayType: String,
    val holiday: Boolean = false,
    val todayDayType: String,
    val available: Boolean = false,
    val directions: List<TimetableDirectionView> = emptyList(),
    val announcements: List<AnnouncementView> = emptyList(),
)

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
    val heavyTraffic: Boolean = false,
    val planned: PlannedTripView? = null,
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
    val planned: PlannedView? = null,
)

@Serializable
data class StopArrivalsView(
    val stop: StopInfo,
    val arrivals: List<StopArrivalView>,
    val lines: List<LineStatusView>,
    val announcements: List<AnnouncementView> = emptyList(),
)

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
    val nextStopSeq: Int? = null,
    val alongMeters: Double? = null,
    val heavyTraffic: Boolean = false,
    val planned: PlannedTripView? = null,
)

@Serializable
data class StopRef(val code: String, val seq: Int, val name: String)

@Serializable
data class PassedView(val doorNo: String, val metersPast: Double)

@Serializable
data class LineArrivalsResponse(
    val line: String,
    val direction: String,
    val stop: StopRef,
    val data: Freshness,
    val approaching: List<ArrivalView>,
    val recentlyPassed: List<PassedView>,
    val planned: PlannedView? = null,
    val announcements: List<AnnouncementView> = emptyList(),
)

@Serializable
data class PathStop(val seq: Int, val code: String, val name: String, val lat: Double, val lng: Double)

@Serializable
data class BusPathResponse(
    val line: String,
    val routeCode: String,
    val bus: LiveBus,
    val stop: PathStop,
    val passed: Boolean,
    val distanceMeters: Double,
    val path: List<List<Double>>,
    val remainingStops: List<PathStop>,
)

@Serializable
data class AddFavoriteLineRequest(val line: String, val direction: String?)

@Serializable
data class LineLiveResponse(
    val line: String,
    val data: Freshness,
    val buses: List<LiveBus>,
    val planned: List<PlannedView> = emptyList(),
    val announcements: List<AnnouncementView> = emptyList(),
)

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
data class RailLineView(val id: Int, val name: String, val color: String, val description: String = "")

@Serializable
data class RailLineResponse(val line: RailLineView, val stations: List<RailStationView> = emptyList(), val notices: List<RailNoticeView> = emptyList())

@Serializable
data class RailStationView(
    val id: Int,
    val name: String,
    val lineId: Int,
    val line: String,
    val color: String,
    val lat: Double? = null,
    val lng: Double? = null,
)

@Serializable
data class RailDepartureView(val at: String, val nextDay: Boolean = false)

@Serializable
data class RailTripView(val time: String, val at: String)

@Serializable
data class RailDirectionView(
    val id: Int,
    val from: String,
    val to: String,
    val next: List<RailDepartureView> = emptyList(),
    val trips: List<RailTripView> = emptyList(),
    val nextDate: String? = null,
)

@Serializable
data class RailNoticeView(val line: String, val text: String, val updatedAt: String? = null)

@Serializable
data class RailStationResponse(
    val station: RailStationView,
    val directions: List<RailDirectionView> = emptyList(),
    val notices: List<RailNoticeView> = emptyList(),
)

@Serializable
data class RailDirectionSummary(val id: Int, val to: String, val next: List<RailDepartureView> = emptyList(), val nextDate: String? = null)

@Serializable
data class FavoriteStation(val id: Long, val stationId: Int, val stationName: String, val line: String)

@Serializable
data class FavoriteStationEntry(val favorite: FavoriteStation, val station: RailStationView? = null, val directions: List<RailDirectionSummary> = emptyList())

@Serializable
data class AddFavoriteStationRequest(val station: Int)

@Serializable
data class FavoritesResponse(
    val lines: List<FavoriteLineEntry>,
    val stops: List<FavoriteStopEntry>,
    val announcements: List<AnnouncementView> = emptyList(),
    val stations: List<FavoriteStationEntry> = emptyList(),
)

@Serializable
data class RegisterRequest(val platform: String, val name: String, val invite: String? = null, val linkCode: String? = null)

@Serializable
data class RegisterResponse(val key: String)

@Serializable
data class AddFavoriteStopRequest(val stop: String)

@Serializable
data class Problem(val detail: String? = null)

@Serializable
data class DeviceView(
    val id: Long,
    val platform: String,
    val name: String,
    val createdAt: String,
    val lastSeenAt: String,
    val current: Boolean,
)

@Serializable
data class MeResponse(val currentDeviceId: Long, val devices: List<DeviceView>)

@Serializable
data class LinkCodeResponse(val code: String, val expiresAt: String)
