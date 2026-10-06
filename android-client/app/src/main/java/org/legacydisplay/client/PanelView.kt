package org.legacydisplay.client

import android.content.Context
import android.annotation.SuppressLint
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.text.Layout.Alignment
import android.text.StaticLayout
import android.text.TextPaint
import android.view.GestureDetector
import android.view.MotionEvent
import android.view.View
import java.text.DecimalFormat
import java.text.DecimalFormatSymbols
import java.util.Locale

@SuppressLint("ViewConstructor") // Constructed in Kotlin with runtime dependencies; never inflated from XML.
class PanelView(context: Context, private val state: PanelState, private val action: (String) -> Unit, private val settings: () -> Unit) : View(context) {
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val textPaint = TextPaint(Paint.ANTI_ALIAS_FLAG)
    private val rectangle = RectF()
    private data class CachedText(val text: String, val layout: StaticLayout)
    private val textCache = mutableMapOf<String, CachedText>()
    private var cachedLayout: Layout? = null
    private var scale = 1f; private var offsetX = 0f; private var offsetY = 0f
    private var pressed: String? = null; private var pressX = 0f; private var pressY = 0f
    private var longPress = false
    var banner = ""
    private val number = DecimalFormat("0.#", DecimalFormatSymbols(Locale.US))
    private val gestures = GestureDetector(context, object : GestureDetector.SimpleOnGestureListener() {
        override fun onDown(e: MotionEvent) = true
        override fun onLongPress(e: MotionEvent) {
            longPress = true
            if (buttonAt(e.x, e.y) == null) { pressed = null; settings() }
        }
    })
    init { isClickable = true; contentDescription = "Painel LegacyDisplay. Mantenha pressionada uma área vazia para configurar." }

    @SuppressLint("DrawAllocation") // StaticLayout is cached per widget; rebuilt only when its text or layout changes.
    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val layout = state.layout
        if (cachedLayout !== layout) { textCache.clear(); cachedLayout = layout }
        val data = state.values
        scale = minOf(width.toFloat() / layout.screen.width, height.toFloat() / layout.screen.height)
        offsetX = (width - layout.screen.width * scale) / 2; offsetY = (height - layout.screen.height * scale) / 2
        canvas.drawColor(Color.BLACK)
        canvas.save(); canvas.translate(offsetX, offsetY); canvas.scale(scale, scale)
        canvas.drawColor(Color.parseColor(layout.screen.background))
        for (widget in layout.widgets) {
            val style = widget.style
            canvas.save()
            canvas.clipRect(widget.x.toFloat(), widget.y.toFloat(), (widget.x + widget.width).toFloat(), (widget.y + widget.height).toFloat())
            rectangle.set(widget.x.toFloat(), widget.y.toFloat(), (widget.x + widget.width).toFloat(), (widget.y + widget.height).toFloat())
            paint.color = Color.parseColor(style.background); paint.alpha = (style.opacity * 255).toInt()
            canvas.drawRoundRect(rectangle, style.borderRadius.toFloat(), style.borderRadius.toFloat(), paint)
            if (widget.id == pressed || widget.type == "button" && !state.connected) {
                paint.color = Color.BLACK; paint.alpha = if (widget.id == pressed) 60 else 110
                canvas.drawRoundRect(rectangle, style.borderRadius.toFloat(), style.borderRadius.toFloat(), paint)
            }
            val value = data[widget.source]
            val displayed = when (value) { null -> "—"; is Number -> number.format(value); is Boolean -> if (value) "ON" else "OFF"; else -> value.toString() }
            val text = if (widget.type == "value") (widget.format ?: "{value}").replace("{value}", displayed) else widget.text ?: ""
            textPaint.color = Color.parseColor(style.color); textPaint.alpha = (style.opacity * 255).toInt()
            textPaint.textSize = style.fontSize.toFloat()
            textPaint.typeface = if (style.fontWeight == "bold") Typeface.DEFAULT_BOLD else Typeface.DEFAULT
            val alignment = when (style.alignment) { "center" -> Alignment.ALIGN_CENTER; "right" -> Alignment.ALIGN_OPPOSITE; else -> Alignment.ALIGN_NORMAL }
            val availableWidth = (widget.width - 2 * style.padding).coerceAtLeast(1)
            val cached = textCache[widget.id]
            val textLayout = if (cached?.text == text) cached.layout else {
                StaticLayout.Builder.obtain(text, 0, text.length, textPaint, availableWidth)
                    .setAlignment(alignment).setIncludePad(false).setMaxLines(8).build()
                    .also { textCache[widget.id] = CachedText(text, it) }
            }
            canvas.translate((widget.x + style.padding).toFloat(), widget.y + maxOf(style.padding.toFloat(), (widget.height - textLayout.height) / 2f))
            textLayout.draw(canvas)
            canvas.restore()
        }
        // Footer exposes pairing and stale data status without a browser or modal setup screen.
        paint.color = Color.parseColor("#101820"); paint.alpha = 240
        canvas.drawRect(0f, layout.screen.height - 48f, layout.screen.width.toFloat(), layout.screen.height.toFloat(), paint)
        textPaint.color = Color.parseColor("#A7BAC8"); textPaint.alpha = 255; textPaint.textSize = 19f; textPaint.typeface = Typeface.DEFAULT
        canvas.drawText(banner, 16f, layout.screen.height - 17f, textPaint)
        canvas.restore()
    }

    private fun buttonAt(x: Float, y: Float): Widget? {
        if (scale <= 0) return null
        val localX = (x - offsetX) / scale; val localY = (y - offsetY) / scale
        if (localY >= state.layout.screen.height - 48) return null
        // Last drawn widget wins hit testing, including non-interactive widgets occluding buttons.
        val hit = state.layout.widgets.asReversed().firstOrNull {
            localX >= it.x && localX < it.x + it.width && localY >= it.y && localY < it.y + it.height
        }
        return hit?.takeIf { it.type == "button" }
    }
    override fun onTouchEvent(event: MotionEvent): Boolean {
        gestures.onTouchEvent(event)
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> { longPress = false; pressed = buttonAt(event.x, event.y)?.id; pressX = event.x; pressY = event.y; invalidate() }
            MotionEvent.ACTION_MOVE -> if (kotlin.math.abs(event.x - pressX) + kotlin.math.abs(event.y - pressY) > 24 * resources.displayMetrics.density) { pressed = null; invalidate() }
            MotionEvent.ACTION_UP -> {
                val id = pressed
                if (!longPress && id != null && buttonAt(event.x, event.y)?.id == id) { performClick(); action(id) }
                pressed = null; invalidate()
            }
            MotionEvent.ACTION_CANCEL -> { pressed = null; invalidate() }
            MotionEvent.ACTION_POINTER_DOWN -> { pressed = null; invalidate() }
        }
        return true
    }
    override fun performClick(): Boolean { super.performClick(); return true }
}
