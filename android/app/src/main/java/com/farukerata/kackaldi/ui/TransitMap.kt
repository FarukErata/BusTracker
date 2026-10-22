package com.farukerata.kackaldi.ui

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.os.Build
import android.view.Gravity
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.res.ResourcesCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.farukerata.kackaldi.R
import org.maplibre.android.camera.CameraUpdateFactory
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.geometry.LatLngBounds
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.MapLibreMapOptions
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.Style
import org.maplibre.android.style.expressions.Expression
import org.maplibre.android.style.layers.CircleLayer
import org.maplibre.android.style.layers.LineLayer
import org.maplibre.android.style.layers.Property
import org.maplibre.android.style.layers.PropertyFactory.circleColor
import org.maplibre.android.style.layers.PropertyFactory.circleRadius
import org.maplibre.android.style.layers.PropertyFactory.circleStrokeColor
import org.maplibre.android.style.layers.PropertyFactory.circleStrokeWidth
import org.maplibre.android.style.layers.PropertyFactory.iconAllowOverlap
import org.maplibre.android.style.layers.PropertyFactory.iconIgnorePlacement
import org.maplibre.android.style.layers.PropertyFactory.iconImage
import org.maplibre.android.style.layers.PropertyFactory.lineCap
import org.maplibre.android.style.layers.PropertyFactory.lineColor
import org.maplibre.android.style.layers.PropertyFactory.lineDasharray
import org.maplibre.android.style.layers.PropertyFactory.lineJoin
import org.maplibre.android.style.layers.PropertyFactory.lineWidth
import org.maplibre.android.style.layers.PropertyFactory.symbolSortKey
import org.maplibre.android.style.layers.SymbolLayer
import org.maplibre.android.style.sources.GeoJsonSource
import org.maplibre.geojson.Feature
import org.maplibre.geojson.FeatureCollection
import org.maplibre.geojson.LineString
import org.maplibre.geojson.Point

data class MapStop(val code: String, val lat: Double, val lng: Double)

data class MapRail(val id: Int, val lat: Double, val lng: Double, val color: String)

data class MapBus(
    val doorNo: String,
    val lat: Double,
    val lng: Double,
    val waiting: Boolean = false,
    val age: ReportAge = ReportAge.Fresh,
    val heavyTraffic: Boolean = false,
)

sealed interface MapFrame {
    val key: String

    data class Fit(override val key: String, val points: List<List<Double>>) : MapFrame

    data class Center(override val key: String, val lat: Double, val lng: Double, val zoom: Double = 16.5) : MapFrame
}

data class MapScene(
    val frame: MapFrame,
    val route: List<List<Double>> = emptyList(),
    val variant: List<List<Double>> = emptyList(),
    val highlight: List<List<Double>> = emptyList(),
    val stops: List<MapStop> = emptyList(),
    val pinned: MapStop? = null,
    val buses: List<MapBus> = emptyList(),
    val selectedBus: String? = null,
    val bottomInsetDp: Int = 0,
    val topInsetDp: Int = 0,
    val rail: List<MapRail> = emptyList(),
    val pinColor: String? = null,
    val railRoute: List<List<Double>> = emptyList(),
    val railRouteColor: String? = null,
)

@Composable
fun TransitMap(
    scene: MapScene,
    modifier: Modifier = Modifier,
    onStop: (String) -> Unit = {},
    onBus: (String) -> Unit = {},
    onRail: (Int) -> Unit = {},
) {
    val context = LocalContext.current
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    val palette = Kit.colors
    val density = LocalDensity.current.density
    val styleUrl = if (palette.dark) DARK_STYLE else LIGHT_STYLE
    val currentOnStop by rememberUpdatedState(onStop)
    val currentOnBus by rememberUpdatedState(onBus)
    val currentOnRail by rememberUpdatedState(onRail)
    val controller = remember(palette) { MapController(palette, density, labelTypeface(context)) }
    val mapView = remember(styleUrl) {
        MapView(context, MapLibreMapOptions.createFromAttributes(context).textureMode(true)).apply {
            onCreate(null)
            getMapAsync { map ->
                map.uiSettings.apply {
                    isRotateGesturesEnabled = false
                    isTiltGesturesEnabled = false
                    isCompassEnabled = false
                    isLogoEnabled = false
                    attributionGravity = Gravity.BOTTOM or Gravity.END
                }
                map.addOnMapClickListener { point ->
                    controller.busAt(point)?.let { currentOnBus(it); return@addOnMapClickListener true }
                    controller.stopAt(point)?.let { currentOnStop(it); return@addOnMapClickListener true }
                    controller.railAt(point)?.let { currentOnRail(it); return@addOnMapClickListener true }
                    false
                }
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

    AndroidView(factory = { mapView }, modifier = modifier, update = { controller.show(scene) })
}

private fun labelTypeface(context: Context): Typeface {
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.P) return Typeface.DEFAULT_BOLD
    return runCatching { Typeface.create(ResourcesCompat.getFont(context, R.font.geist), 600, false) }.getOrDefault(Typeface.DEFAULT_BOLD)
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

private class MapController(private val palette: Palette, private val density: Float, private val typeface: Typeface) {
    private var map: MapLibreMap? = null
    private var style: Style? = null
    private var scene: MapScene? = null
    private var framedKey: String? = null

    fun attach(map: MapLibreMap, style: Style) {
        this.map = map
        this.style = style
        listOf(ROUTE, VARIANT, HIGHLIGHT, RAIL_ROUTE, RAIL, STOPS, PIN, BUSES).forEach { style.addSource(GeoJsonSource(it)) }
        style.addLayer(line(ROUTE, palette.route.toArgb(), 5f))
        style.addLayer(line(VARIANT, palette.routeVariant.toArgb(), 4f).withProperties(lineDasharray(arrayOf(2f, 1.5f))))
        style.addLayer(line(HIGHLIGHT, palette.routeHighlight.toArgb(), 7f))
        style.addLayer(line(RAIL_ROUTE, palette.route.toArgb(), 5f))
        style.addLayer(
            CircleLayer(RAIL, RAIL).withProperties(
                circleRadius(5.5f), circleColor(Expression.toColor(Expression.get(COLOR))), circleStrokeColor(android.graphics.Color.WHITE), circleStrokeWidth(2f),
            ).apply { minZoom = RAIL_MIN_ZOOM },
        )
        style.addLayer(
            CircleLayer(STOPS, STOPS).withProperties(
                circleRadius(4.5f), circleColor(palette.stopFill.toArgb()), circleStrokeColor(palette.stopStroke.toArgb()), circleStrokeWidth(1.5f),
            ),
        )
        style.addLayer(
            CircleLayer(PIN, PIN).withProperties(
                circleRadius(8f), circleColor(palette.pinFill.toArgb()), circleStrokeColor(palette.pinStroke.toArgb()), circleStrokeWidth(3f),
            ),
        )
        style.addLayer(
            SymbolLayer(BUSES, BUSES).withProperties(
                iconImage(Expression.get(ICON)),
                iconAllowOverlap(true),
                iconIgnorePlacement(true),
                symbolSortKey(Expression.get(ORDER)),
            ),
        )
        apply()
    }

    fun show(next: MapScene) {
        scene = next
        apply()
    }

    fun busAt(point: LatLng): String? = featureAt(point, BUSES, 20f)?.getStringProperty(CODE)

    fun stopAt(point: LatLng): String? = featureAt(point, STOPS, 22f)?.getStringProperty(CODE)

    fun railAt(point: LatLng): Int? = featureAt(point, RAIL, 18f)?.getNumberProperty(RAIL_ID)?.toInt()

    private fun featureAt(point: LatLng, layer: String, slop: Float): Feature? {
        val map = map ?: return null
        val screen = map.projection.toScreenLocation(point)
        val area = RectF(screen.x - slop, screen.y - slop, screen.x + slop, screen.y + slop)
        return map.queryRenderedFeatures(area, layer).firstOrNull()
    }

    private fun line(id: String, color: Int, width: Float) =
        LineLayer(id, id).withProperties(lineColor(color), lineWidth(width), lineJoin(Property.LINE_JOIN_ROUND), lineCap(Property.LINE_CAP_ROUND))

    private fun apply() {
        val style = style ?: return
        val scene = scene ?: return
        placeAttribution(scene)
        style.source(ROUTE).setGeoJson(lineFeature(scene.route))
        style.source(VARIANT).setGeoJson(lineFeature(scene.variant))
        style.source(HIGHLIGHT).setGeoJson(lineFeature(scene.highlight))
        style.source(STOPS).setGeoJson(FeatureCollection.fromFeatures(scene.stops.filter { it.code != scene.pinned?.code }.map(::stopFeature)))
        style.source(PIN).setGeoJson(FeatureCollection.fromFeatures(listOfNotNull(scene.pinned).map(::stopFeature)))
        style.source(RAIL).setGeoJson(FeatureCollection.fromFeatures(scene.rail.map(::railFeature)))
        style.source(RAIL_ROUTE).setGeoJson(lineFeature(scene.railRoute))
        scene.railRouteColor?.let { hex -> runCatching { android.graphics.Color.parseColor(hex) }.getOrNull() }?.let {
            style.getLayerAs<LineLayer>(RAIL_ROUTE)?.setProperties(lineColor(it))
        }
        style.getLayerAs<CircleLayer>(RAIL)?.minZoom = if (scene.railRoute.isEmpty()) RAIL_MIN_ZOOM else 0f
        style.getLayerAs<CircleLayer>(PIN)?.setProperties(
            circleColor(scene.pinColor?.let { runCatching { android.graphics.Color.parseColor(it) }.getOrNull() } ?: palette.pinFill.toArgb()),
        )
        style.source(BUSES).setGeoJson(FeatureCollection.fromFeatures(scene.buses.map { busFeature(style, it, it.doorNo == scene.selectedBus) }))
        frame(scene)
    }

    private fun placeAttribution(scene: MapScene) {
        val map = map ?: return
        val edge = (ATTRIBUTION_MARGIN * density).toInt()
        val bottom = ((scene.bottomInsetDp + ATTRIBUTION_MARGIN) * density).toInt()
        map.uiSettings.setAttributionMargins(0, 0, edge, bottom)
    }

    private fun Style.source(id: String): GeoJsonSource = getSourceAs(id)!!

    private fun lineFeature(points: List<List<Double>>): FeatureCollection =
        FeatureCollection.fromFeatures(
            if (points.size >= 2) listOf(Feature.fromGeometry(LineString.fromLngLats(points.map { Point.fromLngLat(it[1], it[0]) }))) else emptyList(),
        )

    private fun stopFeature(stop: MapStop): Feature =
        Feature.fromGeometry(Point.fromLngLat(stop.lng, stop.lat)).apply { addStringProperty(CODE, stop.code) }

    private fun railFeature(station: MapRail): Feature =
        Feature.fromGeometry(Point.fromLngLat(station.lng, station.lat)).apply {
            addNumberProperty(RAIL_ID, station.id)
            addStringProperty(COLOR, station.color)
        }

    private fun busFeature(style: Style, bus: MapBus, selected: Boolean): Feature {
        val color = when {
            selected -> palette.busSelected
            bus.waiting -> palette.busWaiting
            else -> palette.busMoving
        }
        val ring = if (bus.age == ReportAge.Fresh) palette.busOutline else palette.seen
        val icon = "bus:${bus.doorNo}:${color.toArgb()}:${ring.toArgb()}:${bus.heavyTraffic}:$selected"
        if (style.getImage(icon) == null) style.addImage(icon, busLabel(bus.doorNo, color.toArgb(), ring.toArgb(), bus.heavyTraffic, selected))
        return Feature.fromGeometry(Point.fromLngLat(bus.lng, bus.lat)).apply {
            addStringProperty(CODE, bus.doorNo)
            addStringProperty(ICON, icon)
            addNumberProperty(ORDER, if (selected) 1 else 0)
        }
    }

    private fun busLabel(text: String, color: Int, ring: Int, traffic: Boolean, selected: Boolean): Bitmap {
        val scale = density * if (selected) 1.2f else 1f
        val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            this.typeface = this@MapController.typeface
            textSize = 11f * scale
            this.color = android.graphics.Color.WHITE
        }
        val outline = (if (ring == palette.busOutline.toArgb()) 1.5f else 2f) * scale
        val padX = 5f * scale
        val padY = 1.5f * scale
        val dot = if (traffic) 4.5f * scale else 0f
        val width = textPaint.measureText(text) + padX * 2 + outline * 2
        val height = textPaint.fontSpacing + padY * 2 + outline * 2
        val bitmap = Bitmap.createBitmap((width + dot * 2).toInt() + 1, (height + dot).toInt() + 1, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        val top = dot
        val radius = 6f * scale
        val fill = Paint(Paint.ANTI_ALIAS_FLAG)
        fill.color = ring
        canvas.drawRoundRect(RectF(0f, top, width, top + height), radius + outline, radius + outline, fill)
        fill.color = color
        canvas.drawRoundRect(RectF(outline, top + outline, width - outline, top + height - outline), radius, radius, fill)
        canvas.drawText(text, outline + padX, top + outline + padY - textPaint.ascent(), textPaint)
        if (traffic) {
            fill.color = palette.busOutline.toArgb()
            canvas.drawCircle(width - dot / 2, dot + 1, dot, fill)
            fill.color = palette.star.toArgb()
            canvas.drawCircle(width - dot / 2, dot + 1, dot - outline, fill)
        }
        return bitmap
    }

    private fun frame(scene: MapScene) {
        val map = map ?: return
        val frame = scene.frame
        if (framedKey == frame.key) return
        when (frame) {
            is MapFrame.Center -> map.moveCamera(CameraUpdateFactory.newLatLngZoom(LatLng(frame.lat, frame.lng), frame.zoom))
            is MapFrame.Fit -> {
                if (frame.points.size < 2) return
                val bounds = LatLngBounds.Builder().includes(frame.points.map { LatLng(it[0], it[1]) }).build()
                val pad = (FRAME_PADDING * density).toInt()
                map.moveCamera(
                    CameraUpdateFactory.newLatLngBounds(
                        bounds, pad, pad + (scene.topInsetDp * density).toInt(), pad, pad + (scene.bottomInsetDp * density).toInt(),
                    ),
                )
                if (map.cameraPosition.zoom > MAX_FIT_ZOOM) map.moveCamera(CameraUpdateFactory.zoomTo(MAX_FIT_ZOOM))
            }
        }
        framedKey = frame.key
    }
}

private const val LIGHT_STYLE = "https://tiles.openfreemap.org/styles/positron"
private const val DARK_STYLE = "https://tiles.openfreemap.org/styles/dark"
private const val ROUTE = "route"
private const val VARIANT = "variant"
private const val HIGHLIGHT = "highlight"
private const val STOPS = "stops"
private const val PIN = "pin"
private const val BUSES = "buses"
private const val RAIL = "rail"
private const val RAIL_ROUTE = "railRoute"
private const val RAIL_ID = "railId"
private const val COLOR = "color"
private const val RAIL_MIN_ZOOM = 13f
private const val CODE = "code"
private const val ICON = "icon"
private const val ORDER = "order"
private const val FRAME_PADDING = 28
private const val ATTRIBUTION_MARGIN = 6
private const val MAX_FIT_ZOOM = 17.0
