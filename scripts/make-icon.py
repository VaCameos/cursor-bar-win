#!/usr/bin/env python3
"""Write a small dual-bar ICO used as the executable icon."""

from __future__ import annotations

import struct
from pathlib import Path


def png(size: int) -> bytes:
    import zlib

    raw = bytearray()
    for y in range(size):
        raw.append(0)
        for x in range(size):
            inset = 2
            px = x - inset
            py = y - inset
            inner = size - inset * 2
            r, g, b, a = 0, 0, 0, 0
            if 0 <= px < inner and 0 <= py < inner:
                top = 3 <= py <= 3 + max(4, size // 5)
                bottom = py >= inner - max(3, size // 7) - 1
                fill_w = int(inner * (0.62 if top else 0.28))
                if top or bottom:
                    if px <= fill_w:
                        r, g, b, a = (56, 199, 122, 255) if top else (242, 194, 46, 255)
                    else:
                        r, g, b, a = (40, 40, 40, 70)
            raw.extend((r, g, b, a))

    def chunk(tag: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")


def ico(sizes: list[int]) -> bytes:
    images = [png(size) for size in sizes]
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    for size, data in zip(sizes, images):
        entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    return header + entries + b"".join(images)


def main() -> None:
    dest = Path(__file__).resolve().parents[1] / "src" / "CursorBar" / "Assets" / "app.ico"
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(ico([16, 32, 48, 256]))
    print(f"wrote {dest}")


if __name__ == "__main__":
    main()
