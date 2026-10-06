package org.legacydisplay.client

import org.json.JSONObject
import java.io.File

// Runs the actual Android protocol engine on the JVM for C#/Kotlin interoperability checks.
// No renderer, Android lifecycle or hardware performance is simulated by this fixture.
object PocTablet {
    @JvmStatic fun main(args: Array<String>) {
        val state = PanelState(MemoryStorage(), File("../protocol/examples/dashboard.json").readText())
        val server = PanelServer(state, 0, "JVM protocol fixture")
        server.start(30_000, true)
        println(JSONObject().put("port", server.listeningPort).put("code", state.pairing.displayedCode()))
        System.out.flush()
        try {
            generateSequence(::readLine).forEach { command ->
                when (command) {
                    "state" -> println(JSONObject().put("connected", state.connected).put("notice", state.notice).put("values", JSONObject(state.values)).put("widgets", state.layout.widgets.size))
                    "touch" -> { server.performAction("ping"); println("{\"sent\":true}") }
                    "touch-denied" -> { server.performAction("deny"); println("{\"sent\":true}") }
                    "disconnect" -> { server.disconnect(); println("{\"disconnected\":true}") }
                    "reset" -> { state.pairing.reset(); server.disconnect(); println(JSONObject().put("reset", true).put("code", state.pairing.displayedCode())) }
                    "stop" -> return
                }
                System.out.flush()
            }
        } finally { server.stop() }
    }
}
