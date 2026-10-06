package org.legacydisplay.client

import org.json.JSONObject
import java.io.IOException
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.UUID

interface Storage {
    fun load(key: String): String?
    fun save(key: String, value: String)
}

data class PanelConfig(val keepScreenOn: Boolean = true, val startOnBoot: Boolean = true) {
    fun toJson() = JSONObject().put("keepScreenOn", keepScreenOn).put("startOnBoot", startOnBoot)
    companion object {
        fun parse(json: String): PanelConfig {
            val root = JSONObject(json)
            root.keysOnly("keepScreenOn", "startOnBoot")
            require(root.get("keepScreenOn") is Boolean && root.get("startOnBoot") is Boolean) { "Config requires two booleans" }
            return PanelConfig(root.getBoolean("keepScreenOn"), root.getBoolean("startOnBoot"))
        }
    }
}

class Pairing(private val storage: Storage, private val now: () -> Long = System::currentTimeMillis) {
    private val random = SecureRandom()
    private var token = storage.load("token") ?: ""
    private var code = ""
    private var expires = 0L
    private var window = 0L
    private var attempts = 0
    @Synchronized fun paired() = token.isNotEmpty()
    @Synchronized fun displayedCode(): String {
        if (paired()) return ""
        if (now() >= expires) newCode()
        return code
    }
    private fun newCode() { code = String.format(java.util.Locale.US, "%06d", random.nextInt(1_000_000)); expires = now() + 300_000 }
    @Synchronized fun pair(candidate: String): String {
        check(!paired()) { "Already paired; reset locally on tablet" }
        if (now() - window >= 60_000) { window = now(); attempts = 0 }
        check(++attempts <= 5) { "Too many attempts; wait one minute" }
        // A code that has expired cannot be silently renewed by a remote request.
        require(code.isNotEmpty() && now() < expires && MessageDigest.isEqual(candidate.toByteArray(), code.toByteArray())) { "Invalid or expired code" }
        val bytes = ByteArray(32); random.nextBytes(bytes)
        val generated = bytes.joinToString("") { "%02x".format(it.toInt() and 255) }
        storage.save("token", generated)
        token = generated; code = ""
        return token
    }
    @Synchronized fun authorized(header: String?): Boolean {
        if (token.isEmpty() || header == null || !header.startsWith("Bearer ")) return false
        return MessageDigest.isEqual(header.removePrefix("Bearer ").toByteArray(), token.toByteArray())
    }
    @Synchronized fun reset() {
        storage.save("token", "")
        token = ""; attempts = 0; window = now(); newCode()
    }
}

class PanelState(private val storage: Storage, defaultLayout: String) {
    @Volatile var layout: Layout = try { LayoutParser.parse(storage.load("layout") ?: defaultLayout) } catch (_: Exception) { LayoutParser.parse(defaultLayout) }
        private set
    @Volatile var config = try { PanelConfig.parse(storage.load("config") ?: PanelConfig().toJson().toString()) } catch (_: Exception) { PanelConfig() }
        private set
    @Volatile var values: Map<String, Any?> = emptyMap()
        private set
    @Volatile var connected = false
    @Volatile var lastBlackoutUpdateMillis = 0L
    @Volatile var notice = "Aguardando Agent"
    @Volatile var listener: (() -> Unit)? = null
    val pairing = Pairing(storage)
    private val pending = mutableMapOf<String, Long>()

    @Synchronized fun deploy(json: String) {
        val parsed = LayoutParser.parse(json)
        storage.save("layout", parsed.json)
        layout = parsed; pending.clear()
        listener?.invoke()
    }
    @Synchronized fun configure(json: String) {
        val parsed = PanelConfig.parse(json)
        storage.save("config", parsed.toJson().toString())
        config = parsed
        listener?.invoke()
    }
    @Synchronized fun update(json: JSONObject) {
        require(json.length() <= 256) { "Too many data values" }
        val updated = values.toMutableMap()
        val keys = json.keys()
        while (keys.hasNext()) {
            val key = keys.next(); val value = json.get(key)
            require(LayoutParser.identifier.matches(key)) { "Invalid source id" }
            require(value == JSONObject.NULL || value is Boolean || value is String && value.length <= 256 || value is Number && value.toDouble().isFinite()) { "Invalid data value" }
            updated[key] = if (value == JSONObject.NULL) null else value
        }
        require(updated.size <= 256) { "Too many sources" }
        values = updated
        if (json.has("mining.blackout")) lastBlackoutUpdateMillis = System.currentTimeMillis()
        listener?.invoke()
    }
    @Synchronized fun invoke(widgetId: String): JSONObject {
        val widget = layout.widgets.firstOrNull { it.id == widgetId && it.type == "button" } ?: error("Invalid button")
        check(connected) { "PC desconectado" }
        val now = System.currentTimeMillis()
        pending.entries.removeAll { it.value <= now }
        check(pending.size < 32) { "Aguarde a resposta do PC" }
        val id = UUID.randomUUID().toString(); pending[id] = now + 10_000
        return JSONObject().put("type", "action.invoke").put("requestId", id).put("widgetId", widget.id).put("action", widget.action)
    }
    @Synchronized fun result(message: JSONObject) {
        val id = message.string("requestId") ?: error("Missing requestId")
        val expiry = pending.remove(id) ?: error("Unknown action result")
        require(expiry > System.currentTimeMillis()) { "Expired action result" }
        val text = message.string("message") ?: error("Missing message")
        require(text.length <= 256 && message.get("success") is Boolean) { "Invalid result" }
        values = values + ("action.lastResult" to text)
        notice = text
        listener?.invoke()
    }
    @Synchronized fun setConnection(value: Boolean) {
        connected = value; if (!value) pending.clear()
        notice = if (value) "PC conectado" else "PC offline · valores da última leitura"
        listener?.invoke()
    }
}
