package org.legacydisplay.client

import android.app.Application
import android.content.Context
import android.os.Build
import java.io.IOException
import java.util.concurrent.Executors

class PreferencesStorage(context: Context) : Storage {
    private val preferences = context.getSharedPreferences("panel", Context.MODE_PRIVATE)
    override fun load(key: String): String? = preferences.getString(key, null)
    override fun save(key: String, value: String) {
        if (!preferences.edit().putString(key, value).commit()) throw IOException("Could not persist settings")
    }
}

class PanelApplication : Application() {
    lateinit var state: PanelState
        private set
    lateinit var server: PanelServer
        private set
    val actions = Executors.newSingleThreadExecutor()
    var serverError: String? = null
        private set
    override fun onCreate() {
        super.onCreate()
        state = PanelState(PreferencesStorage(this), assets.open("dashboard.json").bufferedReader().use { it.readText() })
        state.pairing.displayedCode()
        server = PanelServer(state, deviceName = "${Build.MANUFACTURER} ${Build.MODEL}")
        try { server.start(30_000, true) }
        catch (_: IOException) { serverError = "Não foi possível abrir a porta 8765"; state.notice = serverError!!; server.stop() }
    }
    fun invoke(widgetId: String) {
        actions.execute {
            try { server.performAction(widgetId) }
            catch (e: Exception) {
                state.notice = if (e is IOException) "Falha na conexão com o PC" else e.message ?: "Ação indisponível"
                state.listener?.invoke()
            }
        }
    }
}
