package com.extendscreen.ui

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.widget.ImageView

/**
 * JPEG Decoder and Renderer — Decoding performed on a background thread, rendering on the main thread
 * Use double buffering to prevent flickering: alternating between `decodeBitmap` and `displayBitmap`
 */
object VideoDecoderRenderer {

    private const val TAG = "JpegRenderer"
    private var imageView: ImageView? = null
    private var displayBitmap: Bitmap? = null
    private val mainHandler = Handler(Looper.getMainLooper())

    fun setImageView(view: ImageView) {
        imageView = view
    }

    fun renderFrame(frameData: ByteArray, offset: Int = 0, length: Int = frameData.size) {
        try {
            val bitmap = BitmapFactory.decodeByteArray(frameData, offset, length)
            if (bitmap != null) {
                val oldBitmap = displayBitmap
                displayBitmap = bitmap

                mainHandler.post {
                    imageView?.setImageBitmap(bitmap)
                    // Recycle the old Bitmap (the new Bitmap has already been set, so there will be no flickering)
                    oldBitmap?.recycle()
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Render error: ${e.message}")
        }
    }

    fun release() {
        displayBitmap?.recycle()
        displayBitmap = null
        imageView = null
    }
}
