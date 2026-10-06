package org.legacydisplay.client

import fi.iki.elonen.NanoHTTPD
import fi.iki.elonen.NanoWSD
import org.json.JSONObject
import java.io.IOException
import java.io.File
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

class PanelServer(private val state: PanelState, port: Int = 8765, private val deviceName: String = "Android") : NanoWSD(port) {
    @Volatile private var live: LiveSocket? = null
    private val timer = Executors.newSingleThreadScheduledExecutor()
    init {
        timer.scheduleWithFixedDelay({
            try { live?.sendJson(JSONObject().put("type", "heartbeat")) } catch (_: IOException) { disconnect() }
        }, 5, 5, TimeUnit.SECONDS)
    }
    override fun serve(session: IHTTPSession): Response {
        if (session.headers["upgrade"]?.equals("websocket", true) == true) {
            if (session.uri != "/api/v1/live" || session.method != Method.GET) return response(Response.Status.NOT_FOUND, "Unknown route")
            if (!state.pairing.authorized(session.headers["authorization"])) return response(Response.Status.UNAUTHORIZED, "Pairing required")
            if (live != null) return response(Response.Status.CONFLICT, "Only one Agent is supported")
        }
        return super.serve(session)
    }
    override fun serveHttp(session: IHTTPSession): Response {
        try {
            if (session.uri == "/api/v1/status" && session.method == Method.GET) {
                return ok(JSONObject().put("protocolVersion", 1).put("name", deviceName).put("paired", state.pairing.paired())
                    .put("agentConnected", state.connected).put("screen", JSONObject().put("width", state.layout.screen.width).put("height", state.layout.screen.height)))
            }
            if (session.uri == "/api/v1/pair" && session.method == Method.POST) {
                val json = JSONObject(body(session)); json.keysOnly("code")
                val token = state.pairing.pair(json.string("code") ?: error("Missing code"))
                state.listener?.invoke()
                return ok(JSONObject().put("token", token))
            }
            if (!state.pairing.authorized(session.headers["authorization"])) return response(Response.Status.UNAUTHORIZED, "Pairing required")
            return when {
                session.uri == "/api/v1/layout" && session.method == Method.GET -> ok(JSONObject(state.layout.json))
                session.uri == "/api/v1/layout" && session.method == Method.POST -> {
                    state.deploy(body(session))
                    try { live?.sendJson(JSONObject().put("type", "layout.changed").put("layout", JSONObject(state.layout.json))) } catch (_: IOException) { disconnect() }
                    ok(JSONObject().put("saved", true))
                }
                session.uri == "/api/v1/config" && session.method == Method.GET -> ok(state.config.toJson())
                session.uri == "/api/v1/config" && session.method == Method.PUT -> { state.configure(body(session)); ok(state.config.toJson()) }
                session.uri == "/api/v1/action" && session.method == Method.POST -> {
                    val json = JSONObject(body(session)); json.keysOnly("widgetId")
                    performAction(json.string("widgetId") ?: error("Missing widgetId"))
                    ok(JSONObject().put("sent", true))
                }
                else -> response(Response.Status.NOT_FOUND, "Unknown route or method")
            }
        } catch (e: IllegalStateException) { return response(Response.Status.CONFLICT, e.message ?: "Conflict") }
        catch (e: IOException) { return response(Response.Status.INTERNAL_ERROR, "Storage or connection failure") }
        catch (_: Exception) { return response(Response.Status.BAD_REQUEST, "Invalid JSON or payload") }
    }
    private fun body(session: IHTTPSession): String {
        val length = session.headers["content-length"]?.toLongOrNull() ?: error("Content-Length required")
        require(length in 1..65536 && !session.headers.containsKey("transfer-encoding")) { "Body too large" }
        require(session.headers["content-type"]?.substringBefore(';')?.trim()?.equals("application/json", true) == true) { "JSON required" }
        val files = mutableMapOf<String, String>()
        session.parseBody(files)
        val body = if (session.method == Method.PUT) {
            File(files["content"] ?: error("Missing JSON body")).readText(Charsets.UTF_8)
        } else files["postData"] ?: error("Missing JSON body")
        require(body.toByteArray(Charsets.UTF_8).size <= 65536) { "Body too large" }
        return body
    }
    @Synchronized fun performAction(widgetId: String) {
        val socket = live ?: error("PC desconectado")
        socket.sendJson(state.invoke(widgetId))
    }
    @Synchronized fun disconnect() {
        val socket = live; live = null; state.setConnection(false)
        try { socket?.close(WebSocketFrame.CloseCode.GoingAway, "Connection reset", false) } catch (_: IOException) { }
    }
    override fun stop() { disconnect(); timer.shutdownNow(); super.stop() }
    override fun openWebSocket(handshake: IHTTPSession): WebSocket = LiveSocket(handshake)

    private inner class LiveSocket(private val request: IHTTPSession) : WebSocket(request) {
        override fun onOpen() {
            synchronized(this@PanelServer) {
                if (live != null || !state.pairing.authorized(request.headers["authorization"])) {
                    close(WebSocketFrame.CloseCode.PolicyViolation, "Connection rejected", false); return
                }
                live = this; state.setConnection(true)
            }
            sendJson(JSONObject().put("type", "hello").put("protocolVersion", 1).put("layout", JSONObject(state.layout.json)))
        }
        @Synchronized fun sendJson(json: JSONObject) { send(json.toString()) }
        override fun onMessage(message: WebSocketFrame) {
            try {
                require(message.opCode == WebSocketFrame.OpCode.Text && message.binaryPayload.size <= 65536) { "Invalid live frame" }
                require(live === this && state.pairing.authorized(request.headers["authorization"])) { "Credential revoked or session replaced" }
                val json = JSONObject(message.textPayload)
                when (json.string("type")) {
                    "data.update" -> { json.keysOnly("type", "values"); state.update(json.getJSONObject("values")) }
                    "action.result" -> { json.keysOnly("type", "requestId", "success", "message"); state.result(json) }
                    else -> error("Unknown live message")
                }
            } catch (_: Exception) {
                try { sendJson(JSONObject().put("type", "error").put("message", "Invalid live message")) } catch (_: IOException) { }
                close(WebSocketFrame.CloseCode.PolicyViolation, "Invalid live message", false)
            }
        }
        override fun onClose(code: WebSocketFrame.CloseCode?, reason: String?, initiatedByRemote: Boolean) {
            synchronized(this@PanelServer) { if (live === this) { live = null; state.setConnection(false) } }
        }
        override fun onPong(pong: WebSocketFrame) { }
        override fun onException(exception: IOException) { onClose(null, null, true) }
    }
    private fun ok(json: JSONObject) = newFixedLengthResponse(Response.Status.OK, "application/json; charset=utf-8", json.toString())
    private fun response(status: Response.Status, message: String) = newFixedLengthResponse(status, "application/json; charset=utf-8", JSONObject().put("error", message).toString())
}
