"""AT-SPI adapter; deliberately uses window-relative geometry on both display servers."""
import re
import time
from dataclasses import dataclass
from crossplatform.geometry import Rect, near_model, to_shell, anchor

MODEL = re.compile(r"GPT.?[56]|select model|选择模型", re.I)
APPLICATION = re.compile(r"^(codex|chatgpt)$", re.I)


@dataclass
class Target:
    app: object
    frame: object
    editor: object
    model: object
    context: dict

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
        text = target.editor.queryText()
        return text.getText(0, text.characterCount)

    def _nodes(self, node):
        stack, seen = [node], set()
        while stack and len(seen) < 4000:
            current = stack.pop()
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
                if app.get_process_id() == context["pid"] and APPLICATION.fullmatch(app.name or ""):
                    return app
            except Exception:
                continue
        raise RuntimeError("The foreground Codex application is not exposed through AT-SPI")

    def discover(self, context, fresh=False):
        if not context or not isinstance(context.get("pid"), int) or context["pid"] <= 0:
            self.cached = None
            return None
        if not fresh and self.cached and time.monotonic()-self.last_discovery < 1:
            previous = self.cached.context
            if all(context.get(key) == previous.get(key) for key in ("pid", "windowId", "title")):
                try:
                    if self._visible(self.cached.editor) and self._visible(self.cached.model):
                        self.cached.context = dict(context)
                        return self.cached
                except Exception:
                    pass
        app = self._app(context)
        frames = [app.getChildAtIndex(i) for i in range(app.childCount)]
        frames = [node for node in frames if node.getRoleName() in ("frame", "window", "dialog")]
        frames = [node for node in frames if self._visible(node) and node.name == context["title"]]
        if len(frames) != 1:
            raise RuntimeError("Cannot uniquely identify the foreground Codex window")
        frame = frames[0]
        editors, models = [], []
        for node in self._nodes(frame):
            try:
                if not self._visible(node):
                    continue
                role = node.getRoleName()
                rect = self._rectangle(node)
                if role in ("push button", "button", "combo box") and MODEL.search(node.name or ""):
                    models.append((node, rect))
                elif role in ("text", "entry") and node.getState().contains(self.api.STATE_EDITABLE):
                    node.queryText()
                    editors.append((node, rect))
            except Exception:
                continue
        choices = [(model_rect.bottom, -abs(model_rect.y-editor_rect.bottom), editor, model)
                   for editor, editor_rect in editors for model, model_rect in models
                   if near_model(editor_rect, model_rect)]
        if not choices:
            raise RuntimeError("Codex input box/model selector not exposed through AT-SPI")
        _, _, editor, model = max(choices, key=lambda value: value[:2])
        target = Target(app, frame, editor, model, dict(context))
        # A readable text interface is required. Never guess using clipboard contents.
        self.read(target)
        self.cached, self.last_discovery = target, time.monotonic()
        return target

    def position(self, target, size=26, gap=8):
        context = target.context
        buffer = Rect(**context["buffer"])
        model = to_shell(self._rectangle(target.model), self._rectangle(target.frame), buffer)
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
        editable = target.editor.queryEditableText()
        if editable.setTextContents(result) is False:
            raise RuntimeError("Codex did not accept the replacement; result available in the standalone dialog")
        if self.read(target) != result:
            raise RuntimeError("Unable to verify the replacement; check the draft before sending")
