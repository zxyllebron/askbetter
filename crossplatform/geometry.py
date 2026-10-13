"""Coordinate and draft guards shared by the Linux adapter and offline checks."""
from dataclasses import dataclass


@dataclass(frozen=True)
class Rect:
    x: float
    y: float
    width: float
    height: float

    @property
    def right(self):
        return self.x + self.width

    @property
    def bottom(self):
        return self.y + self.height

    def valid(self):
        return self.width > 0 and self.height > 0


def near_model(editor, model):
    return (editor.valid() and model.valid() and editor.y - 8 <= model.y <= editor.bottom + 160
            and model.x < editor.right and model.right > editor.x)


def anchor(model, size=26, gap=8):
    return Rect(model.x-gap-size, model.y+(model.height-size)/2, size, size)


def to_shell(rect, accessible_window, buffer):
    """Map AT-SPI WINDOW-relative coordinates into GNOME logical coordinates."""
    if not accessible_window.valid() or not buffer.valid():
        raise ValueError("Window dimensions unavailable")
    sx, sy = buffer.width/accessible_window.width, buffer.height/accessible_window.height
    return Rect(buffer.x+(rect.x-accessible_window.x)*sx,
                buffer.y+(rect.y-accessible_window.y)*sy, rect.width*sx, rect.height*sy)


class LayoutGate:
    def __init__(self, settle_seconds=0.3):
        self.settle_seconds = settle_seconds
        self.signature = None
        self.changed_at = 0
        self.moving = False

    def update(self, signature, now, moving=False):
        if signature != self.signature or moving:
            self.signature, self.changed_at = signature, now
        self.moving = moving
        return not moving and now-self.changed_at >= self.settle_seconds


def same_target(before, after, original, current):
    return before == after and original == current
