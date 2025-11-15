from __future__ import annotations

import hashlib
from pathlib import Path

import pytest

from src.hash_utils import hash_file, hash_targets


@pytest.fixture()
def sample_root(tmp_path: Path) -> Path:
    files = {
        "a.txt": b"spectra",
        "b.bin": b"hash studio",
        "folder/c.txt": b"nested",
    }
    for relative, data in files.items():
        file_path = tmp_path / relative
        file_path.parent.mkdir(parents=True, exist_ok=True)
        file_path.write_bytes(data)
    return tmp_path


def test_hash_file_returns_expected_digest(sample_root: Path):
    target = sample_root / "a.txt"
    result = hash_file(target, "sha256", 1024)
    expected = hashlib.sha256(target.read_bytes()).hexdigest()
    assert result.digest == expected


def test_hash_targets_handles_mixed_inputs(sample_root: Path):
    target_files = list(hash_targets([sample_root], "md5", 1024))
    assert len(target_files) == 3
    digests = {res.path.name: res.digest for res in target_files}
    assert digests["c.txt"] == hashlib.md5((sample_root / "folder" / "c.txt").read_bytes()).hexdigest()


def test_hash_targets_reports_byte_progress(sample_root: Path):
    total_bytes = sum(file.stat().st_size for file in sample_root.rglob("*") if file.is_file())
    calls: list[tuple[int, int]] = []

    list(
        hash_targets(
            [sample_root],
            "sha1",
            4,
            on_progress=lambda processed, total: calls.append((processed, total)),
        )
    )

    assert calls, "Expected progress callback to be invoked"
    assert calls[-1] == (total_bytes, total_bytes)
    if total_bytes:
        assert any(processed > 0 for processed, _ in calls)
