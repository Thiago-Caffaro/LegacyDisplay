package org.legacydisplay.client

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import java.io.File
import java.io.IOException

class MemoryStorage : Storage {
    val entries = mutableMapOf<String, String>()
    var fail = false
    override fun load(key: String) = entries[key]
    override fun save(key: String, value: String) {
        if (fail) throw IOException("Test storage failure")
        entries[key] = value
    }
}

class PanelTests {
    private val defaultLayout get() = File("../../protocol/examples/dashboard.json").readText()
    @Test fun unrelatedSensorUpdatesCannotRefreshAnOldBlackoutSignal() {
        val state = PanelState(MemoryStorage(), defaultLayout)
        state.update(JSONObject().put("pc.cpu.usage", 10))
        assertEquals(0L, state.lastBlackoutUpdateMillis)
        state.update(JSONObject().put("mining.blackout", true))
        val stamp = state.lastBlackoutUpdateMillis
        assertTrue(stamp > 0)
        state.update(JSONObject().put("pc.cpu.usage", 20))
        assertEquals(stamp, state.lastBlackoutUpdateMillis)
    }
    @Test fun sharedConformanceCases() {
        val cases = JSONArray(File("../../protocol/examples/layout-conformance.json").readText())
        for (i in 0 until cases.length()) {
            val case = cases.getJSONObject(i)
            val json = if (case.has("json")) case.getString("json") else case.getJSONObject("layout").toString()
            val result = runCatching { LayoutParser.parse(json) }
            assertEquals(case.getString("name"), case.getBoolean("valid"), result.isSuccess)
        }
    }
    @Test fun rejectedOrUnpersistedLayoutPreservesCurrentLayout() {
        val storage = MemoryStorage(); val state = PanelState(storage, defaultLayout)
        val before = state.layout
        assertTrue(runCatching { state.deploy("{}") }.isFailure)
        assertSame(before, state.layout)
        storage.fail = true
        assertTrue(runCatching { state.deploy(defaultLayout) }.isFailure)
        assertSame(before, state.layout)
    }
    @Test fun restoreAndCorruptFallback() {
        val storage = MemoryStorage(); val state = PanelState(storage, defaultLayout)
        val changed = JSONObject(defaultLayout).put("screen", JSONObject().put("width", 900).put("height", 1400)).toString()
        state.deploy(changed)
        assertEquals(900, PanelState(storage, defaultLayout).layout.screen.width)
        storage.entries["layout"] = "corrupt"
        assertEquals(800, PanelState(storage, defaultLayout).layout.screen.width)
    }
    @Test fun pairingExpiryRateLimitAndRevocation() {
        val storage = MemoryStorage(); var time = 1_000_000L
        val pairing = Pairing(storage) { time }
        val oldCode = pairing.displayedCode(); time += 300_001
        assertTrue(runCatching { pairing.pair(oldCode) }.isFailure)
        pairing.displayedCode()
        val token = pairing.pair(pairing.displayedCode())
        assertTrue(pairing.authorized("Bearer $token"))
        assertTrue(Pairing(storage) { time }.authorized("Bearer $token"))
        assertFalse(pairing.authorized(null))
        assertTrue(runCatching { pairing.pair(oldCode) }.isFailure)
        pairing.reset()
        assertFalse(pairing.authorized("Bearer $token"))
        repeat(5) { assertTrue(runCatching { pairing.pair("invalid") }.isFailure) }
        assertTrue(runCatching { pairing.pair(pairing.displayedCode()) }.isFailure)
        time += 60_001
        assertEquals(64, pairing.pair(pairing.displayedCode()).length)
    }
    @Test fun valuesAndActionsCannotInjectCommands() {
        val state = PanelState(MemoryStorage(), defaultLayout)
        state.update(JSONObject().put("pc.cpu.usage", 50))
        assertTrue(runCatching { state.update(JSONObject().put("pc.cpu.usage", JSONObject())) }.isFailure)
        assertEquals(50, state.values["pc.cpu.usage"])
        assertTrue(runCatching { state.invoke("ping") }.isFailure)
        state.setConnection(true)
        assertTrue(runCatching { state.invoke("title") }.isFailure)
        val action = state.invoke("ping")
        assertEquals("demo.ping", action.getString("action"))
        state.result(JSONObject().put("requestId", action.getString("requestId")).put("success", true).put("message", "ok"))
        assertEquals("ok", state.values["action.lastResult"])
        assertTrue(runCatching { state.result(JSONObject().put("requestId", action.getString("requestId"))) }.isFailure)
    }
}
