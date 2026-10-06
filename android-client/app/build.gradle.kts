plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "org.legacydisplay.client"
    compileSdk = 35
    defaultConfig {
        applicationId = "org.legacydisplay.client"
        minSdk = 24
        // minSdk controls Android 7 support; boot/kiosk behavior on newer devices is a later milestone.
        targetSdk = 35
        versionCode = 2
        versionName = "0.2.0"
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_1_8
        targetCompatibility = JavaVersion.VERSION_1_8
    }
    kotlinOptions { jvmTarget = "1.8" }
    sourceSets.getByName("main").assets.srcDir("../../protocol/examples")
    testOptions { unitTests.isReturnDefaultValues = true }
}

dependencies {
    implementation("org.nanohttpd:nanohttpd-websocket:2.3.1")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
}

tasks.register("writePocClasspath") {
    dependsOn("testDebugUnitTest")
    doLast {
        val test = tasks.named<Test>("testDebugUnitTest").get()
        val destination = layout.buildDirectory.file("poc-classpath.txt").get().asFile
        destination.parentFile.mkdirs()
        destination.writeText(test.classpath.asPath)
    }
}

tasks.withType<Test>().configureEach {
    // These shared fixtures live outside this Gradle project and must invalidate cached test results.
    inputs.files(
        rootProject.file("../protocol/examples/dashboard.json"),
        rootProject.file("../protocol/examples/layout-conformance.json")
    ).withPropertyName("protocolFixtures")
}
