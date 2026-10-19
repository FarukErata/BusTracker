package com.farukerata.kackaldi.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.outlined.Computer
import androidx.compose.material.icons.outlined.DeleteOutline
import androidx.compose.material.icons.outlined.Smartphone
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontFamily
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
            message = null
            try {
                api.removeDevice(device.id)
                if (device.current) api.signOutHere() else reload()
            } catch (e: ApiException) {
                message = e.message
            } catch (e: Exception) {
                message = "Can’t reach the server. Check your connection."
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DevicesScreen(viewModel: DevicesViewModel, onBack: () -> Unit) {
    LaunchedEffect(Unit) { viewModel.reload() }
    var confirming by remember { mutableStateOf<DeviceView?>(null) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Your devices") },
                navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Rounded.ArrowBack, contentDescription = "Back") } },
            )
        },
    ) { padding ->
        LazyColumn(Modifier.fillMaxSize().padding(padding)) {
            item { SectionTitle("Add a device") }
            item { LinkCodeCard(viewModel.linkCode, viewModel.creating, viewModel::createLinkCode) }
            viewModel.message?.let { item { Text(it, Modifier.padding(horizontal = 16.dp, vertical = 8.dp), color = MaterialTheme.colorScheme.error) } }
            item { SectionTitle("Signed in") }
            when (val state = viewModel.me) {
                Load.Loading -> item { LoadingState() }
                is Load.Failed -> item { ErrorState(state.message, viewModel::reload) }
                is Load.Ready -> {
                    val devices = state.value.devices
                    items(devices, key = { it.id }) { device ->
                        DeviceRow(device, canRemove = devices.size > 1) { confirming = device }
                    }
                }
            }
        }
    }

    confirming?.let { device ->
        AlertDialog(
            onDismissRequest = { confirming = null },
            title = { Text(if (device.current) "Sign out this phone?" else "Remove ${device.name}?") },
            text = {
                Text(
                    if (device.current) "You’ll need a link code from another device to sign in again."
                    else "It will be signed out. Your saved stops stay on your other devices.",
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    confirming = null
                    viewModel.remove(device)
                }) { Text(if (device.current) "Sign out" else "Remove") }
            },
            dismissButton = { TextButton(onClick = { confirming = null }) { Text("Cancel") } },
        )
    }
}

@Composable
private fun LinkCodeCard(code: LinkCodeResponse?, creating: Boolean, onCreate: () -> Unit) {
    val remaining = secondsLeft(code?.expiresAt)
    Surface(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
        shape = RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.surfaceContainer,
    ) {
        Column(
            Modifier.padding(20.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            if (code != null && remaining > 0) {
                Text(
                    linkCodeInput(code.code),
                    style = MaterialTheme.typography.displaySmall,
                    fontFamily = FontFamily.Monospace,
                    fontWeight = FontWeight.SemiBold,
                )
                Text("Expires in ${countdown(remaining)} · works once", color = MaterialTheme.colorScheme.onSurfaceVariant)
                Text(
                    "On the other device, open Kaç Kaldı and choose Enter a link code.",
                    textAlign = TextAlign.Center,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                OutlinedButton(onClick = onCreate, enabled = !creating) { Text("New code") }
            } else {
                Text(
                    if (code != null) "That code expired. Get a new one when the other device is ready."
                    else "Sign in on another phone or browser with your saved stops: get a code here and type it there.",
                    textAlign = TextAlign.Center,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Button(onClick = onCreate, enabled = !creating) { Text(if (creating) "Getting a code…" else "Get a link code") }
            }
        }
    }
}

@Composable
private fun secondsLeft(expiresAt: String?): Long {
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(expiresAt) {
        while (expiresAt != null) {
            now = System.currentTimeMillis()
            delay(1_000)
        }
    }
    val until = expiresAt?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() } ?: return 0
    return (until.toEpochMilli() - Instant.ofEpochMilli(now).toEpochMilli()) / 1000
}

@Composable
private fun DeviceRow(device: DeviceView, canRemove: Boolean, onRemove: () -> Unit) {
    ListItem(
        leadingContent = {
            Icon(
                if (device.platform == "android" || device.platform == "ios") Icons.Outlined.Smartphone else Icons.Outlined.Computer,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        },
        headlineContent = { Text(device.name) },
        supportingContent = { Text(if (device.current) "This device" else "Last used ${ago(secondsSince(device.lastSeenAt))}") },
        trailingContent = if (canRemove) {
            {
                IconButton(onClick = onRemove) {
                    Icon(Icons.Outlined.DeleteOutline, contentDescription = if (device.current) "Sign out this phone" else "Remove ${device.name}")
                }
            }
        } else null,
    )
}
