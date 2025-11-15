from __future__ import annotations

PRIMARY_COLOR = "#00c2ff"
PRIMARY_DARK = "#0097d6"
ACCENT_COLOR = "#34e0a1"
DANGER_COLOR = "#ff7b00"
BACKGROUND = "#050b16"
CARD = "#111a2b"
TEXT = "#f8fafc"
MUTED = "#94a3b8"
SOFT = "#152238"


def load_stylesheet(scale: float = 1.0) -> str:
    scale = max(0.6, min(scale, 1.15))

    def px(value: float) -> int:
        if value >= 500:
            return int(value)
        return max(1, int(round(value * scale)))

    font_base = px(14)
    status_pad_v = px(6)
    status_pad_h = px(18)
    status_font = px(13)
    splitter_margin = px(26)
    hero_title = px(30)
    hero_title_compact = px(24)
    hero_title_mini = px(20)
    hero_subtitle = px(14)
    hero_subtitle_compact = px(12)
    card_radius = px(22)
    hero_card_radius = px(20)
    badge_pad_v = px(3)
    badge_pad_h = px(10)
    badge_font = px(11)
    search_radius = px(24)
    search_border = max(1, px(1))
    search_field_padding_v = px(4)
    search_field_font = px(15)
    form_label_font = px(12)
    section_title_font = px(18)
    button_radius = px(18)
    button_padding_v = px(10)
    button_padding_h = px(22)
    list_radius = px(20)
    list_padding = px(10)
    list_item_padding_v = px(6)
    list_item_padding_h = px(8)
    table_radius = px(20)
    table_padding = px(6)
    table_item_padding_v = px(10)
    table_item_padding_h = px(8)
    header_padding = px(10)
    header_font = px(13)
    progress_height = px(24)
    progress_font = px(14)
    progress_chunk_margin = px(2)
    combo_radius = px(16)
    combo_padding_v = px(10)
    combo_padding_h = px(16)
    combo_dropdown_width = px(26)
    scrollbar_width = px(10)
    scrollbar_margin_tb = px(12)
    scrollbar_margin_lr = px(2)
    scrollbar_handle_radius = px(4)

    return f"""
        QWidget {{
            background-color: {BACKGROUND};
            color: {TEXT};
            font-family: 'Segoe UI', 'Inter', sans-serif;
            font-size: {font_base}px;
        }}
        QLabel {{
            background-color: transparent;
        }}
        QScrollArea {{ background: transparent; }}
        QMainWindow::separator {{ background: transparent; }}
        QSplitter#MainSplitter::handle {{
            background-color: transparent;
            margin: {splitter_margin}px 0;
        }}
        QSplitter#MainSplitter::handle:horizontal {{
            margin: 0 {splitter_margin}px;
        }}
        QSplitter#MainSplitter::handle:hover,
        QSplitter#MainSplitter::handle:pressed {{
            background-color: rgba(0,194,255,0.08);
            border-radius: 999px;
        }}
        QFrame#HandleGrip {{
            background-color: rgba(255,255,255,0.18);
            border-radius: 999px;
        }}
        QFrame#HandleGrip[direction="horizontal"] {{
            background-color: rgba(0,194,255,0.22);
        }}
        QStatusBar {{
            background-color: rgba(5,11,22,0.9);
            border-top: 1px solid rgba(255,255,255,0.04);
            padding: {status_pad_v}px {status_pad_h}px;
            font-size: {status_font}px;
            color: rgba(148,163,184,0.9);
        }}
        QStatusBar::item {{
            border: none;
        }}

        QLabel#HeroTitle {{
            font-size: {hero_title}px;
            font-weight: 700;
            letter-spacing: 0.01em;
            color: {TEXT};
        }}
        QLabel#HeroTitle[sizeVariant="compact"] {{
            font-size: {hero_title_compact}px;
        }}
        QLabel#HeroTitle[sizeVariant="mini"] {{
            font-size: {hero_title_mini}px;
        }}

        QLabel#HeroSubtitle {{
            font-size: {hero_subtitle}px;
            color: rgba(248,250,252,0.85);
            line-height: 1.4em;
        }}
        QLabel#HeroSubtitle[sizeVariant="compact"] {{
            font-size: {hero_subtitle_compact}px;
            line-height: 1.3em;
        }}

        QFrame#Card {{
            background-color: {CARD};
            border-radius: {card_radius}px;
            border: 1px solid rgba(255,255,255,0.04);
        }}
        QFrame#HeroCard {{
            background: qlineargradient(x1:0, y1:0, x2:1, y2:1,
                stop:0 rgba(0,194,255,0.45), stop:0.7 rgba(19,99,238,0.32), stop:1 rgba(0,0,0,0.1));
            border-radius: {hero_card_radius}px;
            border: 1px solid rgba(148,163,184,0.35);
        }}
        QWidget#HeroMetrics {{
            background-color: transparent;
            border: none;
        }}
        QLabel#Badge {{
            background-color: rgba(255,255,255,0.15);
            border-radius: 999px;
            padding: {badge_pad_v}px {badge_pad_h}px;
            font-size: {badge_font}px;
            letter-spacing: 0.08em;
            text-transform: uppercase;
            color: {TEXT};
        }}
        QFrame#SearchContainer {{
            background-color: rgba(7,15,31,0.85);
            border-radius: {search_radius}px;
            border: {search_border}px solid rgba(0,194,255,0.18);
        }}
        QFrame#SearchContainer:hover {{
            border-color: rgba(0,194,255,0.4);
        }}
        QLineEdit#SearchField {{
            border: none;
            background: transparent;
            color: rgba(248,250,252,0.94);
            padding: {search_field_padding_v}px 0;
            font-size: {search_field_font}px;
            font-weight: 600;
            letter-spacing: 0.02em;
        }}
        QLineEdit#SearchField::placeholder {{
            color: rgba(148,163,184,0.75);
            font-weight: 500;
        }}
        QLabel#SearchIcon {{
            color: rgba(148,163,184,0.85);
        }}
        QLabel#FormLabel {{
            color: rgba(148,163,184,0.9);
            font-size: {form_label_font}px;
            letter-spacing: 0.08em;
        }}

        QLabel#SectionTitle {{
            font-size: {section_title_font}px;
            font-weight: 600;
            color: {TEXT};
        }}

        QPushButton {{
            border: 1px solid rgba(255,255,255,0.08);
            border-radius: {button_radius}px;
            padding: {button_padding_v}px {button_padding_h}px;
            font-weight: 600;
            color: {TEXT};
            background-color: rgba(255,255,255,0.05);
        }}
        QPushButton[variant="primary"] {{
            background: qlineargradient(x1:0, y1:0, x2:1, y2:1,
                stop:0 {PRIMARY_COLOR}, stop:1 {PRIMARY_DARK});
            color: #01121e;
            border: none;
        }}
        QPushButton[variant="accent"] {{
            background: qlineargradient(x1:0, y1:0, x2:1, y2:1,
                stop:0 {ACCENT_COLOR}, stop:1 #20b47d);
            color: #01120b;
            border: none;
        }}
        QPushButton[variant="danger"] {{
            background-color: {DANGER_COLOR};
            color: #1f0c00;
            border: none;
        }}
        QPushButton[variant="ghost"] {{
            background-color: rgba(255,255,255,0.08);
            border: 1px solid rgba(255,255,255,0.15);
        }}
        QPushButton:disabled {{
            background-color: rgba(148,163,184,0.25);
            color: rgba(10,16,32,0.7);
        }}
        QPushButton:hover:!disabled {{
            background-color: rgba(255,255,255,0.12);
        }}
        QPushButton[variant="primary"]:hover:!disabled {{
            background-color: {PRIMARY_DARK};
        }}
        QPushButton[variant="accent"]:hover:!disabled {{
            background-color: #1fb885;
        }}
        QPushButton[variant="danger"]:hover:!disabled {{
            background-color: #ff9000;
        }}

        QListWidget {{
            background-color: #070f1f;
            border: 1px solid rgba(148,163,184,0.18);
            border-radius: {list_radius}px;
            padding: {list_padding}px;
        }}
        QListWidget::item {{
            padding: {list_item_padding_v}px {list_item_padding_h}px;
            border-radius: {px(12)}px;
        }}
        QListWidget::item:selected {{
            background-color: rgba(0,194,255,0.18);
        }}
        QTableWidget {{
            background-color: #070f1f;
            border: 1px solid rgba(148,163,184,0.15);
            border-radius: {table_radius}px;
            padding: {table_padding}px;
            gridline-color: rgba(255,255,255,0.05);
        }}
        QTableWidget::item {{
            color: {TEXT};
            padding: {table_item_padding_v}px {table_item_padding_h}px;
        }}

        QHeaderView::section {{
            background-color: rgba(14,165,233,0.12);
            color: {TEXT};
            border: none;
            padding: {header_padding}px;
            font-size: {header_font}px;
            border-radius: 0;
        }}

        QProgressBar {{
            border: 1px solid rgba(148,163,184,0.25);
            border-radius: 999px;
            background-color: #050d1d;
            height: {progress_height}px;
            padding: 0;
            font-family: 'Segoe UI Semibold', 'Inter', sans-serif;
            font-size: {progress_font}px;
            font-weight: 600;
            letter-spacing: 0.04em;
            color: rgba(248,250,252,0.94);
        }}
        QProgressBar::chunk {{
            background-color: {PRIMARY_COLOR};
            border-radius: 999px;
            margin: {progress_chunk_margin}px;
        }}

        QComboBox, QLineEdit {{
            background-color: #070f1f;
            border: 1px solid rgba(148,163,184,0.25);
            border-radius: {combo_radius}px;
            padding: {combo_padding_v}px {combo_padding_h}px;
            color: {TEXT};
        }}
        QComboBox::drop-down {{ border: none; width: {combo_dropdown_width}px; }}
        QComboBox::down-arrow {{ image: none; border: none; }}

        QScrollBar:vertical {{
            background: transparent;
            width: {scrollbar_width}px;
            margin: {scrollbar_margin_tb}px {scrollbar_margin_lr}px;
        }}
        QScrollBar::handle:vertical {{
            background: rgba(255,255,255,0.15);
            border-radius: {scrollbar_handle_radius}px;
        }}
        QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {{ height: 0; }}
        QScrollBar:horizontal {{
            background: transparent;
            height: {scrollbar_width}px;
            margin: {scrollbar_margin_lr}px {scrollbar_margin_tb}px;
        }}
        QScrollBar::handle:horizontal {{
            background: rgba(255,255,255,0.15);
            border-radius: {scrollbar_handle_radius}px;
        }}
    """
