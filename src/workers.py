from __future__ import annotations

from pathlib import Path
from typing import Sequence

from PySide6.QtCore import QObject, QThread, Signal

from .hash_utils import hash_targets, HashCancelled
from .models import HashResult


class HashWorker(QThread):
    progress = Signal(object, object)
    fileHashed = Signal(HashResult)
    completed = Signal()
    failed = Signal(str)

    def __init__(self, targets: Sequence[str | Path], algorithm: str, chunk_size: int) -> None:
        super().__init__()
        self._targets = [Path(t) for t in targets]
        self._algorithm = algorithm
        self._chunk_size = chunk_size
        self._stop_requested = False
        self._cancelled = False

    def request_stop(self) -> None:
        self._stop_requested = True

    @property
    def cancelled(self) -> bool:
        return self._cancelled

    def run(self) -> None:  # pragma: no cover - requires Qt loop
        should_stop = lambda: self._stop_requested  # noqa: E731 - tiny helper
        try:
            for result in hash_targets(
                self._targets,
                self._algorithm,
                self._chunk_size,
                on_progress=self.progress.emit,
                should_stop=should_stop,
            ):
                if self._stop_requested:
                    self._cancelled = True
                    break
                self.fileHashed.emit(result)
            self.completed.emit()
        except HashCancelled:
            self._cancelled = True
            self.completed.emit()
        except Exception as exc:  # defensive: surface any unexpected error
            self.failed.emit(str(exc))
