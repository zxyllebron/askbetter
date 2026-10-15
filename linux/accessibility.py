"""AT-SPI adapter; deliberately uses window-relative geometry on both display servers."""
import re
import time
from dataclasses import dataclass
from crossplatform.geometry import Rect, near_model, to_shell, anchor

MODEL = re.compile(r"GPT[ .-]?[0-9]|(?:select|choose|change).*model|选择.*模型|切换.*模型|"
                   r"^(?:Auto|Automatic|Instant|Thinking|Pro|Fast|自动|快速|思考|ChatGPT|Codex)$", re.I)
APPLICATION = re.compile(r"^(codex|chatgpt)$", re.I)
COMPOSER = re.compile(r"^(?:Work with (?:Codex|ChatGPT)|Ask (?:Codex|ChatGPT|anything)|"
                      r"Message(?: (?:Codex|ChatGPT))?|Send a message|Type a message|输入(?:消息|提示词)|"
                      r"发送消息|向.*发送消息|询问.*|有什么可以帮忙的)[. …。]*$", re.I)
EXCLUDED = re.compile(r"search|find|reply|response|answer|rename|title|搜索|查找|回复|回答|重命名|标题", re.I)
REPLY = re.compile(r"^(?:Reply(?:\.{3}|…)?|Or write your own response|回复(?:\.{3}|…)?|或自行填写回复)$", re.I)


@dataclass
class Target:
    app: object
    frame: object
    editor: object
    model: object
    context: dict
    has_reply: bool = False
    obstacles: tuple = ()

    def identity(self):
        return (self.context["pid"], self.context["windowId"], self.context["title"],
                hash(self.frame), hash(self.editor))


class DesktopAccess:
    def __init__(self, api=None):
        if api is None:
            import pyatspi
            api = pyatspi
        self.api = api
        self.cached = None
        self.last_discovery = 0

    def _rectangle(self, node):
        value = node.queryComponent().getExtents(self.api.WINDOW_COORDS)
        return Rect(value.x, value.y, value.width, value.height)

    def _visible(self, node):
        state = node.getState()
        return (state.contains(self.api.STATE_SHOWING) and state.contains(self.api.STATE_ENABLED)
                and not state.contains(self.api.STATE_DEFUNCT))

    def read(self, target):
        return self._read_text(target.editor, set(), 0)

    def _read_text(self, node, ancestors, depth):
        if depth > 40 or hash(node) in ancestors:
            raise RuntimeError("Cannot safely read the rich-text draft; original preserved")
        attributes = node.get_attributes() if hasattr(node, "get_attributes") else {}
        if attributes.get("tag", "").startswith("::"):
            return ""  # Chromium exposes CSS placeholder text as an embedded object.
        if attributes.get("tag") == "p" and "placeholder" in attributes.get("class", "").split():
            return ""
        text = node.queryText()
        value = text.getText(0, text.characterCount)
        if "\ufffc" not in value:
            return self._without_trailing_break(node, attributes, value)
        try:
            hypertext = node.queryHypertext()
        except NotImplementedError:
            raise RuntimeError("Unreadable embedded object in draft; original preserved") from None
        ancestors = ancestors | {hash(node)}
        parts = []
        for offset, character in enumerate(value):
            if character != "\ufffc":
                parts.append(character)
                continue
            index = hypertext.getLinkIndex(offset)
            if index < 0:
                raise RuntimeError("Unreadable embedded object in draft; original preserved")
            link = hypertext.getLink(index)
            child = link.getObject(0)
            if child is None:
                raise RuntimeError("Unreadable embedded object in draft; original preserved")
            # Images/attachments have no text interface: refuse replacement rather
            # than dropping their content or optimizing an object placeholder.
            try:
                parts.append(self._read_text(child, ancestors, depth+1))
            except NotImplementedError:
                raise RuntimeError("Draft contains a non-text object; original preserved") from None
        if "ProseMirror" in attributes.get("class", "").split() and value and set(value) == {"\ufffc"}:
            # Each embedded root object is a paragraph/block. Chromium omits
            # paragraph separators from Hypertext; restore logical line breaks.
            return "\n".join(parts)
        return self._without_trailing_break(node, attributes, "".join(parts))

    def _without_trailing_break(self, node, attributes, value):
        if attributes.get("tag") == "p":
            for i in range(getattr(node, "childCount", 0)):
                child = node.getChildAtIndex(i)
                if child is None:
                    continue
                attrs = child.get_attributes()
                if "ProseMirror-trailingBreak" in attrs.get("class", "").split():
                    return value.removesuffix("\n")
        return value

    def _nodes(self, node):
        stack, seen = [node], set()
        while stack and len(seen) < 4000:
            current = stack.pop()
            if current is None:
                continue
            key = hash(current)
            if key in seen:
                continue
            seen.add(key)
            yield current
            try:
                stack.extend(current.getChildAtIndex(i) for i in range(min(current.childCount, 500)))
            except Exception:
                continue

    def _app(self, context):
        desktop = self.api.Registry.getDesktop(0)
        for index in range(desktop.childCount):
            app = desktop.getChildAtIndex(index)
            try:
                # GNOME identifies the foreground application; AT-SPI names may be
                # toolkit names such as Chromium rather than the desktop app name.
                if app.get_process_id() == context["pid"]:
                    return app
            except Exception:
                continue
        raise RuntimeError("The foreground Codex application is not exposed through AT-SPI")

    def discover(self, context, fresh=False):
        if not context or not isinstance(context.get("pid"), int) or context["pid"] <= 0:
            self.cached = None
            return None
        if not fresh and self.cached and self.cached.model is not None and time.monotonic()-self.last_discovery < 1:
            previous = self.cached.context
            if all(context.get(key) == previous.get(key) for key in ("pid", "windowId", "title")):
                try:
                    if self._visible(self.cached.editor) and (self.cached.model is None or self._visible(self.cached.model)):
                        self.cached.context = dict(context)
                        return self.cached
                except Exception:
                    pass
        app = self._app(context)
        frames = [app.getChildAtIndex(i) for i in range(app.childCount)]
        frames = [node for node in frames if node.getRoleName() in ("frame", "window", "dialog")]
        frames = [node for node in frames if self._visible(node) and node.name == context["title"]]
        if len(frames) > 1:
            # Electron exposes hidden auxiliary windows with the same title and
            # SHOWING state. Only accept a unique size match to GNOME's actual
            # foreground window; never select a frame by its draft contents.
            sizes = [context[key] for key in ("client", "buffer")
                     if isinstance(context.get(key), dict)]
            matches = []
            for candidate in frames:
                try:
                    rect = self._rectangle(candidate)
                    if any(size.get("width", 0) > 0 and size.get("height", 0) > 0
                           and abs(rect.width-size["width"]) <= 2
                           and abs(rect.height-size["height"]) <= 2 for size in sizes):
                        matches.append(candidate)
                except Exception:
                    continue
            frames = matches
        if len(frames) != 1:
            raise RuntimeError("Cannot uniquely identify the foreground Codex window")
        frame = frames[0]
        editors, models, controls = [], [], []
        for node in self._nodes(frame):
            try:
                if not self._visible(node):
                    continue
                role = node.getRoleName()
                rect = self._rectangle(node)
                if role in ("push button", "button", "combo box"):
                    controls.append(rect)
                    if MODEL.search(node.name or ""):
                        models.append((node, rect))
                elif role in ("text", "entry") and node.getState().contains(self.api.STATE_EDITABLE):
                    node.queryText()
                    editors.append((node, rect))
            except Exception:
                continue
        choices = [(bool(COMPOSER.fullmatch(editor.name or "")), model_rect.bottom,
                    -abs(model_rect.y-editor_rect.bottom), editor, model)
                   for editor, editor_rect in editors for model, model_rect in models
                   if not EXCLUDED.search(editor.name or "") and near_model(editor_rect, model_rect)]
        best = max(choices, key=lambda value: value[:3]) if choices else None
        labelled = [(node, rect) for node, rect in editors if COMPOSER.fullmatch(node.name or "")]
        if labelled and (best is None or not best[0]):
            editor, _ = max(labelled, key=lambda value: value[1].bottom)
            model = None
        elif best:
            _, _, _, editor, model = best
        else:
            raise RuntimeError("Codex input box/model selector not exposed through AT-SPI")
        editor_rect = self._rectangle(editor)
        has_reply = any(node != editor and (REPLY.fullmatch(node.name or "") or
                        (COMPOSER.fullmatch(editor.name or "") and rect.valid() and
                         0 <= editor_rect.y-rect.bottom <= 160 and
                         rect.x < editor_rect.right and rect.right > editor_rect.x))
                        for node, rect in editors)
        target = Target(app, frame, editor, model, dict(context), has_reply, tuple(controls))
        # Check the interface here; reading/validating rich content happens when
        # optimizing. A malformed draft must not make the button disappear.
        target.editor.queryText()
        self.cached, self.last_discovery = target, time.monotonic()
        return target

    def position(self, target, size=26, gap=8):
        context = target.context
        buffer = Rect(**context["buffer"])
        frame = self._rectangle(target.frame)
        client = Rect(**context.get("client", context["buffer"]))
        # Chromium's AT-SPI window excludes server-side decorations; GTK's
        # accessible frame includes them. Match the exposed dimensions first.
        if abs(frame.height-client.height) < abs(frame.height-buffer.height):
            buffer = client
        editor = to_shell(self._rectangle(target.editor), frame, buffer)
        if target.model is None:
            controls = [to_shell(rect, frame, buffer) for rect in target.obstacles]
            toolbar = sorted((rect for rect in controls if near_model(editor, rect)
                              and rect.x >= editor.x+editor.width/2 and rect.y >= editor.bottom-8), key=lambda rect: rect.x)
            candidates = [anchor(rect, size, gap) for rect in toolbar]
            candidates += [Rect(editor.right-gap-size, editor.bottom+gap, size, size),
                           Rect(editor.right-gap-size, editor.y-gap-size, size, size)]
            def overlaps(a, b):
                return a.x < b.right and a.right > b.x and a.y < b.bottom and a.bottom > b.y
            for rect in candidates:
                if (buffer.x <= rect.x and rect.right <= buffer.right and buffer.y <= rect.y and rect.bottom <= buffer.bottom
                        and not overlaps(rect, editor) and not any(overlaps(rect, other) for other in controls)):
                    return rect
            raise RuntimeError("No safe space beside the labelled composer")
        model = to_shell(self._rectangle(target.model), frame, buffer)
        if model.x >= editor.right and model.y < editor.bottom and model.bottom > editor.y:
            if target.has_reply:
                # A question card collapses the main composer to one row. Its
                # upper edge touches the card: keep the wand in the composer
                # beside its model picker, never float it into the reply card.
                return anchor(model, size, gap)
            # Compact ChatGPT puts the picker directly beside the text field.
            # Its normal left anchor would cover text; place it above the field.
            return Rect(model.x-gap-size, editor.y-gap-size, size, size)
        return anchor(model, size, gap)

    def unchanged(self, target, draft, context):
        try:
            current = self.discover(context, fresh=True)
            return current is not None and current.identity() == target.identity() and self.read(current) == draft
        except Exception:
            return False

    def replace(self, target, draft, result, context):
        if not self.unchanged(target, draft, context):
            raise RuntimeError("Input box or draft changed; the original was preserved")
        try:
            editable = target.editor.queryEditableText()
        except NotImplementedError:
            raise PasteRequired()
        try:
            accepted = editable.setTextContents(result)
        except NotImplementedError:
            raise PasteRequired()
        if accepted is False:
            raise RuntimeError("Codex did not accept the replacement; result available in the standalone dialog")
        if self.read(target) != result:
            raise RuntimeError("Unable to verify the replacement; check the draft before sending")

    def prepare_paste(self, target, draft, context):
        if not self.unchanged(target, draft, context):
            raise RuntimeError("Input box or draft changed; the original was preserved")
        if not target.editor.queryComponent().grabFocus():
            raise RuntimeError("Unable to focus the original input box")
        text = target.editor.queryText()
        if text.getNSelections():
            selected = text.setSelection(0, 0, text.characterCount)
        else:
            selected = text.addSelection(0, text.characterCount)
        if selected is False:
            raise RuntimeError("Unable to select the original draft safely")


class PasteRequired(Exception):
    """Chromium exposes text selection but no EditableText interface."""
