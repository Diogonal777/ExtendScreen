package com.extendscreen.service

import android.app.*
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Binder
import android.os.IBinder
import androidx.core.app.NotificationCompat
import com.extendscreen.ConnectionState
import com.extendscreen.ServerInfo
import com.extendscreen.network.DiscoveryClient
import com.extendscreen.network.InputSender
import com.extendscreen.network.VideoStreamClient
import kotlinx.coroutines.*

class StreamService : Service() {

    companion object {
        private const val CHANNEL_ID = "extendscreen_stream"
        private const val NOTIFICATION_ID = 1
    }

    inner class StreamBinder : Binder() {
        fun getService(): StreamService = this@StreamService
    }

    private val binder = StreamBinder()
    private val scope = CoroutineScope(Dispatchers.Main + SupervisorJob())
    private val discoveryClient = DiscoveryClient()
    private val videoClient = VideoStreamClient()
    private val inputSender = InputSender()

    var connectionState = ConnectionState.DISCONNECTED
        private set
    var connectedServer: ServerInfo? = null
        private set
    private var manualDisconnect = false

    var onStateChanged: ((ConnectionState) -> Unit)? = null

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        startForeground(NOTIFICATION_ID, createNotification("Waiting for connection..."),
            ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        // No longer connecting automatically; waiting for the user to select a mode.
        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder = binder

    /**
     * Wi-Fi mode: Starting search for computers
     */
    fun startWifiDiscovery() {
        manualDisconnect = false
        updateState(ConnectionState.CONNECTING)

        discoveryClient.onServerFound = { server ->
            if (connectionState != ConnectionState.CONNECTED && !videoClient.isConnecting) {
                connectedServer = server
                connectToServer(server)
            }
        }

        discoveryClient.startDiscovery(scope)
    }

    /**
     * Stop searching
     */
    fun stopDiscovery() {
        discoveryClient.stop()
    }

    fun connectManual(ip: String) {
        manualDisconnect = false
        discoveryClient.stop()
        videoClient.disconnect()
        inputSender.disconnect()
        updateState(ConnectionState.CONNECTING)

        val server = ServerInfo(ipAddress = ip, hostname = ip)
        connectedServer = server
        connectToServer(server)
    }

    fun connectUsb() {
        manualDisconnect = false
        discoveryClient.stop()
        videoClient.disconnect()
        inputSender.disconnect()
        updateState(ConnectionState.CONNECTING)

        // USB/ADB mode: Connect via ADB forward, using a forwarded port (to avoid conflicts with ports already being listened on by the computer).
        val server = ServerInfo(ipAddress = "127.0.0.1", videoPort = 22348, inputPort = 22347, hostname = "USB")
        connectedServer = server
        connectToServer(server)
    }

    private fun connectToServer(server: ServerInfo) {
        videoClient.onConnected = {
            discoveryClient.stop()
            updateState(ConnectionState.CONNECTED)
            startForeground(NOTIFICATION_ID, createNotification("Connected"),
                ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
            inputSender.connect(server, scope)
        }

        videoClient.onDisconnected = {
            inputSender.disconnect()
            if (manualDisconnect) {
                updateState(ConnectionState.DISCONNECTED)
            } else {
                updateState(ConnectionState.DISCONNECTED)
                // Reconnect based on the current mode in the event of an unexpected disconnection
                if (server.hostname == "USB") {
                    // USB mode does not automatically reconnect
                } else {
                    startWifiDiscovery()
                }
            }
        }

        videoClient.connect(server, scope)
    }

    private fun updateState(state: ConnectionState) {
        connectionState = state
        onStateChanged?.invoke(state)
    }

    fun sendTouchEvent(type: String, x: Float, y: Float, action: Int, pointerCount: Int) {
        inputSender.sendTouchEvent(type, x, y, action, pointerCount)
    }

    fun sendScrollEvent(deltaX: Float, deltaY: Float) {
        inputSender.sendScrollEvent(deltaX, deltaY)
    }

    fun sendZoomEvent(scale: Float) {
        inputSender.sendZoomEvent(scale)
    }

    fun disconnectAndRestart() {
        manualDisconnect = true
        discoveryClient.stop()
        videoClient.onConnected = null
        videoClient.onDisconnected = null
        videoClient.disconnect()
        inputSender.disconnect()
        connectedServer = null
        updateState(ConnectionState.DISCONNECTED)
    }

    fun disconnect() {
        discoveryClient.stop()
        videoClient.disconnect()
        inputSender.disconnect()
        updateState(ConnectionState.DISCONNECTED)
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun createNotificationChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            "ExtendScreen",
            NotificationManager.IMPORTANCE_LOW
        ).apply { description = "Screen Extension Service" }
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(channel)
    }

    private fun createNotification(text: String): Notification {
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("ExtendScreen")
            .setContentText(text)
            .setSmallIcon(android.R.drawable.ic_menu_view)
            .setOngoing(true)
            .build()
    }

    override fun onDestroy() {
        disconnect()
        scope.cancel()
        super.onDestroy()
    }
}
