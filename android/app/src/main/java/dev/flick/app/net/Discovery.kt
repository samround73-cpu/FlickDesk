package dev.flick.app.net

import android.content.Context
import android.net.wifi.WifiManager
import dev.flick.app.NearbyPc
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import org.json.JSONException
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.SocketException
import java.net.SocketTimeoutException

/** Listens for FlickDesk's UDP beacons and emits the PCs currently visible on the network. */
class Discovery(private val context: Context) {

    private class Seen(val pc: NearbyPc, val at: Long)

    fun pcs(): Flow<List<NearbyPc>> = callbackFlow {
        val wifi = context.applicationContext.getSystemService(WifiManager::class.java)
        val lock = wifi?.createMulticastLock("flick-discovery")?.apply {
            setReferenceCounted(false)
            acquire()
        }
        val socket = DatagramSocket(null).apply {
            reuseAddress = true
            broadcast = true
            soTimeout = 1000
            bind(InetSocketAddress(Protocol.BEACON_PORT))
        }

        val job = launch(Dispatchers.IO) {
            val seen = LinkedHashMap<String, Seen>()
            val buf = ByteArray(2048)
            while (isActive) {
                val packet = DatagramPacket(buf, buf.size)
                try {
                    socket.receive(packet)
                    val json = JSONObject(String(packet.data, 0, packet.length, Charsets.UTF_8))
                    val address = packet.address?.hostAddress
                    if (json.optString("app") == "flick" && json.optString("type") == "beacon" &&
                        json.optInt("v") >= Protocol.VERSION && address != null
                    ) {
                        val id = json.getString("id")
                        seen[id] = Seen(
                            NearbyPc(
                                id = id,
                                name = json.optString("name", "PC"),
                                address = address,
                                port = json.optInt("port", Protocol.PC_PORT),
                                paused = json.optBoolean("paused"),
                            ),
                            System.currentTimeMillis(),
                        )
                    }
                } catch (_: SocketTimeoutException) {
                    // fall through to prune
                } catch (_: JSONException) {
                    continue
                } catch (_: SocketException) {
                    break // socket closed
                }

                val now = System.currentTimeMillis()
                seen.entries.removeAll { now - it.value.at > 8_000 }
                trySend(seen.values.map { it.pc })
            }
        }

        awaitClose {
            job.cancel()
            socket.close()
            lock?.release()
        }
    }.distinctUntilChanged()
}
