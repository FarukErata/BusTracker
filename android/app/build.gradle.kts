import java.net.URI
import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
}

val keystore = Properties().apply {
    val file = File(System.getProperty("user.home"), ".android-keys/bustracker/keystore.properties")
    if (file.exists()) file.inputStream().use(::load)
}

fun signingValue(key: String, env: String): String? = keystore.getProperty(key) ?: System.getenv(env)

android {
    namespace = "com.farukerata.kackaldi"
    compileSdk = 37

    defaultConfig {
        applicationId = "com.farukerata.kackaldi"
        minSdk = 26
        targetSdk = 36
        versionCode = providers.gradleProperty("versionCode").map(String::toInt).getOrElse(1)
        versionName = providers.gradleProperty("versionName").getOrElse("0.1.0-dev")
        manifestPlaceholders["allowCleartext"] = providers.gradleProperty("allowCleartext").getOrElse("false")
        ndk { abiFilters += providers.gradleProperty("abis").getOrElse("arm64-v8a").split(",") }
        buildConfigField("String", "UPDATE_REPO", "\"FarukErata/BusTracker\"")
        val serverUrl = providers.gradleProperty("serverUrl").getOrElse("https://kackaldi.farukerata.com")
        manifestPlaceholders["linkHost"] = URI(serverUrl).host
        buildConfigField("String", "SERVER_URL", "\"$serverUrl\"")
    }

    signingConfigs {
        create("release") {
            signingValue("storeFile", "BT_KEYSTORE_FILE")?.let { storeFile = file(it) }
            storePassword = signingValue("storePassword", "BT_KEYSTORE_PASSWORD")
            keyAlias = signingValue("keyAlias", "BT_KEY_ALIAS")
            keyPassword = signingValue("keyPassword", "BT_KEY_PASSWORD")
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"))
            signingConfig = signingConfigs.getByName("release")
        }
    }

    packaging {
        jniLibs.useLegacyPackaging = true
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.compose.material.icons)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.navigation.compose)
    implementation(libs.androidx.datastore.preferences)
    implementation(libs.ktor.client.okhttp)
    implementation(libs.ktor.client.content.negotiation)
    implementation(libs.ktor.serialization.json)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.maplibre.android)
    debugImplementation(libs.androidx.compose.ui.tooling)
}
