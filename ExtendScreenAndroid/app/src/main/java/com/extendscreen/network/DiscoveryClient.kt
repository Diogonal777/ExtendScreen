package com.extendscreen.network

import com.extendscreen.ServerInfo
import kotlinx.coroutines.*
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress

/**
 * UDP Client: Listens for broadcasts from the Windows server to automatically discover ExtendScreen Server
 * 
 * Workflow:
 * 1. Windows side broadcasts "ExtendScreenServer" to 255.255.255.255:12345
 * 2. Android side binds to port 12345 to receive the broadcast
 * 3. Simultaneously, the Android side broadcasts "ExtendScreenClient" to 12346, which the Windows side listens to
 * 4. Upon receiving the broadcast, it sends device information back to the server on port 12346
 */

class DiscoveryClient {

    companion object {
        private const val DISCOVERY_PORT = 12345
        private const val RESPONSE_PORT = 12346   // Return Port = DISCOVERY_PORT + 1
        private const val BROADCAST_MESSAGE = "ExtendScreenServer"
        private const val CLIENT_BROADCAST = "ExtendScreenClient"
        private const val TAG = "DiscoveryClient"
    }

    private var socket: DatagramSocket? = null
    private var isScanning = false
    private var job: Job? = null

    var onServerFound: ((ServerInfo) -> Unit)? = null

    fun startDiscovery(scope: CoroutineScope) {
        isScanning = true
        job = scope.launch(Dispatchers.IO) {
            try {
                // Bind to port 12345 to receive UDP broadcasts from Windows
                val addr = InetSocketAddress(DISCOVERY_PORT)
                socket = DatagramSocket(null)
                socket!!.reuseAddress = true
                socket!!.broadcast = true
                socket!!.soTimeout = 2000
                socket!!.bind(addr)

                android.util.Log.d(TAG, "Discovery listening on port $DISCOVERY_PORT")

                val buffer = ByteArray(1024)
                while (isScanning && socket != null) {
                    // Simultaneously send a client broadcast so that the Windows side can also discover us
                    try {
                        val clientBroadcast = CLIENT_BROADCAST.toByteArray()
                        val broadcastAddr = InetAddress.getByName("255.255.255.255")
                        val broadcastPacket = DatagramPacket(clientBroadcast, clientBroadcast.size, broadcastAddr, RESPONSE_PORT)
                        socket!!.send(broadcastPacket)
                        android.util.Log.d(TAG, "Sent client broadcast")
                    } catch (e: Exception) {
                        android.util.Log.w(TAG, "Broadcast send error: ${e.message}")
                    }

                    try {
                        val packet = DatagramPacket(buffer, buffer.size)
                        socket?.receive(packet)
                        val message = String(packet.data, 0, packet.length)

                        android.util.Log.d(TAG, "Received: $message from ${packet.address.hostAddress}")

                        if (message.trim() == BROADCAST_MESSAGE) {
                            val serverIp = packet.address.hostAddress ?: continue
                            android.util.Log.d(TAG, "Found ExtendScreen Server at $serverIp")
                            sendDeviceInfo(serverIp)
                        }
                    } catch (e: java.net.SocketTimeoutException) {
                        // Continue waiting after timeout
                        continue
                    } catch (e: Exception) {
                        if (isScanning) {
                            android.util.Log.e(TAG, "Receive error: ${e.message}")
                        }
                    }
                }
            } catch (e: Exception) {
                android.util.Log.e(TAG, "Discovery bind error: ${e.message}")
            }
        }
    }

    private fun sendDeviceInfo(serverIp: String) {
        try {
            val deviceName = android.os.Build.MODEL
            val metrics = android.content.res.Resources.getSystem().displayMetrics
            val info = "DEVICE_INFO|$deviceName|${metrics.widthPixels}|${metrics.heightPixels}"

            val responseSocket = DatagramSocket()
            val addr = InetAddress.getByName(serverIp)
            val data = info.toByteArray()
            val packet = DatagramPacket(data, data.size, addr, RESPONSE_PORT)
            responseSocket.send(packet)
            responseSocket.close()

            android.util.Log.d(TAG, "Sent device info to $serverIp:$RESPONSE_PORT")

            onServerFound?.invoke(
                ServerInfo(ipAddress = serverIp, hostname = serverIp)
            )
        } catch (e: Exception) {
            android.util.Log.e(TAG, "Send device info error: ${e.message}")
        }
    }

    fun stop() {
        isScanning = false
        job?.cancel()
        try { socket?.close() } catch (_: Exception) {}
        socket = null
    }
}
