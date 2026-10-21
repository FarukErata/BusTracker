package com.farukerata.kackaldi.ui

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.asPaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.ContentCopy
import androidx.compose.material.icons.outlined.DesktopWindows
import androidx.compose.material.icons.outlined.Smartphone
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.farukerata.kackaldi.data.ApiException
import com.farukerata.kackaldi.data.DeviceView
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.data.LinkCodeResponse
import com.farukerata.kackaldi.data.MeResponse
import com.farukerata.kackaldi.i18n.S
import java.time.Instant
import java.time.OffsetDateTime
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

class DevicesViewModel(private val api: KacKaldiApi) : ViewModel() {
    var me by mutableStateOf<Load<MeResponse>>(Load.Loading)
        private set
    var linkCode by mutableStateOf<LinkCodeResponse?>(null)
        private set
    var creating by mutableStateOf(false)
        private set
    var message by mutableStateOf<String?>(null)
        private set
    var confirming by mutableStateOf<Long?>(null)
    var removing by mutableStateOf(false)
        private set

    fun reload() {
        viewModelScope.launch { me = load { api.me() } }
    }

    fun createLinkCode() {
        viewModelScope.launch {
            creating = true
            message = null
            when (val created = load { api.createLinkCode() }) {
                is Load.Ready -> linkCode = created.value
                is Load.Failed -> message = created.message
                Load.Loading -> Unit
            }
            creating = false
        }
    }

    fun remove(device: DeviceView) {
        viewModelScope.launch {
            removing = true
            message = null
            try {
                api.removeDevice(device.id)
                reload()
            } catch (e: ApiException) {
                message = e.message
            } catch (e: Exception) {
                message = S.noServer
            } finally {
                removing = false
                confirming = null
            }
        }
    }
}

@Composable
fun DevicesScreen(viewModel: DevicesViewModel, onBack: () -> Unit) {
    LaunchedEffect(Unit) { viewModel.reload() }
    Column(
        Modifier
            .fillMaxSize()
            .background(Kit.colors.background)
            .windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal)),
    ) {
        Header(onBack = onBack) { Box(Modifier.padding(horizontal = 4.dp)) { HeaderTitle(S.devicesTitle) } }
        LazyColumn(Modifier.fillMaxSize(), contentPadding = WindowInsets.navigationBars.asPaddingValues()) {
            item { SectionTitle(S.addDevice) }
            item { LinkCodeCard(viewModel.linkCode, viewModel.creating, viewModel::createLinkCode) }
            viewModel.message?.let { item { Text(it, Modifier.padding(horizontal = 12.dp, vertical = 8.dp), style = Kit.sm, color = Kit.colors.destructive) } }
            item { SectionTitle(S.devices) }
            when (val state = viewModel.me) {
                Load.Loading -> item { RowsSkeleton(rows = 2) }
                is Load.Failed -> item { ErrorState(state.message, viewModel::reload) }
                is Load.Ready -> items(state.value.devices, key = { it.id }) { device -> DeviceRow(device, viewModel) }
            }
        }
    }
}

@Composable
private fun LinkCodeCard(code: LinkCodeResponse?, creating: Boolean, onCreate: () -> Unit) {
    val colors = Kit.colors
    val remaining = secondsLeft(code?.expiresAt)
    if (code != null && remaining > 0) {
        Column(
            Modifier.padding(horizontal = 12.dp).fillMaxWidth().border(1.dp, colors.border, RoundedCornerShape(14.dp)).padding(20.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            MonoCode(linkCodeInput(code.code))
            Text(S.expiresIn(countdown(remaining)), style = Kit.sm, color = colors.mutedForeground)
            Text(
                S.otherDeviceHint,
                style = Kit.sm,
                color = colors.mutedForeground,
                textAlign = TextAlign.Center,
            )
            CopyButton(linkCodeInput(code.code))
        }
        return
    }
    Column(Modifier.padding(horizontal = 12.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text(
            if (code != null) S.codeExpiredText else S.getCodeText,
            style = Kit.sm,
            color = colors.mutedForeground,
        )
        KitButton(if (creating) S.gettingCode else if (code != null) S.getNewCode else S.getCode, onCreate, enabled = !creating)
    }
}

@Composable
private fun CopyButton(text: String) {
    val context = LocalContext.current
    var copied by remember { mutableStateOf(false) }
    LaunchedEffect(copied) {
        if (!copied) return@LaunchedEffect
        delay(1_500)
        copied = false
    }
    KitButton(
        if (copied) S.copied else S.copy,
        {
            (context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("Link code", text))
            copied = true
        },
        kind = ButtonKind.Outline,
        icon = if (copied) Icons.Outlined.Check else Icons.Outlined.ContentCopy,
    )
}

@Composable
private fun secondsLeft(expiresAt: String?): Long {
    val now = rememberNow(1_000)
    val until = expiresAt?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() } ?: return 0
    return (until.toEpochMilli() - Instant.ofEpochMilli(now).toEpochMilli()) / 1000
}

@Composable
private fun DeviceRow(device: DeviceView, viewModel: DevicesViewModel) {
    val colors = Kit.colors
    val phone = device.platform == "android" || device.platform == "ios" || device.name.contains("Phone", ignoreCase = true)
    ItemRow(
        onClick = null,
        leading = { Icon(if (phone) Icons.Outlined.Smartphone else Icons.Outlined.DesktopWindows, null, Modifier.size(18.dp), tint = colors.mutedForeground) },
        trailing = if (device.current) null else ({ RemoveControls(device, viewModel) }),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ItemTitle(device.name, Modifier.weight(1f, fill = false))
            if (device.current) {
                Box(Modifier.height(18.dp).border(1.dp, colors.border, RowShape).background(colors.secondary, RowShape).padding(horizontal = 5.dp), contentAlignment = Alignment.Center) {
                    Text(S.thisDevice, style = Kit.xs.copy(fontWeight = FontWeight.Medium), color = colors.foreground)
                }
            }
        }
        ItemDescription(if (device.current) S.inUse else S.lastUsed(ago(secondsSince(device.lastSeenAt))))
    }
}

@Composable
private fun RemoveControls(device: DeviceView, viewModel: DevicesViewModel) {
    if (viewModel.confirming != device.id) {
        KitButton(S.remove, { viewModel.confirming = device.id }, kind = ButtonKind.Ghost)
        return
    }
    Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
        KitButton(S.cancel, { viewModel.confirming = null }, kind = ButtonKind.Ghost)
        KitButton(S.remove, { viewModel.remove(device) }, kind = ButtonKind.Destructive, enabled = !viewModel.removing)
    }
}
