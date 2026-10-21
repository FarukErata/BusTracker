package com.farukerata.kackaldi.ui

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.automirrored.outlined.ArrowForward
import androidx.compose.material.icons.automirrored.outlined.KeyboardArrowRight
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Refresh
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material.icons.outlined.Traffic
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.ripple
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.farukerata.kackaldi.i18n.L10n
import com.farukerata.kackaldi.i18n.Lang
import com.farukerata.kackaldi.i18n.S
import kotlinx.coroutines.delay

val RowShape = RoundedCornerShape(8.dp)
val FieldShape = RoundedCornerShape(10.dp)
val CardShape = RoundedCornerShape(12.dp)
private val BadgeShape = RoundedCornerShape(6.dp)

@Composable
fun Modifier.pressable(onClick: () -> Unit, enabled: Boolean = true, shape: androidx.compose.ui.graphics.Shape = RowShape, role: Role = Role.Button): Modifier {
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val scale by animateFloatAsState(if (pressed) 0.98f else 1f, tween(160), label = "press")
    return this
        .scale(scale)
        .clip(shape)
        .clickable(interaction, ripple(), enabled = enabled, role = role, onClick = onClick)
}

@Composable
fun Header(onBack: (() -> Unit)? = null, actions: @Composable RowScope.() -> Unit = {}, content: @Composable RowScope.() -> Unit) {
    Row(
        Modifier.fillMaxWidth().padding(start = 12.dp, end = 12.dp, top = 12.dp).heightIn(min = 40.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        if (onBack != null) GhostIcon(Icons.AutoMirrored.Outlined.ArrowBack, S.back, onBack)
        Row(Modifier.weight(1f), verticalAlignment = Alignment.CenterVertically, content = content)
        actions()
    }
}

@Composable
fun HeaderTitle(text: String) {
    Text(text, style = Kit.lg.copy(fontWeight = FontWeight.SemiBold, letterSpacing = Kit.tight), color = Kit.colors.foreground)
}

@Composable
fun GhostIcon(icon: ImageVector, description: String, onClick: () -> Unit, tint: Color = Kit.colors.foreground, enabled: Boolean = true) {
    Box(
        Modifier.size(36.dp).pressable(onClick, enabled).alpha(if (enabled) 1f else 0.5f),
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, description, Modifier.size(18.dp), tint = tint)
    }
}

@Composable
fun LanguageMenu() {
    val lang by L10n.lang.collectAsState()
    var open by remember { mutableStateOf(false) }
    Box {
        Box(
            Modifier.height(36.dp).pressable({ open = true }).semantics { contentDescription = S.language }.padding(horizontal = 8.dp),
            contentAlignment = Alignment.Center,
        ) {
            Text(lang.code.uppercase(), style = Kit.xs.copy(fontWeight = FontWeight.SemiBold, letterSpacing = 0.5.sp), color = Kit.colors.foreground)
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }, containerColor = Kit.colors.background) {
            Lang.entries.forEach { option ->
                DropdownMenuItem(
                    text = { Text(option.code.uppercase(), style = Kit.sm.copy(fontWeight = FontWeight.SemiBold), color = Kit.colors.foreground) },
                    onClick = {
                        open = false
                        L10n.set(option)
                    },
                )
            }
        }
    }
}

@Composable
fun SearchField(value: String, onChange: (String) -> Unit, placeholder: String, modifier: Modifier = Modifier, onFocusChange: (Boolean) -> Unit = {}) {
    val colors = Kit.colors
    BasicTextField(
        value = value,
        onValueChange = onChange,
        singleLine = true,
        textStyle = Kit.base.copy(color = colors.foreground),
        cursorBrush = SolidColor(colors.foreground),
        keyboardOptions = KeyboardOptions.Default,
        modifier = modifier.onFocusChanged { onFocusChange(it.isFocused) },
        decorationBox = { field ->
            Row(
                Modifier.fillMaxWidth().height(40.dp).border(1.dp, colors.input, FieldShape).padding(start = 10.dp, end = 2.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Icon(Icons.Outlined.Search, null, Modifier.size(16.dp), tint = colors.mutedForeground)
                Box(Modifier.weight(1f)) {
                    if (value.isEmpty()) Text(placeholder, style = Kit.base, color = colors.mutedForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    field()
                }
                if (value.isNotEmpty()) GhostIcon(Icons.Outlined.Close, S.clearSearch, { onChange("") })
            }
        },
    )
}

@Composable
fun SectionTitle(text: String) {
    Text(
        text,
        Modifier.padding(start = 12.dp, end = 12.dp, top = 16.dp, bottom = 4.dp),
        style = Kit.xs.copy(fontWeight = FontWeight.Medium),
        color = Kit.colors.mutedForeground,
    )
}

@Composable
fun ItemRow(
    onClick: (() -> Unit)?,
    modifier: Modifier = Modifier,
    selected: Boolean = false,
    alignTop: Boolean = false,
    leading: (@Composable () -> Unit)? = null,
    trailing: (@Composable () -> Unit)? = null,
    content: @Composable ColumnScope.() -> Unit,
) {
    val base = modifier.fillMaxWidth().padding(horizontal = 0.dp)
    Row(
        (if (onClick != null) base.pressable(onClick) else base.clip(RowShape))
            .background(if (selected) Kit.colors.secondary.copy(alpha = 0.6f) else Color.Transparent)
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = if (alignTop) Alignment.Top else Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        leading?.invoke()
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp), content = content)
        trailing?.invoke()
    }
}

@Composable
fun ItemTitle(text: String, modifier: Modifier = Modifier) {
    Text(text, modifier, style = Kit.sm.copy(fontWeight = FontWeight.Medium), color = Kit.colors.foreground, maxLines = 1, overflow = TextOverflow.Ellipsis)
}

@Composable
fun ItemDescription(text: String, modifier: Modifier = Modifier) {
    Text(text, modifier, style = Kit.sm, color = Kit.colors.mutedForeground, maxLines = 2, overflow = TextOverflow.Ellipsis)
}

@Composable
fun Chevron() {
    Icon(Icons.AutoMirrored.Outlined.KeyboardArrowRight, null, Modifier.size(16.dp), tint = Kit.colors.mutedForeground)
}

@Composable
fun LineBadge(code: String, modifier: Modifier = Modifier, minWidth: Dp = 0.dp, age: ReportAge = ReportAge.Fresh) {
    Box(
        modifier.widthIn(min = minWidth).height(20.dp).clip(BadgeShape).background(Kit.colors.secondary).ageRing(age, BadgeShape).padding(horizontal = 5.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(code, style = Kit.xs.copy(fontWeight = FontWeight.SemiBold), color = Kit.colors.foreground, maxLines = 1)
    }
}

@Composable
fun Modifier.ageRing(age: ReportAge, shape: Shape): Modifier = if (age == ReportAge.Fresh) this else border(2.dp, Kit.colors.seen, shape)

@Composable
fun OutlineBadge(text: String) {
    Box(
        Modifier.height(20.dp).border(1.dp, Kit.colors.border, BadgeShape).padding(horizontal = 5.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(text, style = Kit.xs.copy(fontWeight = FontWeight.Medium), color = Kit.colors.foreground)
    }
}

@Composable
fun LineBadges(lines: List<String>, max: Int = 4) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        lines.take(max).forEach { LineBadge(it) }
        if (lines.size > max) OutlineBadge("+${lines.size - max}")
    }
}

@Composable
fun TrafficBadge() {
    Row(
        Modifier.height(20.dp).clip(BadgeShape).background(Kit.colors.warning).padding(horizontal = 5.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Icon(Icons.Outlined.Traffic, null, Modifier.size(12.dp), tint = Color.White)
        Text(S.heavyTraffic, style = Kit.xs.copy(fontWeight = FontWeight.Medium), color = Color.White, maxLines = 1)
    }
}

@Composable
fun StopName(name: String, district: String?, style: TextStyle = Kit.sm.copy(fontWeight = FontWeight.Medium)) {
    Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.Bottom) {
        Text(titleCase(name), Modifier.weight(1f, fill = false), style = style, color = Kit.colors.foreground, maxLines = 1, overflow = TextOverflow.Ellipsis)
        if (!district.isNullOrBlank()) Text(titleCase(district), style = Kit.xs, color = Kit.colors.mutedForeground, maxLines = 1)
    }
}

@Composable
fun Towards(stop: String?, style: TextStyle = Kit.sm) {
    if (stop.isNullOrBlank()) return
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
        Icon(Icons.AutoMirrored.Outlined.ArrowForward, null, Modifier.size(12.dp), tint = Kit.colors.mutedForeground)
        Text(titleCase(stop), style = style, color = Kit.colors.mutedForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

@Composable
fun Chip(label: String, selected: Boolean, onClick: () -> Unit) {
    val colors = Kit.colors
    Box(
        Modifier
            .height(32.dp)
            .pressable(onClick, shape = CircleShape)
            .background(if (selected) colors.secondary else Color.Transparent, CircleShape)
            .border(BorderStroke(1.dp, if (selected) Color.Transparent else colors.border), CircleShape)
            .padding(horizontal = 12.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(label, style = Kit.sm.copy(fontWeight = FontWeight.Medium), color = colors.foreground)
    }
}

@Composable
fun Minutes(etaSeconds: Int, receivedAt: Long) {
    val now = rememberNow(5_000)
    val elapsed = if (receivedAt > 0) ((now - receivedAt) / 1000).toInt().coerceAtLeast(0) else 0
    Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(2.dp)) {
        Text(minutes((etaSeconds - elapsed).coerceAtLeast(0)), style = Kit.lg.copy(fontWeight = FontWeight.SemiBold, lineHeight = Kit.lg.fontSize), color = Kit.colors.foreground)
        Text(S.min, style = Kit.xs, color = Kit.colors.mutedForeground)
    }
}

@Composable
fun NotLeft() {
    Text(S.notLeft, style = Kit.xs, color = Kit.colors.mutedForeground)
}

@Composable
fun rememberNow(intervalMillis: Long): Long {
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(intervalMillis) {
        while (true) {
            now = System.currentTimeMillis()
            delay(intervalMillis)
        }
    }
    return now
}

enum class ButtonKind { Primary, Outline, Ghost, Destructive }

@Composable
fun KitButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    kind: ButtonKind = ButtonKind.Primary,
    enabled: Boolean = true,
    large: Boolean = false,
    icon: ImageVector? = null,
) {
    val colors = Kit.colors
    val (background, content) = when (kind) {
        ButtonKind.Primary -> colors.primary to colors.primaryForeground
        ButtonKind.Outline -> Color.Transparent to colors.foreground
        ButtonKind.Ghost -> Color.Transparent to colors.foreground
        ButtonKind.Destructive -> colors.destructive.copy(alpha = 0.12f) to colors.destructive
    }
    Row(
        modifier
            .height(if (large) 40.dp else 32.dp)
            .pressable(onClick, enabled, FieldShape)
            .alpha(if (enabled) 1f else 0.5f)
            .background(background, FieldShape)
            .then(if (kind == ButtonKind.Outline) Modifier.border(1.dp, colors.border, FieldShape) else Modifier)
            .padding(horizontal = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp, Alignment.CenterHorizontally),
    ) {
        if (icon != null) Icon(icon, null, Modifier.size(16.dp), tint = content)
        Text(text, style = Kit.sm.copy(fontWeight = FontWeight.Medium), color = content, maxLines = 1)
    }
}

@Composable
fun LinkText(text: String, onClick: () -> Unit, modifier: Modifier = Modifier) {
    Text(
        text,
        modifier.clip(RowShape).clickable(onClick = onClick).padding(horizontal = 4.dp, vertical = 2.dp),
        style = Kit.sm.copy(textDecoration = TextDecoration.Underline),
        color = Kit.colors.mutedForeground,
        textAlign = TextAlign.Center,
    )
}

@Composable
fun EmptyState(title: String, description: String, modifier: Modifier = Modifier, action: (@Composable () -> Unit)? = null) {
    Column(
        modifier.fillMaxWidth().padding(horizontal = 24.dp, vertical = 48.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Text(title, style = Kit.sm.copy(fontWeight = FontWeight.Medium), color = Kit.colors.foreground, textAlign = TextAlign.Center)
        Text(description, style = Kit.sm, color = Kit.colors.mutedForeground, textAlign = TextAlign.Center)
        if (action != null) {
            Spacer(Modifier.height(4.dp))
            action()
        }
    }
}

@Composable
fun ErrorState(message: String, onRetry: () -> Unit, modifier: Modifier = Modifier) {
    EmptyState(S.couldNotLoad, message, modifier) {
        KitButton(S.retry, onRetry, kind = ButtonKind.Outline, icon = Icons.Outlined.Refresh)
    }
}

@Composable
fun RowsSkeleton(rows: Int = 4) {
    val tint = Kit.colors.secondary
    Column(Modifier.padding(horizontal = 12.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        repeat(rows) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Box(Modifier.size(32.dp).background(tint, CircleShape))
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Box(Modifier.fillMaxWidth(0.66f).height(14.dp).background(tint, RowShape))
                    Box(Modifier.fillMaxWidth(0.33f).height(12.dp).background(tint, RowShape))
                }
            }
        }
    }
}

@Composable
fun UpdatedAgo(fetchedAt: String?, modifier: Modifier = Modifier) {
    if (fetchedAt == null) return
    val now = rememberNow(5_000)
    Text(
        S.updated(ago(secondsSince(fetchedAt, java.time.Instant.ofEpochMilli(now)))),
        modifier.fillMaxWidth().padding(16.dp),
        style = Kit.xs,
        color = Kit.colors.mutedForeground,
        textAlign = TextAlign.Center,
    )
}

@Composable
fun MonoCode(text: String) {
    Text(text, style = Kit.xl2.copy(fontFamily = FontFamily.Monospace, fontWeight = FontWeight.SemiBold, fontSize = Kit.xl2.fontSize * 1.4f), color = Kit.colors.foreground)
}

@Composable
fun Hint(text: String, icon: ImageVector, modifier: Modifier = Modifier) {
    Row(
        modifier.fillMaxWidth().padding(horizontal = 12.dp).border(1.dp, Kit.colors.border, RowShape).padding(horizontal = 10.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Icon(icon, null, Modifier.size(14.dp), tint = Kit.colors.mutedForeground)
        Text(text, style = Kit.xs, color = Kit.colors.mutedForeground)
    }
}

@Composable
fun HSpace(width: Dp) = Spacer(Modifier.width(width))
