package com.farukerata.kackaldi.watch

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.farukerata.kackaldi.KacKaldiApplication
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

class AlertReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val app = context.applicationContext as KacKaldiApplication
        val result = goAsync()
        CoroutineScope(Dispatchers.Default).launch {
            try {
                when (intent.action) {
                    ACTION_FIRE -> intent.getStringExtra(EXTRA_ID)?.let { AlertScheduler.fire(context, app.alerts, it) }
                    else -> AlertScheduler.sync(context, app.alerts)
                }
            } finally {
                result.finish()
            }
        }
    }

    companion object {
        const val ACTION_FIRE = "com.farukerata.kackaldi.action.FIRE_ALERT"
        const val EXTRA_ID = "com.farukerata.kackaldi.extra.ALERT_ID"
    }
}
