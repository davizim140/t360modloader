#!/usr/bin/env python3
"""Independent verifier for the XEX2 page-descriptor SHA-1 chain."""
import argparse
import hashlib
import struct
from pathlib import Path

def be32(data, off):
    return struct.unpack_from(">I", data, off)[0]

def verify(xex_path: Path, image_path: Path):
    xex = xex_path.read_bytes()
    image = image_path.read_bytes()
    if xex[:4] != b"XEX2":
        raise ValueError("not an XEX2 file")
    sec = be32(xex, 0x10)
    image_size = be32(xex, sec + 4)
    flags = be32(xex, sec + 0x10C)
    expected_root = xex[sec + 0x114:sec + 0x128]
    count = be32(xex, sec + 0x180)
    page_size = 0x1000 if flags & 0x10000000 else 0x10000
    if len(image) != image_size:
        raise ValueError(f"basefile size {len(image)} != XEX image_size {image_size}")
    if count == 0:
        raise ValueError("XEX has no page descriptors")
    desc_off = sec + 0x184
    if desc_off + count * 24 > len(xex):
        raise ValueError("page descriptor table exceeds XEX header")

    cursor = 0
    for i in range(count):
        desc = xex[desc_off + i * 24:desc_off + (i + 1) * 24]
        info = be32(desc, 0)
        pages = info >> 4
        if pages == 0:
            raise ValueError(f"descriptor {i} has zero pages")
        declared = pages * page_size
        page = image[cursor:min(cursor + declared, len(image))]
        if len(page) < declared:
            page += bytes(declared - len(page))
        actual = hashlib.sha1(page + desc).digest()
        if actual != expected_root:
            raise ValueError(f"descriptor {i} hash mismatch")
        expected_root = desc[4:24]
        cursor += declared

    if cursor < len(image):
        raise ValueError(f"descriptors cover {cursor} bytes, image has {len(image)}")
    return {"status":"page-hash-chain-pass","descriptor_count":count,"page_size":page_size,
            "image_size":image_size,"covered_bytes":cursor}

if __name__ == "__main__":
    p=argparse.ArgumentParser()
    p.add_argument("xex",type=Path)
    p.add_argument("basefile",type=Path)
    a=p.parse_args()
    print(verify(a.xex,a.basefile))
