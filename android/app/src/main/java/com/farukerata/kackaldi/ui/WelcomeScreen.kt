package com.farukerata.kackaldi.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.em
import com.farukerata.kackaldi.data.ApiException
import com.farukerata.kackaldi.data.KacKaldiApi
import com.farukerata.kackaldi.i18n.S
import kotlinx.coroutines.launch

private enum class JoinMode(val wrongStatus: Int) {
    Invite(403),
    Link(400);

    val lead: String get() = if (this == Invite) S.inviteLead else S.linkLead
    val label: String get() = if (this == Invite) S.inviteCode else S.linkCode
    val action: String get() = if (this == Invite) S.join else S.link
    val busy: String get() = if (this == Invite) S.joining else S.linking
    val wrong: String get() = if (this == Invite) S.inviteWrong else S.linkWrong
    val other: String get() = if (this == Invite) S.haveLink else S.haveInvite
    val placeholder: String get() = if (this == Invite) S.inviteCode else "ABC-123"

    fun format(value: String) = if (this == Link) linkCodeChars(value) else value

    fun submitted(value: String) = if (this == Link) linkCodeInput(value) else value

    fun isComplete(value: String) = if (this == Link) isCompleteLinkCode(value) else value.isNotBlank()
}

@Composable
fun WelcomeScreen(api: KacKaldiApi, linkFromWeb: String?, onLinkUsed: () -> Unit) {
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
                error = friendlyError(e)
            } finally {
                pending = false
            }
        }
    }

    LaunchedEffect(linkFromWeb) {
        val incoming = linkFromWeb ?: return@LaunchedEffect
        onLinkUsed()
        mode = JoinMode.Link
        code = linkCodeChars(incoming)
        autoTried = true
        if (mode.isComplete(code)) submit(code)
    }

    fun change(value: String) {
        code = mode.format(value)
        error = null
        if (mode != JoinMode.Link || autoTried || !mode.isComplete(code)) return
        autoTried = true
        submit(code)
    }

    val colors = Kit.colors
    val link = mode == JoinMode.Link
    Column(
        Modifier
            .fillMaxSize()
            .background(colors.background)
            .windowInsetsPadding(WindowInsets.safeDrawing)
            .imePadding()
            .padding(horizontal = 24.dp),
        verticalArrangement = Arrangement.spacedBy(24.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Column(Modifier.widthIn(max = 384.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("Kaç Kaldı", style = Kit.xl2.copy(fontWeight = FontWeight.SemiBold, letterSpacing = Kit.tight), color = colors.foreground)
            Text(mode.lead, style = Kit.sm, color = colors.mutedForeground, textAlign = TextAlign.Center)
        }
        Column(Modifier.widthIn(max = 384.dp).fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            BasicTextField(
                value = code,
                onValueChange = ::change,
                singleLine = true,
                visualTransformation = if (link) LinkCodeTransformation else VisualTransformation.None,
                textStyle = if (link) {
                    Kit.lg.copy(fontFamily = FontFamily.Monospace, textAlign = TextAlign.Center, letterSpacing = 0.2.em, color = colors.foreground)
                } else {
                    Kit.base.copy(color = colors.foreground)
                },
                cursorBrush = SolidColor(colors.foreground),
                keyboardOptions = KeyboardOptions(
                    capitalization = if (link) KeyboardCapitalization.Characters else KeyboardCapitalization.None,
                    autoCorrectEnabled = false,
                    imeAction = ImeAction.Go,
                ),
                keyboardActions = KeyboardActions(onGo = { if (mode.isComplete(code) && !pending) submit(code) }),
                modifier = Modifier.fillMaxWidth(),
                decorationBox = { field ->
                    Box(
                        Modifier
                            .fillMaxWidth()
                            .height(40.dp)
                            .border(1.dp, if (error != null) colors.destructive else colors.input, FieldShape)
                            .padding(horizontal = 10.dp),
                        contentAlignment = if (link) Alignment.Center else Alignment.CenterStart,
                    ) {
                        if (code.isEmpty()) {
                            Text(
                                mode.placeholder,
                                style = if (link) Kit.lg.copy(fontFamily = FontFamily.Monospace, letterSpacing = 0.2.em) else Kit.base,
                                color = colors.mutedForeground,
                            )
                        }
                        field()
                    }
                },
            )
            error?.let { Text(it, Modifier.fillMaxWidth(), style = Kit.sm, color = colors.destructive, textAlign = TextAlign.Center) }
            KitButton(
                if (pending) mode.busy else mode.action,
                { submit(code) },
                Modifier.fillMaxWidth(),
                enabled = !pending && mode.isComplete(code),
                large = true,
            )
        }
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
            LinkText(mode.other, {
                mode = if (mode == JoinMode.Invite) JoinMode.Link else JoinMode.Invite
                code = ""
                error = null
            })
            Text(
                S.scannedQr,
                Modifier.widthIn(max = 320.dp),
                style = Kit.xs,
                color = colors.mutedForeground,
                textAlign = TextAlign.Center,
            )
        }
    }
}
