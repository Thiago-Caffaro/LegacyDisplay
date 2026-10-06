package org.legacydisplay.client

import android.app.Activity
import android.app.AlertDialog
import android.content.pm.ActivityInfo
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.View
import android.view.WindowManager
import android.view.MotionEvent
import android.graphics.Color
import android.widget.FrameLayout
import java.net.Inet4Address
import java.net.NetworkInterface

class MainActivity : Activity() {
    private val handler = Handler(Looper.getMainLooper())
    private lateinit var runtime: PanelApplication
    private lateinit var panel: PanelView
    private lateinit var blackoutView: View
    private val blackout = BlackoutPolicy()
    private var wakeGesture = false
    private var addressTicks = 0
    private var refreshPending = false
    private var dialogOpen = false
    private var address = "LAN:8765"
    private val refresh = Runnable { refreshPending = false; applySettings(); panel.invalidate() }
    private val tick = object : Runnable {
        override fun run() {
            if (addressTicks++ % 10 == 0) address = addresses()
            scheduleRefresh()
            handler.postDelayed(this, 1_000)
        }
    }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        runtime = application as PanelApplication
        panel = PanelView(this, runtime.state, { runtime.invoke(it) }, { showSettings() })
        blackoutView = View(this).apply { setBackgroundColor(Color.BLACK); visibility = View.GONE; importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO }
        setContentView(FrameLayout(this).apply {
            addView(panel, FrameLayout.LayoutParams(-1, -1))
            addView(blackoutView, FrameLayout.LayoutParams(-1, -1))
        })
        address = addresses()
        applySettings()
    }
    override fun onResume() {
        super.onResume(); immersive()
        blackout.wake(System.currentTimeMillis())
        runtime.state.listener = { handler.post { scheduleRefresh() } }
        handler.post(tick)
    }
    override fun onPause() {
        runtime.state.listener = null
        handler.removeCallbacks(tick)
        super.onPause()
    }
    override fun onDestroy() {
        runtime.state.listener = null; handler.removeCallbacksAndMessages(null)
        super.onDestroy()
    }
    override fun onWindowFocusChanged(hasFocus: Boolean) { super.onWindowFocusChanged(hasFocus); if (hasFocus) immersive() }
    @Suppress("DEPRECATION") // These flags implement immersive mode on the Android 7 target device.
    private fun immersive() {
        window.decorView.systemUiVisibility = View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or View.SYSTEM_UI_FLAG_FULLSCREEN or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
            View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or View.SYSTEM_UI_FLAG_LAYOUT_STABLE
    }
    private fun scheduleRefresh() {
        if (!refreshPending) { refreshPending = true; handler.postDelayed(refresh, 33) }
    }
    private fun applySettings() {
        val state = runtime.state
        val now = System.currentTimeMillis()
        val requested = blackout.requested(state.connected, state.values["mining.blackout"], now, state.lastBlackoutUpdateMillis)
        val dark = !dialogOpen && blackout.dark(state.connected, state.values["mining.blackout"], now, state.lastBlackoutUpdateMillis)
        blackoutView.visibility = if (dark) View.VISIBLE else View.GONE
        panel.visibility = if (dark) View.INVISIBLE else View.VISIBLE
        val attributes = window.attributes
        val brightness = if (dark) 0f else WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE
        if (attributes.screenBrightness != brightness) { attributes.screenBrightness = brightness; window.attributes = attributes }
        if (!dark && (state.config.keepScreenOn || requested || dialogOpen)) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        val orientation = if (state.layout.screen.width > state.layout.screen.height) ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE else ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
        if (requestedOrientation != orientation) requestedOrientation = orientation
        panel.banner = runtime.serverError ?: if (!state.pairing.paired()) "Código ${state.pairing.displayedCode()} · $address" else state.notice
    }
    override fun dispatchTouchEvent(event: MotionEvent): Boolean {
        if (event.actionMasked == MotionEvent.ACTION_DOWN) {
            wakeGesture = blackoutView.visibility == View.VISIBLE
            blackout.wake(System.currentTimeMillis()); applySettings()
        }
        if (wakeGesture) {
            if (event.actionMasked == MotionEvent.ACTION_UP || event.actionMasked == MotionEvent.ACTION_CANCEL) wakeGesture = false
            return true
        }
        return super.dispatchTouchEvent(event)
    }
    private fun showSettings() {
        if (dialogOpen) return
        dialogOpen = true
        val state = runtime.state
        val selected = booleanArrayOf(state.config.keepScreenOn, state.config.startOnBoot)
        val code = state.pairing.displayedCode()
        AlertDialog.Builder(this).setTitle("LegacyDisplay · configuração")
            .setMessage("$address\n${if (code.isEmpty()) "Pareado com o PC" else "Código: $code (válido por até 5 minutos)"}")
            .setPositiveButton("Salvar") { _, _ ->
                runtime.actions.execute {
                    try { state.configure(PanelConfig(selected[0], selected[1]).toJson().toString()) }
                    catch (_: Exception) { state.notice = "Falha ao salvar configuração"; state.listener?.invoke() }
                }
            }
            .setNeutralButton("Novo pareamento") { _, _ ->
                runtime.actions.execute {
                    try { state.pairing.reset(); runtime.server.disconnect() }
                    catch (_: Exception) { state.notice = "Falha ao limpar pareamento" }
                    state.listener?.invoke()
                }
            }
            .setNegativeButton("Fechar", null)
            .create().also { dialog ->
                // A message and multi-choice list do not coexist in Android's stock AlertDialog.
                val column = android.widget.LinearLayout(this).apply { orientation = android.widget.LinearLayout.VERTICAL; setPadding(32, 16, 32, 0) }
                listOf("Manter tela ligada", "Iniciar ao ligar (Android 7)").forEachIndexed { index, label ->
                    column.addView(android.widget.CheckBox(this).apply { text = label; isChecked = selected[index]; setOnCheckedChangeListener { _, checked -> selected[index] = checked } })
                }
                dialog.setView(column)
                dialog.setOnDismissListener { dialogOpen = false; applySettings(); immersive(); panel.invalidate() }
                dialog.show()
            }
    }
    private fun addresses(): String = try {
        val interfaces = NetworkInterface.getNetworkInterfaces().toList()
        val ips = interfaces.filter { it.isUp && !it.isLoopback }.flatMap { it.inetAddresses.toList() }
            .filterIsInstance<Inet4Address>().filter { !it.isLoopbackAddress }.mapNotNull { it.hostAddress }
        if (ips.isEmpty()) "Sem rede · porta 8765" else "${ips.first()}:8765"
    } catch (_: Exception) { "LAN:8765" }
}
