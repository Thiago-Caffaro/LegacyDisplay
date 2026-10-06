package org.legacydisplay.client

// A temporary wake grants time to use controls; stale/offline data cannot hold the panel dark.
class BlackoutPolicy {
    private var wakeUntil = 0L
    fun wake(now: Long) { wakeUntil = now + 15_000 }
    fun requested(connected: Boolean, value: Any?, now: Long, updated: Long): Boolean =
        connected && value == true && now - updated in 0..15_000
    fun dark(connected: Boolean, value: Any?, now: Long, updated: Long): Boolean =
        requested(connected, value, now, updated) && now >= wakeUntil
}
