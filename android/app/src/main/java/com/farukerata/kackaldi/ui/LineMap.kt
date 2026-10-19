package com.farukerata.kackaldi.ui

import android.graphics.RectF
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.farukerata.kackaldi.data.LineStop
import org.maplibre.android.camera.CameraUpdateFactory
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.geometry.LatLngBounds
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.Style
import org.maplibre.android.style.expressions.Expression
import org.maplibre.android.style.layers.CircleLayer
import org.maplibre.android.style.layers.LineLayer
import org.maplibre.android.style.layers.Property
import org.maplibre.android.style.layers.PropertyFactory.circleColor
import org.maplibre.android.style.layers.PropertyFactory.circleOpacity
import org.maplibre.android.style.layers.PropertyFactory.circleRadius
import org.maplibre.android.style.layers.PropertyFactory.circleStrokeColor
import org.maplibre.android.style.layers.PropertyFactory.circleStrokeOpacity
import org.maplibre.android.style.layers.PropertyFactory.circleStrokeWidth
import org.maplibre.android.style.layers.PropertyFactory.circleTranslate
import org.maplibre.android.style.layers.PropertyFactory.lineCap
import org.maplibre.android.style.layers.PropertyFactory.lineColor
import org.maplibre.android.style.layers.PropertyFactory.lineJoin
import org.maplibre.android.style.layers.PropertyFactory.lineWidth
import org.maplibre.android.style.sources.GeoJsonSource
import org.maplibre.geojson.Feature
import org.maplibre.geojson.FeatureCollection
import org.maplibre.geojson.LineString
import org.maplibre.geojson.Point

data class MapBus(val doorNo: String, val lat: Double, val lng: Double, val age: ReportAge, val heavyTraffic: Boolean = false)

data class LineMapData(val key: String, val route: List<List<Double>>, val stops: List<LineStop>, val buses: List<MapBus>)

private data class MapColors(val route: Int, val stopFill: Int, val bus: Int, val staleBus: Int, val outline: Int)

@Composable
fun LineMap(data: LineMapData, onStop: (String) -> Unit, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    val scheme = MaterialTheme.colorScheme
    val colors = MapColors(
        route = scheme.primary.toArgb(),
        stopFill = scheme.surface.toArgb(),
        bus = scheme.primary.toArgb(),
        staleBus = scheme.outline.toArgb(),
        outline = Color.White.toArgb(),
    )
    val styleUrl = if (isSystemInDarkTheme()) DARK_STYLE else LIGHT_STYLE
    val currentOnStop by rememberUpdatedState(onStop)
    val controller = remember { LineMapController(colors) }
    val mapView = remember {
        MapView(context).apply {
            onCreate(null)
            getMapAsync { map ->
                map.uiSettings.apply {
                    isRotateGesturesEnabled = false
                    isTiltGesturesEnabled = false
                    isCompassEnabled = false
                    isLogoEnabled = false
                }
                map.addOnMapClickListener { point -> controller.stopAt(point)?.let { currentOnStop(it); true } ?: false }
                map.setStyle(Style.Builder().fromUri(styleUrl)) { style -> controller.attach(map, style) }
            }
        }
    }

    DisposableEffect(lifecycle, mapView) {
        val tracker = MapLifecycle(mapView)
        val observer = LifecycleEventObserver { _, event -> tracker.on(event) }
        lifecycle.addObserver(observer)
        onDispose {
            lifecycle.removeObserver(observer)
            tracker.destroy()
        }
    }

    AndroidView(factory = { mapView }, modifier = modifier, update = { controller.show(data) })
}

private class MapLifecycle(private val view: MapView) {
    private var started = false
    private var resumed = false

    fun on(event: Lifecycle.Event) {
        when (event) {
            Lifecycle.Event.ON_START -> if (!started) { view.onStart(); started = true }
            Lifecycle.Event.ON_RESUME -> if (!resumed) { view.onResume(); resumed = true }
            Lifecycle.Event.ON_PAUSE -> pause()
            Lifecycle.Event.ON_STOP -> stop()
            else -> Unit
        }
    }

    fun destroy() {
        pause()
        stop()
        view.onDestroy()
    }

    private fun pause() {
        if (resumed) { view.onPause(); resumed = false }
    }

    private fun stop() {
        if (started) { view.onStop(); started = false }
    }
}

private class LineMapController(private val colors: MapColors) {
    private var map: MapLibreMap? = null
    private var style: Style? = null
    private var data: LineMapData? = null
    private var framedKey: String? = null

    fun attach(map: MapLibreMap, style: Style) {
        this.map = map
        this.style = style
        style.addSource(GeoJsonSource(ROUTE))
        style.addSource(GeoJsonSource(STOPS))
        style.addSource(GeoJsonSource(BUSES))
        style.addLayer(
            LineLayer(ROUTE, ROUTE).withProperties(
                lineColor(colors.route), lineWidth(4f), lineJoin(Property.LINE_JOIN_ROUND), lineCap(Property.LINE_CAP_ROUND),
            ),
        )
        style.addLayer(
            CircleLayer(STOPS, STOPS).withProperties(
                circleRadius(byZoom(1.5f, 5f)),
                circleColor(colors.stopFill),
                circleStrokeColor(colors.route),
                circleStrokeWidth(byZoom(1f, 2f)),
            ),
        )
        style.addLayer(
            CircleLayer(BUSES, BUSES).withProperties(
                circleRadius(10f),
                circleColor(Expression.toColor(Expression.get(FILL))),
                circleOpacity(Expression.get(OPACITY)),
                circleStrokeColor(colors.outline),
                circleStrokeWidth(3f),
                circleStrokeOpacity(Expression.get(OPACITY)),
            ),
        )
        style.addLayer(
            CircleLayer(TRAFFIC, BUSES).withProperties(
                circleRadius(4.5f),
                circleColor(TrafficAmber.toArgb()),
                circleStrokeColor(colors.outline),
                circleStrokeWidth(1.5f),
                circleTranslate(arrayOf(8f, -8f)),
            ).withFilter(Expression.eq(Expression.get(TRAFFIC), true)),
        )
        apply()
    }

    fun show(next: LineMapData) {
        data = next
        apply()
    }

    fun stopAt(point: LatLng): String? {
        val map = map ?: return null
        val screen = map.projection.toScreenLocation(point)
        val area = RectF(screen.x - TAP_SLOP, screen.y - TAP_SLOP, screen.x + TAP_SLOP, screen.y + TAP_SLOP)
        return map.queryRenderedFeatures(area, STOPS).firstOrNull()?.getStringProperty(CODE)
    }

    private fun apply() {
        val style = style ?: return
        val data = data ?: return
        val route = data.route.map { Point.fromLngLat(it[1], it[0]) }
        style.getSourceAs<GeoJsonSource>(ROUTE)?.setGeoJson(
            FeatureCollection.fromFeatures(if (route.size >= 2) listOf(Feature.fromGeometry(LineString.fromLngLats(route))) else emptyList()),
        )
        style.getSourceAs<GeoJsonSource>(STOPS)?.setGeoJson(
            FeatureCollection.fromFeatures(
                data.stops.filter { it.lat != 0.0 }.map { stop ->
                    Feature.fromGeometry(Point.fromLngLat(stop.lng, stop.lat)).apply { addStringProperty(CODE, stop.code) }
                },
            ),
        )
        style.getSourceAs<GeoJsonSource>(BUSES)?.setGeoJson(FeatureCollection.fromFeatures(data.buses.map(::busFeature)))
        frame(data, route)
    }

    private fun busFeature(bus: MapBus): Feature = Feature.fromGeometry(Point.fromLngLat(bus.lng, bus.lat)).apply {
        addStringProperty(FILL, hex(if (bus.age == ReportAge.Stale) colors.staleBus else colors.bus))
        addNumberProperty(OPACITY, if (bus.age == ReportAge.Fresh) 1f else 0.55f)
        addBooleanProperty(TRAFFIC, bus.heavyTraffic)
    }

    private fun frame(data: LineMapData, route: List<Point>) {
        val map = map ?: return
        if (framedKey == data.key) return
        val points = route.ifEmpty { data.stops.filter { it.lat != 0.0 }.map { Point.fromLngLat(it.lng, it.lat) } }
        if (points.size < 2) return
        val bounds = LatLngBounds.Builder().includes(points.map { LatLng(it.latitude(), it.longitude()) }).build()
        map.moveCamera(CameraUpdateFactory.newLatLngBounds(bounds, FRAME_PADDING))
        framedKey = data.key
    }

    private fun hex(argb: Int) = String.format("#%06X", 0xFFFFFF and argb)

    private fun byZoom(far: Float, near: Float): Expression =
        Expression.interpolate(Expression.linear(), Expression.zoom(), Expression.stop(FAR_ZOOM, far), Expression.stop(NEAR_ZOOM, near))
}

private const val LIGHT_STYLE = "https://tiles.openfreemap.org/styles/positron"
private const val DARK_STYLE = "https://tiles.openfreemap.org/styles/dark"
private const val ROUTE = "route"
private const val STOPS = "stops"
private const val BUSES = "buses"
private const val CODE = "code"
private const val FILL = "fill"
private const val OPACITY = "opacity"
private const val TRAFFIC = "traffic"
private const val TAP_SLOP = 24f
private const val FRAME_PADDING = 48
private const val FAR_ZOOM = 11f
private const val NEAR_ZOOM = 15f
