from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from pathlib import Path


@dataclass(frozen=True, slots=True)
class HashResult:
    """Container for a single hashing outcome."""

    path: Path
    algorithm: str
    digest: str
    size_bytes: int
    modified_at: datetime

    def as_row(self) -> tuple[str, str, str, str, str]:
        """Return data formatted for displaying inside the table widget."""

        human_size = (
            f"{self.size_bytes:,} B" if self.size_bytes < 1024 else format_size(self.size_bytes)
        )
        timestamp = self.modified_at.strftime("%d/%m/%Y %H:%M:%S")
        return (
            self.path.name,
            str(self.path),
            self.algorithm,
            self.digest,
            human_size,
            timestamp,
        )


def format_size(size: int) -> str:
    # Simple human-readable size formatter to avoid extra dependencies here.
    for unit in ("KB", "MB", "GB", "TB", "PB"):
        size /= 1024
        if size < 1024:
            return f"{size:.2f} {unit}"
    return f"{size:.2f} EB"
