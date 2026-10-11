import java.util.Base64

plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

// Integration tests must never install or uninstall the user's application.
val integrationDefines = providers.gradleProperty("dart-defines").orNull.orEmpty()
    .split(',')
    .filter { it.isNotBlank() }
    .map { String(Base64.getDecoder().decode(it), Charsets.UTF_8) }
val integrationTarget = integrationDefines.any {
    it.startsWith("INTEGRATION_TEST_SHOULD_REPORT_RESULTS_TO_NATIVE=")
} || providers.gradleProperty("target").orNull.orEmpty()
    .replace('\\', '/')
    .split('/')
    .contains("integration_test")

// CI and explicit public builds require the previously configured durable key.
val releaseStore = providers.environmentVariable("ANDROID_KEYSTORE_PATH").orNull
val releaseAlias = providers.environmentVariable("ANDROID_KEY_ALIAS").orNull
val releaseKeyPassword = providers.environmentVariable("ANDROID_KEY_PASSWORD").orNull
val releaseStorePassword = providers.environmentVariable("ANDROID_STORE_PASSWORD").orNull
val hasReleaseSigning = listOf(releaseStore, releaseAlias, releaseKeyPassword, releaseStorePassword).all { !it.isNullOrBlank() }
val requiresReleaseSigning = providers.environmentVariable("MONOLITH_REQUIRE_RELEASE_SIGNING").orNull == "true"
if (requiresReleaseSigning && !hasReleaseSigning) error("All four Android signing variables are required for a public build.")

android {
    namespace = "com.monolithharness.monolith_harness_app_flutter"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        // TODO: Specify your own unique Application ID (https://developer.android.com/studio/build/application-id.html).
        applicationId = "com.monolithharness.flutter"
        manifestPlaceholders["monolithApplicationLabel"] = if (integrationTarget) "Monolith QA" else "Monolith"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = 26
        targetSdk = flutter.targetSdkVersion
        // Uses the version code from pubspec.yaml. When using split APKs, 1000 * ABI_VERSION
        // is added automatically by Flutter. (https://developer.android.com/studio/build/configure-apk-splits#configure-APK-versions)
        // You can force using the value of versionCode by specifying the `-P force-version-code-ignoring-abi=true`
        // flag during build.
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    signingConfigs {
        if (hasReleaseSigning) create("durableRelease") {
            storeFile = file(releaseStore!!)
            keyAlias = releaseAlias
            keyPassword = releaseKeyPassword
            storePassword = releaseStorePassword
        }
    }

    buildTypes {
        configureEach {
            if (integrationTarget) applicationIdSuffix = ".qa"
        }
        release {
            signingConfig = signingConfigs.getByName(if (hasReleaseSigning) "durableRelease" else "debug")
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source = "../.."
}
