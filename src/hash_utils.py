from __future__ import annotations

import hashlib
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Iterable, Iterator, Sequence

from .models import HashResult

CHUNK_SIZES = {
    "Standard": 1 << 20,  # 1 MiB
    "Velocissimo": 1 << 22,  # 4 MiB
    "Ultra": 1 << 24,  # 16 MiB
}

SUPPORTED_ALGORITHMS: tuple[str, ...] = (
    "MD5",
    "SHA1",
    "SHA224",
    "SHA256",
    "SHA384",
    "SHA512",
    "BLAKE2b",
    "BLAKE2s",
    "SHA3_256",
    "SHA3_512",
)


class HashCalculationError(RuntimeError):
    """Raised whenever hashing a given file fails."""


class HashCancelled(RuntimeError):
    """Raised internally to stop hashing early."""


@dataclass(slots=True)
class HashTarget:
    path: Path


def iter_target_files(targets: Sequence[Path]) -> Iterator[Path]:
    for target in targets:
        target = target.expanduser().resolve()
        if target.is_file():
            yield target
        elif target.is_dir():
            for file in target.rglob("*"):
                if file.is_file():
                    yield file
        else:
            continue


def hash_file(
    path: Path,
    algorithm: str,
    chunk_size: int,
    *,
    on_chunk: Callable[[int], None] | None = None,
    should_stop: Callable[[], bool] | None = None,
) -> HashResult:
    try:
        hasher = hashlib.new(algorithm.lower())
    except ValueError as exc:  # pragma: no cover - defensive
        raise HashCalculationError(f"Algoritmo non supportato: {algorithm}") from exc

    path = path.resolve()
    with path.open("rb") as stream:
        while True:
            if should_stop and should_stop():
                raise HashCancelled()
            chunk = stream.read(chunk_size)
            if not chunk:
                break
            hasher.update(chunk)
            if on_chunk:
                on_chunk(len(chunk))

    stat = path.stat()
    return HashResult(
        path=path,
        algorithm=algorithm.upper(),
        digest=hasher.hexdigest(),
        size_bytes=stat.st_size,
        modified_at=_timestamp(stat.st_mtime),
    )


def hash_targets(
    targets: Sequence[Path],
    algorithm: str,
    chunk_size: int,
    on_progress: Callable[[int, int], None] | None = None,
    should_stop: Callable[[], bool] | None = None,
) -> Iterator[HashResult]:
    files = list(dict.fromkeys(iter_target_files(targets)))
    total_bytes = sum(file_path.stat().st_size for file_path in files)
    processed_bytes = 0

    def _handle_chunk(chunk_len: int) -> None:
        nonlocal processed_bytes
        if should_stop and should_stop():
            raise HashCancelled()
        processed_bytes += chunk_len
        if on_progress:
            on_progress(processed_bytes, total_bytes)

    for file_path in files:
        if should_stop and should_stop():
            raise HashCancelled()
        result = hash_file(
            file_path,
            algorithm,
            chunk_size,
            on_chunk=_handle_chunk,
            should_stop=should_stop,
        )
        yield result

    if on_progress:
        on_progress(total_bytes, total_bytes)


def count_target_files(targets: Sequence[Path]) -> int:
    return sum(1 for _ in iter_target_files(targets))


def _timestamp(epoch: float):
    from datetime import datetime, timezone

    return datetime.fromtimestamp(epoch, tz=timezone.utc).astimezone()
