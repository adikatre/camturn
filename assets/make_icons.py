#!/usr/bin/env python3
"""Generates CamTurn's original icons (no third-party artwork, no dependencies beyond the standard library).

Design: a rounded tile with a white camera and a circular "turn" arrow around the lens.
  camturn.ico         app icon (blue tile)
  tray-idle.ico       grey tile                     (tooltip says "idle")
  tray-streaming.ico  blue tile + green dot badge   (tooltip says "streaming")
  tray-error.ico      grey tile + orange "!" badge  (tooltip says what is wrong)
Status is never conveyed by colour alone: badges differ in shape and the tray tooltip carries the text.

Run: python3 assets/make_icons.py   (writes into assets/icons/)
"""
import math
import os
import struct
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "icons")
SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
SS = 4  # supersampling factor per axis

BLUE = (31, 111, 235)
GREY = (90, 98, 112)
WHITE = (255, 255, 255)
GREEN = (46, 160, 67)
ORANGE = (219, 109, 40)


def rounded_rect(x, y, x0, y0, x1, y1, r):
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r and x0 <= x <= x1 and y0 <= y <= y1


def in_triangle(px, py, a, b, c):
    def s(p1, p2, p3):
        return (p1[0] - p3[0]) * (p2[1] - p3[1]) - (p2[0] - p3[0]) * (p1[1] - p3[1])

    d1, d2, d3 = s((px, py), a, b), s((px, py), b, c), s((px, py), c, a)
    neg = d1 < 0 or d2 < 0 or d3 < 0
    pos = d1 > 0 or d2 > 0 or d3 > 0
    return not (neg and pos)


def shade(u, v, tile, badge):
    """Colour at normalised coordinates (0..1, y down), or None for transparent."""
    # Badge first (drawn over everything, with a white outline ring).
    if badge == "dot":
        d = math.hypot(u - 0.78, v - 0.78)
        if d <= 0.17:
            return GREEN if d <= 0.13 else WHITE
    elif badge == "warn":
        tri = ((0.78, 0.55), (0.98, 0.97), (0.58, 0.97))
        outline = ((0.78, 0.49), (1.02, 1.0), (0.54, 1.0))
        if in_triangle(u, v, *tri):
            if 0.765 <= u <= 0.795 and (0.66 <= v <= 0.84 or 0.875 <= v <= 0.91):
                return WHITE
            return ORANGE
        if in_triangle(u, v, *outline):
            return WHITE

    if not rounded_rect(u, v, 0.04, 0.04, 0.96, 0.96, 0.2):
        return None

    # Turn arrow: a ring segment around the lens with an arrowhead.
    cx, cy = 0.5, 0.54
    d = math.hypot(u - cx, v - cy)
    ang = math.degrees(math.atan2(v - cy, u - cx)) % 360
    if 0.30 <= d <= 0.36 and not (300 <= ang <= 360):
        return WHITE
    head = ((0.5 + 0.33 * math.cos(math.radians(300)) - 0.09, cy + 0.33 * math.sin(math.radians(300)) - 0.03),
            (0.5 + 0.33 * math.cos(math.radians(300)) + 0.07, cy + 0.33 * math.sin(math.radians(300)) - 0.10),
            (0.5 + 0.33 * math.cos(math.radians(300)) + 0.05, cy + 0.33 * math.sin(math.radians(300)) + 0.08))
    if in_triangle(u, v, *head):
        return WHITE

    # Camera body with lens.
    if rounded_rect(u, v, 0.30, 0.42, 0.70, 0.68, 0.05) or rounded_rect(u, v, 0.40, 0.37, 0.52, 0.44, 0.02):
        if math.hypot(u - 0.5, v - 0.55) <= 0.075:
            return tile
        if math.hypot(u - 0.5, v - 0.55) <= 0.11:
            return WHITE
        return WHITE
    return tile


def render(size, tile, badge):
    px = []
    for y in range(size):
        for x in range(size):
            acc = [0, 0, 0, 0]
            for sy in range(SS):
                for sx in range(SS):
                    c = shade((x + (sx + 0.5) / SS) / size, (y + (sy + 0.5) / SS) / size, tile, badge)
                    if c is not None:
                        acc[0] += c[0]
                        acc[1] += c[1]
                        acc[2] += c[2]
                        acc[3] += 255
            n = SS * SS
            a = acc[3] // n
            if a == 0:
                px.append((0, 0, 0, 0))
            else:
                k = 255 * n / acc[3]
                px.append((min(255, int(acc[0] * k / n)), min(255, int(acc[1] * k / n)), min(255, int(acc[2] * k / n)), a))
    return px


def png(size, px):
    raw = b"".join(b"\x00" + bytes(c for p in px[y * size:(y + 1) * size] for c in p) for y in range(size))

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def dib(size, px):
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    rows = []
    for y in range(size - 1, -1, -1):
        rows.append(bytes(c for (r, g, b, a) in px[y * size:(y + 1) * size] for c in (b, g, r, a)))
    mask_row = ((size + 31) // 32) * 4
    mask = b"\x00" * (mask_row * size)
    return header + b"".join(rows) + mask


def ico(path, tile, badge):
    images = []
    for s in SIZES:
        px = render(s, tile, badge)
        images.append((s, png(s, px) if s >= 256 else dib(s, px)))
    head = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    data = b""
    for s, blob in images:
        entries += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(blob), offset + len(data))
        data += blob
    with open(path, "wb") as f:
        f.write(head + entries + data)


def main():
    os.makedirs(OUT, exist_ok=True)
    ico(os.path.join(OUT, "camturn.ico"), BLUE, None)
    ico(os.path.join(OUT, "tray-idle.ico"), GREY, None)
    ico(os.path.join(OUT, "tray-streaming.ico"), BLUE, "dot")
    ico(os.path.join(OUT, "tray-error.ico"), GREY, "warn")
    for name, tile, badge in [("camturn", BLUE, None), ("tray-idle", GREY, None), ("tray-streaming", BLUE, "dot"), ("tray-error", GREY, "warn")]:
        with open(os.path.join(OUT, name + "-256.png"), "wb") as f:
            f.write(png(256, render(256, tile, badge)))
    print("icons written to", OUT)


if __name__ == "__main__":
    main()
