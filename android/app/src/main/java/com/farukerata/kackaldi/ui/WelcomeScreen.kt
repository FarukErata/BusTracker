package com.farukerata.kackaldi.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.farukerata.kackaldi.data.ApiException
import com.farukerata.kackaldi.data.KacKaldiApi
import kotlinx.coroutines.launch

private enum class JoinMode(
    val lead: String,
    val label: String,
    val action: String,
    val busy: String,
    val wrong: String,
    val other: String,
    val wrongStatus: Int,
) {
    Invite(
        lead = "This server is private. Enter the invite code you were given.",
        label = "Invite code",
        action = "Join",
        busy = "Joining…",
        wrong = "That code doesn’t work. Check it and try again.",
        other = "Already using Kaç Kaldı on another device? Enter a link code",
        wrongStatus = 403,
    ),
    Link(
        lead = "On your other device, open Devices and tap Get a link code. Enter the code here.",
        label = "Link code",
        action = "Link",
        busy = "Linking…",
        wrong = "That code doesn’t work or has expired. Get a new one on the other device.",
        other = "I have an invite code instead",
        wrongStatus = 400,
    );

    fun format(value: String) = if (this == Link) linkCodeChars(value) else value

    fun submitted(value: String) = if (this == Link) linkCodeInput(value) else value

    fun isComplete(value: String) = if (this == Link) isCompleteLinkCode(value) else value.isNotBlank()
}

@Composable
fun WelcomeScreen(api: KacKaldiApi) {
    var mode by rememberSaveable { mutableStateOf(JoinMode.Invite) }
    var code by rememberSaveable { mutableStateOf("") }
    var pending by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    var autoTried by rememberSaveable { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    fun submit(value: String) {
        pending = true
        error = null
        scope.launch {
            try {
                val sent = mode.submitted(value)
                if (mode == JoinMode.Link) api.joinWithLinkCode(sent) else api.joinWithInvite(sent)
            } catch (e: ApiException) {
                error = if (e.status == mode.wrongStatus) mode.wrong else e.message
            } catch (e: Exception) {
                error = "Can’t reach the server. Check your connection."
            } finally {
                pending = false
            }
        }
    }

    fun change(value: String) {
        code = mode.format(value)
        error = null
        if (mode != JoinMode.Link || autoTried || !mode.isComplete(code)) return
        autoTried = true
        submit(code)
    }

    Scaffold { padding ->
        Column(
            Modifier.fillMaxSize().padding(padding).imePadding().padding(horizontal = 24.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text("Kaç Kaldı", style = MaterialTheme.typography.headlineMedium)
            Text(mode.lead, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Center)
            OutlinedTextField(
                value = code,
                onValueChange = ::change,
                label = { Text(mode.label) },
                singleLine = true,
                visualTransformation = if (mode == JoinMode.Link) LinkCodeTransformation else VisualTransformation.None,
                isError = error != null,
                supportingText = error?.let { { Text(it) } },
                textStyle = if (mode == JoinMode.Link) MaterialTheme.typography.titleLarge.copy(fontFamily = FontFamily.Monospace, textAlign = TextAlign.Center) else MaterialTheme.typography.bodyLarge,
                keyboardOptions = KeyboardOptions(
                    capitalization = if (mode == JoinMode.Link) KeyboardCapitalization.Characters else KeyboardCapitalization.None,
                    autoCorrectEnabled = false,
                    imeAction = ImeAction.Go,
                ),
                keyboardActions = KeyboardActions(onGo = { if (mode.isComplete(code) && !pending) submit(code) }),
                modifier = Modifier.fillMaxWidth(),
            )
            Button(onClick = { submit(code) }, enabled = !pending && mode.isComplete(code), modifier = Modifier.fillMaxWidth()) {
                Text(if (pending) mode.busy else mode.action)
            }
            TextButton(onClick = {
                mode = if (mode == JoinMode.Invite) JoinMode.Link else JoinMode.Invite
                code = ""
                error = null
            }) {
                Text(mode.other, textAlign = TextAlign.Center)
            }
        }
    }
}
