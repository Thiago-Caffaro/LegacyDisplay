package org.legacydisplay.client

import org.json.JSONObject

data class Screen(val width: Int, val height: Int, val background: String)
data class Style(
    val background: String = "#101820", val color: String = "#FFFFFF", val fontSize: Int = 36,
    val fontWeight: String = "normal", val alignment: String = "left", val borderRadius: Int = 0,
    val padding: Int = 12, val opacity: Float = 1f
)
data class Widget(
    val id: String, val type: String, val x: Int, val y: Int, val width: Int, val height: Int,
    val text: String?, val source: String?, val format: String?, val action: String?, val style: Style
)
data class Layout(val screen: Screen, val widgets: List<Widget>, val json: String)

object LayoutParser {
    val identifier = Regex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$")
    private val color = Regex("^#[0-9a-fA-F]{6}$")

    fun parse(json: String): Layout {
        require(json.toByteArray(Charsets.UTF_8).size <= 65536) { "Layout exceeds 64 KiB" }
        val root = JSONObject(json)
        root.keysOnly("version", "screen", "widgets")
        require(root.integer("version") == 1) { "Unsupported layout version" }
        val screenJson = root.getJSONObject("screen")
        screenJson.keysOnly("width", "height", "background")
        val screen = Screen(screenJson.integer("width"), screenJson.integer("height"), screenJson.string("background", "#101820")!!)
        require(screen.width in 1..4096 && screen.height in 1..4096 && color.matches(screen.background)) { "Invalid screen" }
        val items = root.getJSONArray("widgets")
        require(items.length() <= 128) { "At most 128 widgets" }
        val ids = mutableSetOf<String>()
        val widgets = (0 until items.length()).map { index ->
            val item = items.getJSONObject(index)
            item.keysOnly("id", "type", "x", "y", "width", "height", "text", "source", "format", "action", "style")
            val id = item.string("id") ?: error("Missing id")
            require(identifier.matches(id) && ids.add(id)) { "Invalid or duplicate id" }
            val type = item.string("type")!!
            require(type in listOf("text", "value", "button")) { "Unsupported widget type" }
            val x = item.integer("x"); val y = item.integer("y")
            val width = item.integer("width"); val height = item.integer("height")
            require(x >= 0 && y >= 0 && width > 0 && height > 0 && x.toLong() + width <= screen.width && y.toLong() + height <= screen.height) { "Widget outside screen" }
            val text = item.string("text"); val source = item.string("source")
            val format = item.string("format"); val action = item.string("action")
            require(text == null || text.length <= 256) { "Text too long" }
            require(format == null || format.length <= 128) { "Format too long" }
            require(source == null || identifier.matches(source)) { "Invalid source" }
            if (type == "value") require(source != null && identifier.matches(source)) { "Value requires source" }
            else require(text != null) { "Text/Button requires text" }
            if (type == "button") require(action != null && identifier.matches(action)) { "Button requires action" }
            else require(action == null) { "Only buttons can have actions" }
            val styleJson = if (item.has("style")) item.getJSONObject("style") else JSONObject()
            styleJson.keysOnly("background", "color", "fontSize", "fontWeight", "alignment", "borderRadius", "padding", "opacity")
            val opacity = if (styleJson.has("opacity")) styleJson.get("opacity").let { require(it is Number); it.toDouble() } else 1.0
            val style = Style(styleJson.string("background", "#101820")!!, styleJson.string("color", "#FFFFFF")!!,
                styleJson.integer("fontSize", 36), styleJson.string("fontWeight", "normal")!!,
                styleJson.string("alignment", "left")!!, styleJson.integer("borderRadius", 0), styleJson.integer("padding", 12), opacity.toFloat())
            require(color.matches(style.background) && color.matches(style.color)) { "Invalid style color" }
            require(style.fontSize in 1..200 && style.padding in 0..100 && style.borderRadius in 0..200 && opacity.isFinite() && opacity in 0.0..1.0) { "Invalid style dimensions" }
            require(style.fontWeight in listOf("normal", "bold") && style.alignment in listOf("left", "center", "right")) { "Invalid font style" }
            Widget(id, type, x, y, width, height, text, source, format, action, style)
        }
        return Layout(screen, widgets, root.toString())
    }
}

internal fun JSONObject.keysOnly(vararg allowed: String) {
    val keys = keys()
    while (keys.hasNext()) require(keys.next() in allowed) { "Unknown JSON field" }
}
internal fun JSONObject.integer(key: String, default: Int? = null): Int {
    if (!has(key) && default != null) return default
    val value = get(key)
    require((value is Int || value is Long) && (value as Number).toLong() in Int.MIN_VALUE.toLong()..Int.MAX_VALUE.toLong()) { "$key must be an integer literal" }
    return value.toInt()
}
internal fun JSONObject.string(key: String, default: String? = null): String? {
    if (!has(key)) return default
    val value = get(key)
    if (value == JSONObject.NULL) return null
    require(value is String) { "$key must be a string" }
    return value
}
