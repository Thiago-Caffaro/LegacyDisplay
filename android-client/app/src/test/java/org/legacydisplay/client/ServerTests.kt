package org.legacydisplay.client

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

class ServerTests {
    @Test fun realHttpServerChecksAuthAndPersistsJsonPut() {
        val state = PanelState(MemoryStorage(), File("../../protocol/examples/dashboard.json").readText())
        val server = PanelServer(state, 0)
        server.start(30_000, true)
        val token = state.pairing.pair(state.pairing.displayedCode())
        fun request(path: String, method: String = "GET", body: String? = null, authorized: Boolean = true): Pair<Int, String> {
            val connection = URL("http://127.0.0.1:${server.listeningPort}$path").openConnection() as HttpURLConnection
            connection.connectTimeout = 3000; connection.readTimeout = 3000; connection.requestMethod = method
            if (authorized) connection.setRequestProperty("Authorization", "Bearer $token")
            if (body != null) {
                connection.doOutput = true; connection.setRequestProperty("Content-Type", "application/json")
                connection.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
            }
            return try {
                val status = connection.responseCode
                val stream = if (status < 400) connection.inputStream else connection.errorStream
                status to stream.bufferedReader().use { it.readText() }
            } finally { connection.disconnect() }
        }
        try {
            assertEquals(200, request("/api/v1/status", authorized = false).first)
            assertEquals(401, request("/api/v1/layout", authorized = false).first)
            assertEquals(200, request("/api/v1/config", "PUT", "{\"keepScreenOn\":false,\"startOnBoot\":false}").first)
            assertFalse(state.config.keepScreenOn); assertFalse(state.config.startOnBoot)
            assertFalse(JSONObject(request("/api/v1/config").second).getBoolean("keepScreenOn"))
            assertEquals(400, request("/api/v1/layout", "POST", "{}").first)
            assertEquals(800, state.layout.screen.width)
            assertEquals(409, request("/api/v1/action", "POST", "{\"widgetId\":\"ping\"}").first)
            state.pairing.reset()
            assertEquals(401, request("/api/v1/layout").first)
        } finally { server.stop() }
    }
}
