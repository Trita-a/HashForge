from __future__ import annotations

from PySide6.QtCore import QPoint, QRect, QSize, Qt
from PySide6.QtWidgets import QLayout, QSizePolicy, QLayoutItem


class FlowLayout(QLayout):
    """A responsive layout that wraps its child widgets like text flow."""

    def __init__(self, parent=None, margin: int = 0, spacing: int = 10) -> None:
        super().__init__(parent)
        self._items: list[QLayoutItem] = []
        self.setContentsMargins(margin, margin, margin, margin)
        self.setSpacing(spacing)

    def addItem(self, item: QLayoutItem) -> None:  # pragma: no cover - simple container
        self._items.append(item)

    def count(self) -> int:  # pragma: no cover - trivial
        return len(self._items)

    def itemAt(self, index: int):  # pragma: no cover - trivial
        if 0 <= index < len(self._items):
            return self._items[index]
        return None

    def takeAt(self, index: int):  # pragma: no cover - trivial
        if 0 <= index < len(self._items):
            return self._items.pop(index)
        return None

    def expandingDirections(self):  # pragma: no cover - simple
        return Qt.Orientations()

    def hasHeightForWidth(self) -> bool:
        return True

    def heightForWidth(self, width: int) -> int:
        return self._do_layout(QRect(0, 0, width, 0), test_only=True)

    def setGeometry(self, rect: QRect) -> None:
        super().setGeometry(rect)
        self._do_layout(rect, test_only=False)

    def sizeHint(self) -> QSize:  # pragma: no cover - simple
        return self.minimumSize()

    def minimumSize(self) -> QSize:
        size = QSize()
        for item in self._items:
            size = size.expandedTo(item.minimumSize())
        left, top, right, bottom = self.getContentsMargins()
        size += QSize(left + right, top + bottom)
        return size

    def _do_layout(self, rect: QRect, *, test_only: bool) -> int:
        left, top, right, bottom = self.getContentsMargins()
        effective_rect = rect.adjusted(left, top, -right, -bottom)
        x = effective_rect.x()
        y = effective_rect.y()
        line_height = 0

        for item in self._items:
            widget = item.widget()
            if widget is None or not widget.isVisible():
                continue

            space_x = self.spacing() + widget.style().layoutSpacing(
                QSizePolicy.PushButton,
                QSizePolicy.PushButton,
                Qt.Horizontal,
            )
            space_y = self.spacing() + widget.style().layoutSpacing(
                QSizePolicy.PushButton,
                QSizePolicy.PushButton,
                Qt.Vertical,
            )
            next_x = x + item.sizeHint().width() + space_x
            if next_x - space_x > effective_rect.right() and line_height > 0:
                x = effective_rect.x()
                y = y + line_height + space_y
                next_x = x + item.sizeHint().width() + space_x
                line_height = 0

            if not test_only:
                item.setGeometry(QRect(QPoint(x, y), item.sizeHint()))

            x = next_x
            line_height = max(line_height, item.sizeHint().height())

        total_height = y + line_height - rect.y() + top + bottom
        return total_height
