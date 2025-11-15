from __future__ import annotations

import ctypes
from ctypes import wintypes
import csv
import sys
import time
from pathlib import Path
from typing import Iterable

from PySide6.QtCore import Qt, Signal, QEvent, QPoint, QSize
from PySide6.QtGui import QAction, QCloseEvent, QGuiApplication, QColor, QIcon, QPainter, QPixmap, QPen
from PySide6.QtWidgets import (
    QApplication,
    QFileDialog,
    QFrame,
    QHBoxLayout,
    QLabel,
    QListWidget,
    QListWidgetItem,
    QMainWindow,
    QMenuBar,
    QLineEdit,
    QMessageBox,
    QPushButton,
    QProgressBar,
    QScrollArea,
    QSizePolicy,
    QStatusBar,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
    QWidget,
    QSplitter,
    QComboBox,
    QHeaderView,
    QAbstractItemView,
    QAbstractScrollArea,
    QGraphicsDropShadowEffect,
    QStyle,
    QBoxLayout,
    QSpacerItem,
)

WM_NCHITTEST = 0x0084
HTLEFT = 10
HTRIGHT = 11
HTTOP = 12
HTTOPLEFT = 13
HTTOPRIGHT = 14
HTBOTTOM = 15
HTBOTTOMLEFT = 16
HTBOTTOMRIGHT = 17


from .hash_utils import CHUNK_SIZES, SUPPORTED_ALGORITHMS
from .models import HashResult, format_size
from .styles import load_stylesheet
from .workers import HashWorker

APP_NAME = "Hash Forge"
APP_USER_MODEL_ID = "com.hashforge.app"
APP_CREDITS = "Crediti • William Tritapepe"


def _resource_path(relative: str) -> Path:
    """Return absolute path for bundled resources (handles PyInstaller)."""
    base = getattr(sys, "_MEIPASS", None)
    if base:
        return Path(base) / relative
    return Path(__file__).resolve().parent.parent / relative


ICON_PATH = _resource_path("icon.png")


def _set_app_user_model_id(app_id: str) -> None:
    """Ensure Windows taskbar/thumbnail uses our packaged icon."""
    if not sys.platform.startswith("win"):
        return
    try:
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(app_id)
    except OSError:
        pass
TABLE_HEADERS = ("Nome", "Percorso", "Algoritmo", "Hash", "Dimensione", "Modificato")


class TargetListWidget(QListWidget):
    filesDropped = Signal(list)

    def __init__(self) -> None:
        super().__init__()
        self.setAcceptDrops(True)

    def dragEnterEvent(self, event):  # pragma: no cover - UI interaction
        if event.mimeData().hasUrls():
            event.acceptProposedAction()
        else:
            super().dragEnterEvent(event)

    def dragMoveEvent(self, event):  # pragma: no cover - UI interaction
        if event.mimeData().hasUrls():
            event.acceptProposedAction()
        else:
            super().dragMoveEvent(event)

    def dropEvent(self, event):  # pragma: no cover - UI interaction
        if not event.mimeData().hasUrls():
            super().dropEvent(event)
            return
        paths = [url.toLocalFile() for url in event.mimeData().urls() if url.isLocalFile()]
        if paths:
            self.filesDropped.emit(paths)
        event.acceptProposedAction()


class TitleBar(QWidget):  # pragma: no cover - UI behaviour
    def __init__(self, window: QMainWindow, menu_bar: QMenuBar | None, icon: QIcon | None = None) -> None:
        super().__init__()
        self._window = window
        self._menu_bar = menu_bar
        self._drag_pos: QPoint | None = None
        self._drag_offset: QPoint | None = None
        self._icon_cache: dict[tuple[str, str], QIcon] = {}
        self.setObjectName("TitleBar")

        layout = QHBoxLayout(self)
        layout.setContentsMargins(24, 6, 12, 6)
        layout.setSpacing(10)

        self._icon_label = QLabel()
        self._icon_label.setObjectName("TitleBarIcon")
        self._icon_label.setFixedSize(22, 22)
        self._icon_label.setScaledContents(True)
        layout.addWidget(self._icon_label, alignment=Qt.AlignVCenter)

        self._title_label = QLabel(window.windowTitle())
        font = self._title_label.font()
        font.setPointSizeF(font.pointSizeF() + 1)
        self._title_label.setFont(font)
        self._title_label.setObjectName("TitleBarText")
        layout.addWidget(self._title_label, alignment=Qt.AlignVCenter)
        self.set_icon(icon)

        if menu_bar is not None:
            menu_bar.setNativeMenuBar(False)
            menu_bar.setObjectName("TopMenuBar")
            layout.addWidget(menu_bar, alignment=Qt.AlignVCenter)

        layout.addStretch()

        self._min_btn = self._make_button("min")
        self._min_btn.clicked.connect(window.showMinimized)
        layout.addWidget(self._min_btn)

        self._max_btn = self._make_button("max")
        self._max_btn.clicked.connect(self._toggle_max_restore)
        layout.addWidget(self._max_btn)

        self._close_btn = self._make_button("close")
        self._close_btn.clicked.connect(window.close)
        layout.addWidget(self._close_btn)

    def _interactive_widgets(self) -> tuple[QWidget, ...]:
        widgets: list[QWidget] = [self._min_btn, self._max_btn, self._close_btn]
        if self._menu_bar is not None:
            widgets.append(self._menu_bar)
        return tuple(widgets)

    def _belongs_to_interactive(self, widget: QWidget | None) -> bool:
        interactive = self._interactive_widgets()
        while widget is not None:
            if widget in interactive:
                return True
            widget = widget.parentWidget()
        return False

    def _make_button(self, role: str) -> QPushButton:
        btn = QPushButton()
        btn.setObjectName("WindowButton")
        btn.setProperty("buttonRole", role)
        btn.setCursor(Qt.PointingHandCursor)
        btn.setFocusPolicy(Qt.NoFocus)
        btn.setFlat(True)
        btn.setMinimumSize(40, 30)
        btn.setMaximumWidth(42)
        btn.setToolTip({"min": "Riduci a icona", "max": "Massimizza", "close": "Chiudi"}.get(role, role))
        self._apply_icon(btn, role)
        return btn

    def set_title(self, title: str) -> None:
        self._title_label.setText(title)

    def _toggle_max_restore(self) -> None:
        if self._window.isMaximized():
            self._window.showNormal()
        else:
            self._window.showMaximized()
        self.update_max_button(self._window.isMaximized())

    def set_icon(self, icon: QIcon | None) -> None:
        if icon is None or icon.isNull():
            self._icon_label.clear()
            return
        self._icon_label.setPixmap(icon.pixmap(20, 20))

    def update_max_button(self, maximized: bool) -> None:
        state = "restore" if maximized else "normal"
        self._apply_icon(self._max_btn, "max", state)
        self._max_btn.setProperty("windowState", "max" if maximized else "normal")
        self._max_btn.style().unpolish(self._max_btn)
        self._max_btn.style().polish(self._max_btn)

    def _apply_icon(self, button: QPushButton, role: str, state: str = "") -> None:
        key = (role, state)
        icon = self._icon_cache.get(key)
        if icon is None:
            icon = self._build_icon(role, state)
            self._icon_cache[key] = icon
        button.setIcon(icon)
        button.setIconSize(QSize(18, 18))

    def _build_icon(self, role: str, state: str) -> QIcon:
        size = 24
        pixmap = QPixmap(size, size)
        pixmap.fill(Qt.transparent)
        painter = QPainter(pixmap)
        painter.setRenderHint(QPainter.Antialiasing)
        pen_color = QColor("#E6E9F2") if role != "close" else QColor("#FF6B6B")
        pen = QPen(pen_color)
        pen.setWidthF(1.8)
        pen.setCapStyle(Qt.RoundCap)
        pen.setJoinStyle(Qt.RoundJoin)
        painter.setPen(pen)
        painter.setBrush(Qt.NoBrush)

        if role == "min":
            y = size / 2
            painter.drawLine(6, y, size - 6, y)
        elif role == "max" and state == "restore":
            painter.drawRoundedRect(7, 9, size - 14, size - 14, 3, 3)
            painter.drawRoundedRect(9, 7, size - 14, size - 14, 3, 3)
        elif role == "max":
            painter.drawRoundedRect(6, 6, size - 12, size - 12, 3, 3)
        elif role == "close":
            painter.drawLine(7, 7, size - 7, size - 7)
            painter.drawLine(7, size - 7, size - 7, 7)
        else:
            painter.drawEllipse(6, 6, size - 12, size - 12)

        painter.end()
        return QIcon(pixmap)

    def mousePressEvent(self, event):
        if event.button() == Qt.LeftButton:
            child = self.childAt(event.position().toPoint())
            if self._belongs_to_interactive(child):
                return super().mousePressEvent(event)
            self._drag_pos = event.globalPosition().toPoint()
            self._drag_offset = self._drag_pos - self._window.frameGeometry().topLeft()
            event.accept()
            return
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event):
        if (
            event.buttons() & Qt.LeftButton
            and self._drag_pos is not None
            and self._drag_offset is not None
            and not self._window.isMaximized()
        ):
            global_pos = event.globalPosition().toPoint()
            self._window.move(global_pos - self._drag_offset)
            event.accept()
            return
        super().mouseMoveEvent(event)

    def mouseReleaseEvent(self, event):
        if event.button() == Qt.LeftButton:
            self._drag_pos = None
            self._drag_offset = None
        super().mouseReleaseEvent(event)

    def mouseDoubleClickEvent(self, event):
        if event.button() == Qt.LeftButton:
            child = self.childAt(event.position().toPoint())
            if not self._belongs_to_interactive(child):
                self._toggle_max_restore()
                event.accept()
                return
        super().mouseDoubleClickEvent(event)


class ResizeCorner(QWidget):  # pragma: no cover - UI behaviour
    def __init__(self, window: QMainWindow) -> None:
        super().__init__(window)
        self._window = window
        self.setFixedSize(18, 18)
        self.setCursor(Qt.SizeFDiagCursor)
        self.setObjectName("ResizeCorner")
        self.setToolTip("Ridimensiona")
        self._press_pos: QPoint | None = None
        self._press_size: QSize | None = None

    def mousePressEvent(self, event):
        if event.button() == Qt.LeftButton:
            if self._window.isMaximized():
                return
            self._press_pos = event.globalPosition().toPoint()
            self._press_size = self._window.size()
            self.grabMouse(Qt.SizeFDiagCursor)
            event.accept()
            return
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event):
        if (
            event.buttons() & Qt.LeftButton
            and self._press_pos is not None
            and self._press_size is not None
            and not self._window.isMaximized()
        ):
            delta = event.globalPosition().toPoint() - self._press_pos
            new_width = max(self._window.minimumWidth(), self._press_size.width() + delta.x())
            new_height = max(self._window.minimumHeight(), self._press_size.height() + delta.y())
            self._window.resize(new_width, new_height)
            event.accept()
            return
        super().mouseMoveEvent(event)

    def mouseReleaseEvent(self, event):
        if event.button() == Qt.LeftButton:
            self._press_pos = None
            self._press_size = None
            self.releaseMouse()
        super().mouseReleaseEvent(event)

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing)
        painter.fillRect(self.rect(), Qt.transparent)
        pen = QPen(QColor("#9AA5B8"))
        pen.setWidth(1)
        painter.setPen(pen)
        size = min(self.width(), self.height()) - 3
        for offset in (0, 4, 8):
            painter.drawLine(self.width() - offset - size, self.height() - offset - 2, self.width() - 2, self.height() - offset - size)
        painter.end()

class MainWindow(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle(APP_NAME)
        self.setMinimumSize(1120, 700)
        self.setWindowFlags(
            Qt.Window
            | Qt.FramelessWindowHint
            | Qt.WindowSystemMenuHint
            | Qt.WindowMinMaxButtonsHint
        )
        self.setAttribute(Qt.WA_TranslucentBackground, False)
        self._worker: HashWorker | None = None
        self._results: list[HashResult] = []
        self._hash_start_time: float | None = None
        self._density_mode: str | None = None
        self._scale_factor: float = 1.0
        self._height_mode: str | None = None
        self._button_base_height = 46
        self._ignore_progress = False
        self._resize_border = 8
        self._menu_widget = self._build_menubar()
        self._title_bar: TitleBar | None = None

        self._build_ui()
        self._connect_signals()

    def _build_controls(self) -> QFrame:
        frame = QFrame()
        frame.setObjectName("Card")
        controls = QVBoxLayout(frame)
        controls.setSpacing(18)
        controls.setContentsMargins(24, 24, 24, 24)
        self._controls_layout = controls

        top_row = QBoxLayout(QBoxLayout.LeftToRight)
        top_row.setSpacing(12)
        self._controls_top_row = top_row

        algo_label = QLabel("ALGORITMO")
        algo_label.setObjectName("FormLabel")
        top_row.addWidget(algo_label)
        self.algorithm_box = self._styled_combo(SUPPORTED_ALGORITHMS)
        self.algorithm_box.setCurrentText("SHA256")
        self.algorithm_box.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Fixed)
        top_row.addWidget(self.algorithm_box)

        chunk_label = QLabel("PRESTAZIONI")
        chunk_label.setObjectName("FormLabel")
        top_row.addWidget(chunk_label)
        chunk_choices = list(CHUNK_SIZES.items())
        self.chunk_box = self._styled_combo([label for label, _ in chunk_choices])
        self.chunk_box.setCurrentIndex(0)
        self.chunk_box.setMinimumWidth(160)
        top_row.addWidget(self.chunk_box)
        controls.addLayout(top_row)

        button_row = QHBoxLayout()
        button_row.setSpacing(10)
        button_row.setContentsMargins(0, 0, 0, 0)
        self._button_row = button_row
        self.file_btn = QPushButton("Aggiungi file")
        self.folder_btn = QPushButton("Aggiungi cartella")
        self.clear_btn = QPushButton("Svuota lista")
        self._quick_add_buttons = (self.file_btn, self.folder_btn, self.clear_btn)
        for btn in self._quick_add_buttons:
            btn.setMinimumWidth(140)
            btn.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Fixed)
        self._set_button_variant(self.file_btn, "primary")
        self._set_button_variant(self.folder_btn, "primary")
        self._set_button_variant(self.clear_btn, "danger")
        button_row.addWidget(self.file_btn, 1)
        button_row.addWidget(self.folder_btn, 1)
        button_row.addWidget(self.clear_btn, 1)
        controls.addLayout(button_row)

        self.targets_list = TargetListWidget()
        self.targets_list.setSelectionMode(QListWidget.ExtendedSelection)
        self.targets_list.setMinimumHeight(150)
        self.targets_list.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Expanding)
        controls.addWidget(self.targets_list)

        actions_row = QBoxLayout(QBoxLayout.LeftToRight)
        actions_row.setSpacing(12)
        self._actions_layout = actions_row
        self.remove_btn = QPushButton("Rimuovi selezionati")
        self.remove_btn.setMinimumWidth(170)
        self._set_button_variant(self.remove_btn, "ghost")
        actions_row.addWidget(self.remove_btn)
        self._actions_spacer = QSpacerItem(0, 0, QSizePolicy.Expanding, QSizePolicy.Minimum)
        actions_row.addItem(self._actions_spacer)

        self.compute_btn = QPushButton("Calcola hash")
        self.compute_btn.setSizePolicy(QSizePolicy.Fixed, QSizePolicy.Fixed)
        self.stop_btn = QPushButton("Ferma")
        self.stop_btn.setEnabled(False)
        self._set_button_variant(self.compute_btn, "accent")
        self._set_button_variant(self.stop_btn, "danger")

        run_controls = QHBoxLayout()
        run_controls.setSpacing(10)
        run_controls.setContentsMargins(0, 0, 0, 0)
        run_controls.addWidget(self.compute_btn)
        run_controls.addWidget(self.stop_btn)
        actions_row.addLayout(run_controls)
        controls.addLayout(actions_row)

        self.progress = QProgressBar()
        self.progress.setRange(0, 100)
        self.progress.setTextVisible(True)
        self.progress.setAlignment(Qt.AlignLeft | Qt.AlignVCenter)
        self._set_progress_display(0, 0)
        controls.addWidget(self.progress)

        return frame

    def _build_hero_card(self) -> QFrame:
        frame = QFrame()
        frame.setObjectName("HeroCard")
        layout = QVBoxLayout(frame)
        layout.setSpacing(10)
        layout.setContentsMargins(20, 22, 20, 22)
        self._hero_layout = layout

        badge = QLabel("Hashing professionale")
        badge.setObjectName("Badge")
        layout.addWidget(badge, alignment=Qt.AlignLeft)

        self.hero_title = QLabel("Hash Forge")
        self.hero_title.setObjectName("HeroTitle")
        layout.addWidget(self.hero_title)

        self.hero_subtitle = QLabel(
            "Calcola gli hash di file e cartelle, mostra l'avanzamento in tempo reale e ti aiuta a verificare l'integrità dei dati."
        )
        self.hero_subtitle.setObjectName("HeroSubtitle")
        self.hero_subtitle.setWordWrap(True)
        self.hero_subtitle.setMaximumWidth(760)
        layout.addWidget(self.hero_subtitle)

        return frame

    def resizeEvent(self, event):  # pragma: no cover - depends on GUI resizing
        super().resizeEvent(event)
        self._update_responsive_layout()

    def _update_responsive_layout(self) -> None:
        if not hasattr(self, "splitter"):
            return
        width = max(1, self.width())
        base_width = 1920
        scale = max(0.75, min(0.92, width / base_width))
        self._apply_scale(scale)
        height = self._available_height()

        if width < 1000:
            density = "ultra"
        elif width < 1500:
            density = "compact"
        else:
            density = "default"

        self._apply_density_mode(density)
        if height < 740:
            height_mode = "compressed"
        elif height < 860:
            height_mode = "tight"
        else:
            height_mode = "spacious"
        self._apply_height_mode(height_mode)

        orientation = Qt.Horizontal if width >= 1200 else Qt.Vertical
        if self.splitter.orientation() != orientation:
            self.splitter.setOrientation(orientation)
        if hasattr(self, "hero_title"):
            variant = {
                "default": "default",
                "compact": "compact",
                "ultra": "mini",
            }[density]
            self.hero_title.setProperty("sizeVariant", variant)
            self._repolish(self.hero_title)
        if hasattr(self, "hero_subtitle"):
            limit = 960 if orientation == Qt.Horizontal else 1400
            self.hero_subtitle.setMaximumWidth(limit)
            self.hero_subtitle.setProperty("sizeVariant", "compact" if density != "default" else "default")
            self._repolish(self.hero_subtitle)
        self._refresh_splitter_handles()
        self._update_scrollbar_policy()

    def _apply_density_mode(self, mode: str) -> None:
        if self._density_mode == mode:
            return

        profiles = {
            "default": {
                "content_margin": 28,
                "section_spacing": 20,
                "card_padding": 22,
                "stack_gap": 16,
                "hero_padding": 28,
                "hero_spacing": 12,
                "row_spacing": 12,
                "button_min": 140,
                "chunk_min": 160,
                "targets_min": 150,
                "top_direction": QBoxLayout.LeftToRight,
                "actions_direction": QBoxLayout.LeftToRight,
                "results_direction": QBoxLayout.LeftToRight,
                "metrics_spacing": 16,
                "show_metrics": True,
            },
            "compact": {
                "content_margin": 20,
                "section_spacing": 16,
                "card_padding": 18,
                "stack_gap": 12,
                "hero_padding": 22,
                "hero_spacing": 10,
                "row_spacing": 10,
                "button_min": 120,
                "chunk_min": 140,
                "targets_min": 140,
                "top_direction": QBoxLayout.LeftToRight,
                "actions_direction": QBoxLayout.LeftToRight,
                "results_direction": QBoxLayout.LeftToRight,
                "metrics_spacing": 12,
                "show_metrics": True,
            },
            "ultra": {
                "content_margin": 16,
                "section_spacing": 12,
                "card_padding": 14,
                "stack_gap": 10,
                "hero_padding": 18,
                "hero_spacing": 8,
                "row_spacing": 8,
                "button_min": 104,
                "chunk_min": 120,
                "targets_min": 120,
                "top_direction": QBoxLayout.LeftToRight,
                "actions_direction": QBoxLayout.LeftToRight,
                "results_direction": QBoxLayout.LeftToRight,
                "metrics_spacing": 10,
                "show_metrics": True,
            },
        }

        config = profiles[mode]
        self._density_mode = mode
        scale = getattr(self, "_scale_factor", 1.0)

        def sized(value: int, minimum: int = 0) -> int:
            return max(minimum, int(round(value * scale)))

        if hasattr(self, "_content_layout"):
            margin = sized(config["content_margin"])
            self._content_layout.setContentsMargins(margin, margin, margin, margin)
            self._content_layout.setSpacing(sized(config["section_spacing"], 4))
        if hasattr(self, "_controls_layout"):
            pad = sized(config["card_padding"])
            self._controls_layout.setContentsMargins(pad, pad, pad, pad)
            self._controls_layout.setSpacing(sized(config["stack_gap"], 4))
        if hasattr(self, "_results_layout"):
            pad = sized(config["card_padding"])
            self._results_layout.setContentsMargins(pad, pad, pad, pad)
            self._results_layout.setSpacing(sized(config["stack_gap"], 4))
        if hasattr(self, "_hero_layout"):
            pad = sized(config["hero_padding"])
            self._hero_layout.setContentsMargins(pad, pad, pad, pad)
            self._hero_layout.setSpacing(sized(config["hero_spacing"], 4))
        if hasattr(self, "_controls_top_row"):
            self._controls_top_row.setDirection(config["top_direction"])
            self._controls_top_row.setSpacing(sized(config["row_spacing"], 4))
        if hasattr(self, "_actions_layout"):
            self._actions_layout.setDirection(config["actions_direction"])
            self._actions_layout.setSpacing(sized(config["row_spacing"], 4))
        if hasattr(self, "_results_title_layout"):
            self._results_title_layout.setDirection(config["results_direction"])
            self._results_title_layout.setSpacing(sized(config["row_spacing"], 4))

        if hasattr(self, "_button_row"):
            self._button_row.setSpacing(sized(config["row_spacing"], 4))
        if hasattr(self, "_hero_metrics_container"):
            self._hero_metrics_container.setVisible(False)

        if hasattr(self, "chunk_box"):
            self.chunk_box.setMinimumWidth(sized(config["chunk_min"], 100))
        if hasattr(self, "targets_list"):
            self.targets_list.setMinimumHeight(sized(config["targets_min"], 100))

        for button in getattr(self, "_responsive_buttons", []):
            button.setMinimumWidth(sized(config["button_min"], 90))

        if hasattr(self, "_actions_spacer"):
            if config["actions_direction"] in (QBoxLayout.TopToBottom, QBoxLayout.BottomToTop):
                self._actions_spacer.changeSize(0, 0, QSizePolicy.Minimum, QSizePolicy.Minimum)
            else:
                self._actions_spacer.changeSize(0, 0, QSizePolicy.Expanding, QSizePolicy.Minimum)
        if hasattr(self, "_results_title_spacer"):
            if config["results_direction"] in (QBoxLayout.TopToBottom, QBoxLayout.BottomToTop):
                self._results_title_spacer.changeSize(0, 0, QSizePolicy.Minimum, QSizePolicy.Minimum)
            else:
                self._results_title_spacer.changeSize(0, 0, QSizePolicy.Expanding, QSizePolicy.Minimum)

        if hasattr(self, "_controls_layout"):
            self._controls_layout.invalidate()
        if hasattr(self, "_results_layout"):
            self._results_layout.invalidate()

    def _apply_scale(self, scale: float) -> None:
        scale = max(0.65, min(scale, 1.0))
        if abs(scale - getattr(self, "_scale_factor", 1.0)) < 0.01:
            return
        self._scale_factor = scale
        app = QApplication.instance()
        if app is not None:
            app.setStyleSheet(load_stylesheet(scale))
        min_height = max(32, int(round(self._button_base_height * scale)))
        for button in getattr(self, "_responsive_buttons", []):
            button.setMinimumHeight(min_height)

    def _build_results_card(self) -> QFrame:
        frame = QFrame()
        frame.setObjectName("Card")
        layout = QVBoxLayout(frame)
        layout.setSpacing(16)
        layout.setContentsMargins(24, 24, 24, 24)
        self._results_layout = layout

        title_row = QBoxLayout(QBoxLayout.LeftToRight)
        label = QLabel("RISULTATI")
        label.setObjectName("SectionTitle")
        title_row.addWidget(label)
        spacer = QSpacerItem(0, 0, QSizePolicy.Expanding, QSizePolicy.Minimum)
        title_row.addItem(spacer)
        self._results_title_spacer = spacer
        self._results_title_layout = title_row

        self.copy_btn = QPushButton("Copia hash")
        self.export_btn = QPushButton("Esporta CSV")
        self.export_txt_btn = QPushButton("Esporta TXT")
        self.clear_results_btn = QPushButton("Pulisci risultati")
        self._set_button_variant(self.copy_btn, "primary")
        self._set_button_variant(self.export_btn, "primary")
        self._set_button_variant(self.export_txt_btn, "primary")
        self._set_button_variant(self.clear_results_btn, "danger")
        self._result_action_buttons = (
            self.copy_btn,
            self.export_btn,
            self.export_txt_btn,
            self.clear_results_btn,
        )
        title_row.addWidget(self.copy_btn)
        title_row.addWidget(self.export_btn)
        title_row.addWidget(self.export_txt_btn)
        title_row.addWidget(self.clear_results_btn)
        layout.addLayout(title_row)

        search_row = QHBoxLayout()
        search_row.setSpacing(10)
        search_container = QFrame()
        search_container.setObjectName("SearchContainer")
        search_container_layout = QHBoxLayout(search_container)
        search_container_layout.setContentsMargins(18, 10, 18, 10)
        search_container_layout.setSpacing(10)

        search_icon = QLabel()
        search_icon.setObjectName("SearchIcon")
        icon = self.style().standardIcon(QStyle.SP_FileDialogContentsView)
        search_icon.setPixmap(icon.pixmap(18, 18))
        search_container_layout.addWidget(search_icon)

        self.search_input = QLineEdit()
        self.search_input.setObjectName("SearchField")
        self.search_input.setPlaceholderText("Cerca per nome, percorso o hash…")
        self.search_input.setClearButtonEnabled(True)
        self.search_input.setFrame(False)
        self.search_input.setMinimumWidth(260)
        self.search_input.textChanged.connect(self._apply_result_filter)
        search_container_layout.addWidget(self.search_input)

        search_row.addWidget(search_container)
        layout.addLayout(search_row)

        self.table = QTableWidget()
        self.table.setColumnCount(len(TABLE_HEADERS))
        self.table.setHorizontalHeaderLabels(TABLE_HEADERS)
        self.table.setSizeAdjustPolicy(QAbstractScrollArea.AdjustToContentsOnFirstShow)
        self.table.setSelectionBehavior(QTableWidget.SelectRows)
        self.table.setEditTriggers(QTableWidget.NoEditTriggers)
        self.table.setAlternatingRowColors(True)
        self.table.setWordWrap(False)
        self.table.setHorizontalScrollMode(QAbstractItemView.ScrollPerPixel)
        header = self.table.horizontalHeader()
        header.setMinimumSectionSize(120)
        header.setStretchLastSection(False)
        header.setSectionResizeMode(QHeaderView.Stretch)
        for column in (0, 2, 4, 5):
            header.setSectionResizeMode(column, QHeaderView.ResizeToContents)
        self.table.verticalHeader().setVisible(False)
        layout.addWidget(self.table)

        return frame

    def _build_menubar(self) -> QMenuBar:
        menu = QMenuBar()
        file_menu = menu.addMenu("File")
        exit_action = QAction("Esci", self)
        exit_action.triggered.connect(self.close)
        file_menu.addAction(exit_action)

        help_menu = menu.addMenu("Aiuto")
        about_action = QAction("Informazioni", self)
        about_action.triggered.connect(self._show_about)
        help_menu.addAction(about_action)
        return menu

    def _styled_combo(self, entries: Iterable[str]):
        combo = QComboBox()
        for entry in entries:
            combo.addItem(entry, entry)
        return combo

    def _connect_signals(self) -> None:
        self.file_btn.clicked.connect(self._choose_file)
        self.folder_btn.clicked.connect(self._choose_folder)
        self.clear_btn.clicked.connect(self.targets_list.clear)
        self.remove_btn.clicked.connect(self._remove_selected)
        self.compute_btn.clicked.connect(self._start_hashing)
        self.stop_btn.clicked.connect(self._stop_hashing)
        self.copy_btn.clicked.connect(self._copy_selected_hash)
        self.export_btn.clicked.connect(self._export_csv)
        self.export_txt_btn.clicked.connect(self._export_txt)
        self.clear_results_btn.clicked.connect(self._clear_results)
        self.targets_list.filesDropped.connect(self._append_targets)

    # region target management
    def _choose_file(self) -> None:
        files, _ = QFileDialog.getOpenFileNames(self, "Seleziona i file")
        self._append_targets(files)

    def _choose_folder(self) -> None:
        directory = QFileDialog.getExistingDirectory(self, "Seleziona cartella")
        if directory:
            self._append_targets([directory])

    def _append_targets(self, paths: Iterable[str]) -> None:
        existing = {self.targets_list.item(i).text() for i in range(self.targets_list.count())}
        for path in paths:
            if path in existing:
                continue
            item = QListWidgetItem(path)
            item.setToolTip(path)
            self.targets_list.addItem(item)

    def _remove_selected(self) -> None:
        for item in self.targets_list.selectedItems():
            row = self.targets_list.row(item)
            self.targets_list.takeItem(row)

    # endregion

    # region hashing lifecycle
    def _start_hashing(self) -> None:
        if self.targets_list.count() == 0:
            QMessageBox.information(self, APP_NAME, "Aggiungi almeno un file o cartella")
            return
        if self._worker and self._worker.isRunning():
            QMessageBox.warning(self, APP_NAME, "Calcolo già in corso")
            return

        algorithm = self.algorithm_box.currentText()
        chunk_label = self.chunk_box.currentText()
        chunk_size = CHUNK_SIZES.get(chunk_label, next(iter(CHUNK_SIZES.values())))
        targets = [self.targets_list.item(i).text() for i in range(self.targets_list.count())]

        self._results.clear()
        self._apply_result_filter()
        self._set_progress_display(0, 0)
        self.statusBar().showMessage("Hashing in corso...")
        self.compute_btn.setEnabled(False)
        self.stop_btn.setEnabled(True)
        self._ignore_progress = False
        self._hash_start_time = time.monotonic()

        self._worker = HashWorker(targets, algorithm, chunk_size)
        self._worker.fileHashed.connect(self._handle_result)
        self._worker.progress.connect(self._update_progress)
        self._worker.completed.connect(self._handle_completed)
        self._worker.failed.connect(self._handle_failed)
        self._worker.start()

    def _stop_hashing(self) -> None:
        if self._worker and self._worker.isRunning():
            self._worker.request_stop()
            self.statusBar().showMessage("Interruzione in corso...")
            self.stop_btn.setEnabled(False)
        self._ignore_progress = True
        self.progress.setFormat("  Operazione interrotta")
        self._hash_start_time = None

    def _build_ui(self) -> None:
        screen = QGuiApplication.primaryScreen()
        if screen:
            area = screen.availableGeometry()
            self.resize(int(area.width() * 0.82), int(area.height() * 0.8))
        self.setMinimumSize(1230, 700)

        content = QWidget()
        content_layout = QVBoxLayout(content)
        content_layout.setSpacing(24)
        content_layout.setContentsMargins(32, 32, 32, 32)
        self._content_layout = content_layout

        self.hero_card = self._build_hero_card()
        self.hero_card.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Maximum)
        self.hero_card.setMaximumHeight(360)
        content_layout.addWidget(self.hero_card)
        self._apply_shadow(self.hero_card, blur=50, alpha=140, y_offset=16)

        self.splitter = QSplitter(Qt.Vertical)
        self.splitter.setObjectName("MainSplitter")
        self.splitter.setChildrenCollapsible(False)
        self.splitter.setHandleWidth(6)
        controls_card = self._build_controls()
        results_card = self._build_results_card()
        controls_card.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Preferred)
        results_card.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Expanding)
        self.splitter.addWidget(controls_card)
        self.splitter.addWidget(results_card)
        self.splitter.setStretchFactor(0, 1)
        self.splitter.setStretchFactor(1, 2)
        content_layout.addWidget(self.splitter, stretch=1)
        self._apply_shadow(controls_card, blur=40, alpha=110, y_offset=10)
        self._apply_shadow(results_card, blur=40, alpha=110, y_offset=10)

        self._responsive_buttons = (
            *self._quick_add_buttons,
            self.remove_btn,
            self.compute_btn,
            self.stop_btn,
            *self._result_action_buttons,
        )

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QFrame.NoFrame)
        scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        scroll.setWidget(content)

        if self._title_bar is None:
            self._title_bar = TitleBar(self, self._menu_widget, self.windowIcon())

        chrome_container = QWidget()
        chrome_layout = QVBoxLayout(chrome_container)
        chrome_layout.setContentsMargins(0, 0, 0, 0)
        chrome_layout.setSpacing(0)
        chrome_layout.addWidget(self._title_bar)
        chrome_layout.addWidget(scroll)

        self.setCentralWidget(chrome_container)
        self._content_widget = content
        self._controls_card = controls_card
        self._results_card = results_card
        self._scroll_area = scroll
        self.splitter.splitterMoved.connect(lambda *_: self._refresh_splitter_handles())
        self._refresh_splitter_handles()

        status = QStatusBar()
        status.setObjectName("MainStatusBar")
        status.setSizeGripEnabled(False)
        status.setContentsMargins(20, 0, 20, 0)
        status.setMinimumHeight(32)
        status.showMessage("Pronto")
        self._resize_corner = ResizeCorner(self)
        status.addPermanentWidget(self._resize_corner, 0)
        self.setStatusBar(status)

        self._update_responsive_layout()
        if self._title_bar:
            self._title_bar.update_max_button(self.isMaximized())

    def _handle_result(self, result: HashResult) -> None:
        self._results.append(result)
        self._update_responsive_layout()
        self._apply_result_filter()

    def _apply_result_filter(self) -> None:
        if not hasattr(self, "table"):
            return
        query = ""
        if hasattr(self, "search_input") and self.search_input:
            query = self.search_input.text().strip().lower()
        if not self._results:
            self.table.setRowCount(0)
            return
        if not query:
            filtered = list(self._results)
        else:
            filtered = [
                result
                for result in self._results
                if any(query in field.lower() for field in result.as_row())
            ]
        self._populate_results_table(filtered)

    def _populate_results_table(self, results: Iterable[HashResult]) -> None:
        self.table.setRowCount(0)
        for result in results:
            row = self.table.rowCount()
            self.table.insertRow(row)
            self._populate_table_row(row, result)

    def _populate_table_row(self, row: int, result: HashResult) -> None:
        for column, value in enumerate(result.as_row()):
            item = QTableWidgetItem(value)
            item.setFlags(item.flags() ^ Qt.ItemIsEditable)
            if column == 3:
                font = item.font()
                font.setFamily("Consolas")
                item.setFont(font)
            self.table.setItem(row, column, item)

    def _update_progress(self, processed: int, total: int) -> None:
        if self._ignore_progress:
            return
        self._set_progress_display(processed, total)

    def _handle_completed(self) -> None:
        worker = self._worker
        self._worker = None
        self.compute_btn.setEnabled(True)
        self.stop_btn.setEnabled(False)
        self._ignore_progress = False
        if worker and worker.cancelled:
            self.statusBar().showMessage("Calcolo interrotto", 5000)
        else:
            if self.progress.value() < 100:
                self.progress.setValue(100)
            self.statusBar().showMessage("Calcolo completato", 5000)
        self._hash_start_time = None

    def _handle_failed(self, message: str) -> None:
        self._worker = None
        self.compute_btn.setEnabled(True)
        self.stop_btn.setEnabled(False)
        self._ignore_progress = False
        self.statusBar().showMessage("Errore")
        QMessageBox.critical(self, APP_NAME, message)
        self._hash_start_time = None

    # endregion

    # region utilities
    def _copy_selected_hash(self) -> None:
        selected_rows = {index.row() for index in self.table.selectedIndexes()}
        if not selected_rows:
            QMessageBox.information(self, APP_NAME, "Seleziona almeno una riga")
            return
        hashes = []
        for row in selected_rows:
            item = self.table.item(row, 3)
            if item:
                hashes.append(item.text())
        QApplication.clipboard().setText("\n".join(hashes))
        self.statusBar().showMessage("Hash copiati negli appunti", 4000)

    def _export_csv(self) -> None:
        if not self._results:
            QMessageBox.information(self, APP_NAME, "Nessun risultato da esportare")
            return
        path, _ = QFileDialog.getSaveFileName(
            self,
            "Salva risultati",
            "hashes.csv",
            "CSV (*.csv)",
        )
        if not path:
            return
        with open(path, "w", newline="", encoding="utf-8") as handle:
            writer = csv.writer(handle)
            writer.writerow(TABLE_HEADERS)
            for result in self._results:
                writer.writerow(result.as_row())
        self.statusBar().showMessage(f"Salvati {len(self._results)} hash", 5000)

    def _export_txt(self) -> None:
        if not self._results:
            QMessageBox.information(self, APP_NAME, "Nessun risultato da esportare")
            return
        path, _ = QFileDialog.getSaveFileName(
            self,
            "Salva risultati",
            "hashes.txt",
            "Testo (*.txt);;Tutti i file (*.*)",
        )
        if not path:
            return
        entries: list[str] = []
        for result in self._results:
            name, file_path, algorithm, digest, size_text, modified = result.as_row()
            entries.append(
                "\n".join(
                    (
                        f"Nome: {name}",
                        f"Percorso: {file_path}",
                        f"Algoritmo: {algorithm}",
                        f"Hash: {digest}",
                        f"Dimensione: {size_text}",
                        f"Modificato: {modified}",
                    )
                )
            )
        separator = "\n" + ("-" * 64) + "\n\n"
        with open(path, "w", encoding="utf-8") as handle:
            handle.write("Hash Forge - Risultati esportati\n")
            handle.write(f"Totale elementi: {len(self._results)}\n")
            handle.write("=" * 64 + "\n\n")
            handle.write(separator.join(entries))
            handle.write("\n")
        self.statusBar().showMessage(f"Esportati {len(self._results)} hash in TXT", 5000)

    def _clear_results(self) -> None:
        self._results.clear()
        self._apply_result_filter()
        self.statusBar().showMessage("Risultati puliti", 3000)

    def _show_about(self) -> None:
        QMessageBox.information(
            self,
            "Informazioni",
            "Hash Forge\nCalcola gli hash di file e cartelle, mostra l'avanzamento in tempo reale e permette di salvare o copiare i risultati per verificarne l'integrità.\n\n"
            f"{APP_CREDITS}",
        )

    def closeEvent(self, event: QCloseEvent) -> None:
        if self._worker and self._worker.isRunning():
            self._worker.request_stop()
            self._worker.wait(1000)
        event.accept()

    def changeEvent(self, event):
        super().changeEvent(event)
        if event.type() == QEvent.WindowStateChange and self._title_bar:
            self._title_bar.update_max_button(self.isMaximized())

    # endregion

    def _human_bytes(self, value: int) -> str:
        if value <= 0:
            return "0 B"
        if value < 1024:
            return f"{value} B"
        return format_size(value)

    def _set_progress_display(self, processed: int, total: int) -> None:
        processed = max(0, processed)
        total = max(0, total)
        if total:
            value = (processed * 100) // total
        else:
            value = 100 if processed > 0 else 0
        value = max(0, min(100, value))
        self.progress.setValue(value)
        eta = self._estimate_eta(processed, total)
        eta_segment = f" • ETA {eta}" if eta else ""
        self.progress.setFormat(
            f"  {value}% • {self._human_bytes(processed)} / {self._human_bytes(total)}{eta_segment}"
        )

    def _set_button_variant(self, button: QPushButton, variant: str) -> None:
        button.setProperty("variant", variant)
        button.setCursor(Qt.PointingHandCursor)
        self._repolish(button)

    def _apply_shadow(self, widget: QWidget, *, blur: int, alpha: int, y_offset: int) -> None:
        effect = QGraphicsDropShadowEffect(self)
        effect.setBlurRadius(blur)
        effect.setColor(QColor(0, 0, 0, alpha))
        effect.setOffset(0, y_offset)
        widget.setGraphicsEffect(effect)

    def _estimate_eta(self, processed: int, total: int) -> str | None:
        if (
            not self._hash_start_time
            or total <= 0
            or processed <= 0
            or processed >= total
        ):
            return None
        elapsed = time.monotonic() - self._hash_start_time
        if elapsed <= 0:
            return None
        rate = processed / elapsed
        if rate <= 0:
            return None
        remaining = max(0, total - processed)
        seconds = remaining / rate
        if seconds < 1:
            return "<1s"
        seconds = int(seconds)
        minutes, secs = divmod(seconds, 60)
        if minutes < 1:
            return f"{secs}s"
        hours, mins = divmod(minutes, 60)
        if hours == 0:
            return f"{mins}m {secs:02d}s"
        return f"{hours}h {mins:02d}m"

    def _refresh_splitter_handles(self) -> None:
        if not hasattr(self, "splitter"):
            return
        orientation = "vertical" if self.splitter.orientation() == Qt.Vertical else "horizontal"
        cursor = Qt.SizeVerCursor if orientation == "vertical" else Qt.SizeHorCursor
        for index in range(1, self.splitter.count()):
            handle = self.splitter.handle(index)
            if not handle:
                continue
            handle.setObjectName("MainSplitterHandle")
            handle.setCursor(cursor)
            self._position_handle_grip(handle, orientation)
            self._repolish(handle)

    def _position_handle_grip(self, handle: QWidget, orientation: str) -> None:
        grip = handle.findChild(QFrame, "HandleGrip")
        if grip is None:
            grip = QFrame(handle)
            grip.setObjectName("HandleGrip")
            grip.setAttribute(Qt.WA_TransparentForMouseEvents)
        if orientation == "vertical":
            grip.resize(68, 4)
        else:
            grip.resize(4, 68)
        grip.setProperty("direction", orientation)
        grip.move(max(0, handle.width() - grip.width()) // 2, max(0, handle.height() - grip.height()) // 2)
        grip.show()
        self._repolish(grip)

    def _available_height(self) -> int:
        height = max(0, self.height())
        if getattr(self, "_title_bar", None):
            height -= self._title_bar.height()
        if self.statusBar():
            height -= self.statusBar().height()
        return max(0, height)

    def _apply_height_mode(self, mode: str) -> None:
        if self._height_mode == mode:
            return
        self._height_mode = mode
        profiles = {
            "spacious": {
                "content_top": 32,
                "content_bottom": 32,
                "card_padding": 24,
                "section_spacing": 24,
                "hero_padding": 28,
                "hero_spacing": 12,
                "hero_max": 360,
                "show_metrics": True,
            },
            "tight": {
                "content_top": 26,
                "content_bottom": 24,
                "card_padding": 20,
                "section_spacing": 18,
                "hero_padding": 22,
                "hero_spacing": 10,
                "hero_max": 300,
                "show_metrics": True,
            },
            "compressed": {
                "content_top": 18,
                "content_bottom": 16,
                "card_padding": 16,
                "section_spacing": 14,
                "hero_padding": 16,
                "hero_spacing": 8,
                "hero_max": 240,
                "show_metrics": False,
            },
        }
        config = profiles[mode]
        if hasattr(self, "_content_layout"):
            margins = self._content_layout.contentsMargins()
            self._content_layout.setContentsMargins(
                margins.left(),
                config["content_top"],
                margins.right(),
                config["content_bottom"],
            )
            self._content_layout.setSpacing(min(self._content_layout.spacing(), config["section_spacing"]))
        for layout_attr in ("_controls_layout", "_results_layout"):
            layout = getattr(self, layout_attr, None)
            if layout is None:
                continue
            pad = min(layout.contentsMargins().left(), config["card_padding"])
            layout.setContentsMargins(pad, pad, pad, pad)
            layout.setSpacing(min(layout.spacing(), config["section_spacing"]))
        if hasattr(self, "_hero_layout"):
            pad = min(self._hero_layout.contentsMargins().left(), config["hero_padding"])
            self._hero_layout.setContentsMargins(pad, pad, pad, pad)
            self._hero_layout.setSpacing(min(self._hero_layout.spacing(), config["hero_spacing"]))
        if hasattr(self, "hero_card"):
            self.hero_card.setMaximumHeight(config["hero_max"])
        if hasattr(self, "_hero_metrics_container"):
            self._hero_metrics_container.setVisible(config["show_metrics"])

    def _update_scrollbar_policy(self) -> None:
        scroll = getattr(self, "_scroll_area", None)
        content = getattr(self, "_content_widget", None)
        if scroll is None or content is None:
            return
        viewport = scroll.viewport()
        if viewport is None:
            return
        content_height = content.sizeHint().height()
        viewport_height = viewport.height()
        needs_scroll = content_height - viewport_height > 6
        policy = Qt.ScrollBarAsNeeded if needs_scroll else Qt.ScrollBarAlwaysOff
        if scroll.verticalScrollBarPolicy() != policy:
            scroll.setVerticalScrollBarPolicy(policy)

    def _repolish(self, widget: QWidget) -> None:
        widget.style().unpolish(widget)
        widget.style().polish(widget)
        widget.update()

    def setWindowTitle(self, title: str) -> None:
        super().setWindowTitle(title)
        if hasattr(self, "_title_bar") and self._title_bar:
            self._title_bar.set_title(title)

    def setWindowIcon(self, icon: QIcon) -> None:
        super().setWindowIcon(icon)
        if hasattr(self, "_title_bar") and self._title_bar:
            self._title_bar.set_icon(icon)

    def nativeEvent(self, eventType, message):  # pragma: no cover - requires Windows GUI
        if eventType == "windows_generic_MSG":
            msg = wintypes.MSG.from_address(message.__int__())
            if msg.message == WM_NCHITTEST:
                hit = self._handle_hit_test(msg)
                if hit:
                    return True, hit
        return super().nativeEvent(eventType, message)

    def _handle_hit_test(self, msg) -> int:
        if self.isMaximized():
            return 0
        border = self._resize_border
        l_param = msg.lParam
        x = ctypes.c_short(l_param & 0xFFFF).value
        y = ctypes.c_short((l_param >> 16) & 0xFFFF).value
        global_pos = QPoint(x, y)
        local_pos = self.mapFromGlobal(global_pos)
        rect = self.rect()
        on_left = -border <= local_pos.x() <= border
        on_right = -border <= (rect.width() - local_pos.x()) <= border
        on_top = -border <= local_pos.y() <= border
        on_bottom = -border <= (rect.height() - local_pos.y()) <= border

        if on_top and on_left:
            return HTTOPLEFT
        if on_top and on_right:
            return HTTOPRIGHT
        if on_bottom and on_left:
            return HTBOTTOMLEFT
        if on_bottom and on_right:
            return HTBOTTOMRIGHT
        if on_left:
            return HTLEFT
        if on_right:
            return HTRIGHT
        if on_top:
            return HTTOP
        if on_bottom:
            return HTBOTTOM
        return 0


def main() -> int:
    _set_app_user_model_id(APP_USER_MODEL_ID)
    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    app.setStyleSheet(load_stylesheet())
    if ICON_PATH.exists():
        app_icon = QIcon(str(ICON_PATH))
        app.setWindowIcon(app_icon)
    else:
        app_icon = None
    window = MainWindow()
    if app_icon is not None:
        window.setWindowIcon(app_icon)
    window.show()
    return app.exec()


if __name__ == "__main__":
    sys.exit(main())
