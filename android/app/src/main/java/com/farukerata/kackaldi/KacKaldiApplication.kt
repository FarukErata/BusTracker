package com.farukerata.kackaldi

import android.app.Application
import com.farukerata.kackaldi.data.DeviceStore
import com.farukerata.kackaldi.data.KacKaldiApi

class KacKaldiApplication : Application() {
    val deviceStore by lazy { DeviceStore(this) }
    val api by lazy { KacKaldiApi(deviceStore) }
}
