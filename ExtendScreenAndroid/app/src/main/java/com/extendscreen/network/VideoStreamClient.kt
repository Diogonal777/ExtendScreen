package com.extendscreen.network

import android.util.Log
import com.extendscreen.ServerInfo
import com.extendscreen.ui.VideoDecoderRenderer
import kotlinx.coroutines.*
import java.io.InputStream
import java.net.Socket

/**
 * TCP Video Stream Client: Receives JPEG frames and decodes them on an I/O thread
 */
class VideoStreamClient {

    companion object {
        private const val TAG = "VideoStreamClient"
        private const val MAX_FRAME_SIZE = 5_000_000 // 5MB 最大帧
    }

    private var socket: Socket? = null
    private var inputStream: InputStream? = null
    private var isConnected = false
    private var _isConnecting = false
    val isConnecting: Boolean get() = _isConnecting
    private var job: Job? = null

    // Reuse frame buffers to reduce GC pressure
    private var frameBuffer = ByteArray(MAX_FRAME_SIZE)

    var onConnected: (() -> Unit)? = null
    var onDisconnected: (() -> Unit)? = null

    fun connect(server: ServerInfo, scope: CoroutineScope) {
        _isConnecting = true
        job = scope.launch(Dispatchers.IO) {
            try {
                Log.d(TAG, "Connecting to ${server.ipAddress}:${server.videoPort}")
                socket = Socket(server.ipAddress, server.videoPort)
                socket?.tcpNoDelay = true
                socket?.soTimeout = 5000
                socket?.receiveBufferSize = 512 * 1024
                inputStream = socket?.getInputStream()
                isConnected = true
                _isConnecting = false

                withContext(Dispatchers.Main) {
                    onConnected?.invoke()
                }

                Log.d(TAG, "Connected to video stream")
                receiveFrames()
            } catch (e: Exception) {
                Log.e(TAG, "Connection error: ${e.message}")
                _isConnecting = false
                withContext(Dispatchers.Main) {
                    onDisconnected?.invoke()
                }
            }
        }
    }

    private suspend fun receiveFrames() {
        val stream = inputStream ?: return
        val lengthBuffer = ByteArray(4)

        while (isConnected && isActive()) {
            try {
                // Read a 4-byte length header
                if (!readFully(stream, lengthBuffer, 4)) break

                val frameLength = ((lengthBuffer[0].toInt() and 0xFF) shl 24) or
                        ((lengthBuffer[1].toInt() and 0xFF) shl 16) or
                        ((lengthBuffer[2].toInt() and 0xFF) shl 8) or
                        (lengthBuffer[3].toInt() and 0xFF)

                if (frameLength <= 0 || frameLength > MAX_FRAME_SIZE) {
                    Log.w(TAG, "Invalid frame length: $frameLength")
                    continue
                }

                // Expand the buffer (if necessary)
                if (frameLength > frameBuffer.size) {
                    frameBuffer = ByteArray(frameLength)
                }

                // Read frame data into the multiplexing buffer
                if (!readFully(stream, frameBuffer, frameLength)) break

                // Decoding and rendering are performed directly on the IO thread (within `renderFrame`, a task is posted to the main thread to set the `Bitmap`).
                VideoDecoderRenderer.renderFrame(frameBuffer, 0, frameLength)

            } catch (e: java.net.SocketTimeoutException) {
                continue
            } catch (e: CancellationException) {
                break
            } catch (e: Exception) {
                Log.e(TAG, "Receive error: ${e.message}")
                break
            }
        }

        disconnect()
        withContext(Dispatchers.Main) {
            onDisconnected?.invoke()
        }
    }

    private fun isActive(): Boolean = job?.isActive == true

    private fun readFully(stream: InputStream, buffer: ByteArray, length: Int): Boolean {
        var totalRead = 0
        while (totalRead < length) {
            val bytesRead = stream.read(buffer, totalRead, length - totalRead)
            if (bytesRead < 0) return false
            totalRead += bytesRead
        }
        return true
    }

    fun disconnect() {
        isConnected = false
        _isConnecting = false
        job?.cancel()
        try {
            inputStream?.close()
            socket?.close()
        } catch (e: Exception) {
            Log.e(TAG, "Close error: ${e.message}")
        }
    }
}
