package com.farukerata.kackaldi.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.em
import androidx.compose.ui.unit.sp
import com.farukerata.kackaldi.R

@Immutable
data class Palette(
    val dark: Boolean,
    val background: Color,
    val foreground: Color,
    val card: Color,
    val primary: Color,
    val primaryForeground: Color,
    val secondary: Color,
    val mutedForeground: Color,
    val border: Color,
    val input: Color,
    val destructive: Color,
    val star: Color,
    val warning: Color,
    val route: Color,
    val routeHighlight: Color,
    val routeVariant: Color,
    val stopFill: Color,
    val stopStroke: Color,
    val pinFill: Color,
    val pinStroke: Color,
    val busMoving: Color,
    val busWaiting: Color,
    val busSelected: Color,
    val busOutline: Color,
    val seen: Color = Color(0x8071717A),
)

private val LightPalette = Palette(
    dark = false,
    background = Color(0xFFFFFFFF),
    foreground = Color(0xFF0A0A0A),
    card = Color(0xFFFFFFFF),
    primary = Color(0xFF171717),
    primaryForeground = Color(0xFFFAFAFA),
    secondary = Color(0xFFF5F5F5),
    mutedForeground = Color(0xFF737373),
    border = Color(0xFFE5E5E5),
    input = Color(0xFFE5E5E5),
    destructive = Color(0xFFE7000B),
    star = Color(0xFFF59E0B),
    warning = Color(0xFFEAB308),
    route = Color(0xBF0284C7),
    routeHighlight = Color(0xFF0EA5E9),
    routeVariant = Color(0xD98B5CF6),
    stopFill = Color(0xFFFFFFFF),
    stopStroke = Color(0xFF71717A),
    pinFill = Color(0xFF0284C7),
    pinStroke = Color(0xFFFFFFFF),
    busMoving = Color(0xFF047857),
    busWaiting = Color(0xFF71717A),
    busSelected = Color(0xFF0369A1),
    busOutline = Color(0xFFFFFFFF),
)

private val DarkPalette = Palette(
    dark = true,
    background = Color(0xFF0A0A0A),
    foreground = Color(0xFFFAFAFA),
    card = Color(0xFF171717),
    primary = Color(0xFFE5E5E5),
    primaryForeground = Color(0xFF171717),
    secondary = Color(0xFF262626),
    mutedForeground = Color(0xFFA1A1A1),
    border = Color(0x1AFFFFFF),
    input = Color(0x26FFFFFF),
    destructive = Color(0xFFFF6467),
    star = Color(0xFFFBBF24),
    warning = Color(0xFFEAB308),
    route = Color(0xBF38BDF8),
    routeHighlight = Color(0xFF7DD3FC),
    routeVariant = Color(0xD98B5CF6),
    stopFill = Color(0xFF18181B),
    stopStroke = Color(0xFFA1A1AA),
    pinFill = Color(0xFF38BDF8),
    pinStroke = Color(0xFF09090B),
    busMoving = Color(0xFF047857),
    busWaiting = Color(0xFF71717A),
    busSelected = Color(0xFF0369A1),
    busOutline = Color(0xFF09090B),
)

private val LocalPalette = staticCompositionLocalOf { LightPalette }

val Geist = FontFamily(
    Font(R.font.geist, FontWeight.Normal, variationSettings = FontVariation.Settings(FontVariation.weight(400))),
    Font(R.font.geist, FontWeight.Medium, variationSettings = FontVariation.Settings(FontVariation.weight(500))),
    Font(R.font.geist, FontWeight.SemiBold, variationSettings = FontVariation.Settings(FontVariation.weight(600))),
    Font(R.font.geist, FontWeight.Bold, variationSettings = FontVariation.Settings(FontVariation.weight(700))),
)

object Kit {
    val colors: Palette
        @Composable get() = LocalPalette.current

    val xs = TextStyle(fontFamily = Geist, fontSize = 12.sp, lineHeight = 16.sp)
    val sm = TextStyle(fontFamily = Geist, fontSize = 14.sp, lineHeight = 20.sp)
    val base = TextStyle(fontFamily = Geist, fontSize = 16.sp, lineHeight = 24.sp)
    val lg = TextStyle(fontFamily = Geist, fontSize = 18.sp, lineHeight = 28.sp)
    val xl2 = TextStyle(fontFamily = Geist, fontSize = 24.sp, lineHeight = 32.sp)
    val tight = (-0.025).em
}

@Composable
fun KacKaldiTheme(dark: Boolean, content: @Composable () -> Unit) {
    val palette = if (dark) DarkPalette else LightPalette
    val scheme = if (dark) {
        darkColorScheme(
            primary = palette.primary, onPrimary = palette.primaryForeground, background = palette.background,
            onBackground = palette.foreground, surface = palette.background, onSurface = palette.foreground,
            surfaceVariant = palette.secondary, onSurfaceVariant = palette.mutedForeground, outline = palette.border,
            outlineVariant = palette.border, error = palette.destructive, surfaceContainerLow = palette.card,
            surfaceContainer = palette.card, surfaceContainerHigh = palette.card, surfaceContainerHighest = palette.secondary,
        )
    } else {
        lightColorScheme(
            primary = palette.primary, onPrimary = palette.primaryForeground, background = palette.background,
            onBackground = palette.foreground, surface = palette.background, onSurface = palette.foreground,
            surfaceVariant = palette.secondary, onSurfaceVariant = palette.mutedForeground, outline = palette.border,
            outlineVariant = palette.border, error = palette.destructive, surfaceContainerLow = palette.card,
            surfaceContainer = palette.card, surfaceContainerHigh = palette.card, surfaceContainerHighest = palette.secondary,
        )
    }
    val type = Typography().let { t ->
        Typography(
            bodyLarge = t.bodyLarge.copy(fontFamily = Geist), bodyMedium = t.bodyMedium.copy(fontFamily = Geist),
            bodySmall = t.bodySmall.copy(fontFamily = Geist), labelLarge = t.labelLarge.copy(fontFamily = Geist),
            labelMedium = t.labelMedium.copy(fontFamily = Geist), labelSmall = t.labelSmall.copy(fontFamily = Geist),
            titleLarge = t.titleLarge.copy(fontFamily = Geist), titleMedium = t.titleMedium.copy(fontFamily = Geist),
            titleSmall = t.titleSmall.copy(fontFamily = Geist),
        )
    }
    CompositionLocalProvider(LocalPalette provides palette) {
        MaterialTheme(colorScheme = scheme, typography = type, content = content)
    }
}
