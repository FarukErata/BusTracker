import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
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
    debugImplementation(libs.androidx.compose.ui.tooling)
}
