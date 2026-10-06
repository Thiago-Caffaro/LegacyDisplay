package org.legacydisplay.client

import org.junit.Assert.*
import org.junit.Test

class BlackoutPolicyTest {
    @Test fun freshConfirmationDarkensAndOfflineStaleOrNonBooleanRestores() {
        val p = BlackoutPolicy()
        assertTrue(p.dark(true, true, 20_000, 19_000))
        assertFalse(p.dark(false, true, 20_000, 19_000))
        assertFalse(p.dark(true, true, 35_001, 20_000))
        assertFalse(p.dark(true, true, 20_000, 30_000))
        assertFalse(p.dark(true, "true", 20_000, 19_000))
        assertFalse(p.dark(true, false, 20_000, 19_000))
    }
    @Test fun wakeTemporarilyRevealsControlsThenReturnsToDarkWithoutChangingPcState() {
        val p = BlackoutPolicy()
        p.wake(20_000)
        assertFalse(p.dark(true, true, 34_999, 34_999))
        assertTrue(p.dark(true, true, 35_000, 35_000))
        p.wake(35_000)
        assertFalse(p.dark(true, true, 35_001, 35_001))
        assertFalse(p.dark(true, null, 60_000, 60_000))
    }
}
