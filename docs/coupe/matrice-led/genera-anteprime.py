"""Draws what the UNO Q's LED matrix is meant to show, for the design in ../matrice-led.md.

These are simulations, not photos: nothing here runs on the board yet. Run it from anywhere with
any Python 3; it needs nothing beyond the standard library and writes its PNGs next to itself.
Frames with more than one image are animated PNGs, which browsers and GitHub play by themselves.
"""
import os
import struct
import zlib

ROWS, COLS = 8, 13  # the matrix: 104 LEDs
BRIGHT, MEDIUM, DIM, OFF = 7, 4, 2, 0  # it has 8 brightness levels, 0 to 7

GLYPHS = {
    "A": "010 101 111 101 101", "B": "110 101 110 101 110", "C": "011 100 100 100 011",
    "D": "110 101 101 101 110", "E": "111 100 110 100 111", "G": "011 100 101 101 011",
    "I": "111 010 010 010 111", "K": "101 101 110 101 101", "L": "100 100 100 100 111",
    "M": "101 111 111 101 101", "N": "110 101 101 101 101", "O": "010 101 101 101 010",
    "P": "110 101 110 100 100", "R": "110 101 110 101 101", "S": "111 100 111 001 111",
    "T": "111 010 010 010 010", "U": "101 101 101 101 111", "V": "101 101 101 101 010",
    "X": "101 101 010 101 101", "Y": "101 101 010 010 010", "1": "010 110 010 010 111",
    "2": "110 001 010 100 111", "4": "101 101 111 001 001", "?": "110 001 010 000 010",
}

TEXT_TOP = 1  # text takes rows 1 to 5
PIP_ROW = 7  # the faults' dots
MIL_COLUMN = 12  # the "!" of a fault that turns on the engine warning light
TEXT_COLUMNS = 11  # so text, centred in columns 0 to 10, never reaches it


def blank():
    return [[OFF] * COLS for _ in range(ROWS)]


def text(frame, label, level):
    width = len(label) * 4 - 1
    left = (TEXT_COLUMNS - width) // 2
    for i, char in enumerate(label):
        for row, bits in enumerate(GLYPHS[char].split()):
            for col, bit in enumerate(bits):
                if bit == "1":
                    frame[TEXT_TOP + row][left + i * 4 + col] = level


def pips(frame, count, current, level=DIM):
    """One dot per fault along the bottom, the one on show brighter."""
    step = 2 if count <= 6 else 1
    for i in range(min(count, TEXT_COLUMNS)):
        frame[PIP_ROW][i * step] = BRIGHT if i == current else level


def recording(frame):
    frame[PIP_ROW][MIL_COLUMN] = DIM


def warning_light(frame):
    for row in (1, 2, 3, 5):
        frame[row][MIL_COLUMN] = BRIGHT


def screen(label, level, *extras):
    frame = blank()
    text(frame, label, level)
    for extra in extras:
        extra(frame)
    return frame


def faults(labels, recording_on=True, light=False):
    """Active faults, one at a time for 2 seconds each."""
    images = []
    for current, label in enumerate(labels):
        frame = screen(label, BRIGHT, lambda f: pips(f, len(labels), current))
        if recording_on:
            recording(frame)
        if light:
            # The "!" blinks once a second, the fault stays put.
            lit = [row[:] for row in frame]
            warning_light(lit)
            images += [(lit, 500), (frame, 500)] * 2
        else:
            images.append((frame, 2000))
    return images


def starting():
    images = []
    for col in list(range(COLS)) + list(range(COLS - 2, 0, -1)):
        frame = blank()
        frame[3][col] = frame[4][col] = MEDIUM
        images.append((frame, 90))
    return images


def waiting():
    images = []
    for lit in range(4):
        frame = blank()
        for i in range(3):
            level = BRIGHT if i < lit else DIM
            for row in (3, 4):
                for col in (2 + i * 4, 3 + i * 4):
                    frame[row][col] = level
        images.append((frame, 500))
    return images


def link_lost():
    frame = blank()
    for col in (0, 1, 2, 3, 9, 10, 11, 12):
        frame[3][col] = MEDIUM
    for row, col in [(1, 4), (2, 5), (3, 6), (4, 7), (5, 8), (1, 8), (2, 7), (4, 5), (5, 4)]:
        frame[row][col] = BRIGHT
    return [(frame, 1000), (blank(), 1000)]


def cable_power():
    return [(screen("12V", BRIGHT), 1000), (blank(), 1000)]


ENGINE_CODES = ["VAE", "MIX", "IAT", "KNK", "ECT", "O2", "MAP", "TPS", "CAM", "SPS", "EPR", "EEP",
                "NEU", "DIG", "RAM", "SMO"]
IMMOBILISER_CODES = ["SYN", "STA", "UNI", "C4", "BCK", "KEY", "UNR", "LNK"]

SCREENS = {
    "01-avvio": starting(),
    "02-cerca-centralina": waiting(),
    "03-tutto-ok": [(screen("OK", DIM, recording), 1000)],
    "04-un-errore": faults(["TPS"]),
    "05-spia-motore": faults(["MAP"], light=True),
    "06-piu-errori": faults(["TPS", "MAP", "O2"]),
    "07-solo-memorizzati": [(screen("OK", DIM, recording, lambda f: pips(f, 3, None, 1)), 1000)],
    "08-cavo-senza-12v": cable_power(),
    "09-comunicazione-persa": link_lost(),
    "10-simulazione": [(screen("SIM", MEDIUM), 1000)],
    "11-dati-fermi": [(screen("?", DIM), 1000)],
}

# --- Drawing -------------------------------------------------------------------------------------

CELL, MARGIN = 26, 14
BOARD = (13, 17, 23)
UNLIT = (28, 35, 48)
BLUE = (70, 150, 255)


def led_sprite(level, cell=CELL):
    """One LED and its soft glow, as rows of RGB bytes."""
    strength = (level / 7) ** 0.8
    centre = (cell - 1) / 2
    rows = []
    for y in range(cell):
        row = bytearray()
        for x in range(cell):
            d = ((x - centre) ** 2 + (y - centre) ** 2) ** 0.5 / (cell * 0.34)
            body = max(0.0, min(1.0, (1.0 - d) * 4))  # a dot with a soft edge
            glow = max(0.0, 1.45 - d) ** 2 * 0.28 * strength
            for board, unlit, lit in zip(BOARD, UNLIT, BLUE):
                dot = unlit + (lit - unlit) * strength
                value = board + (dot - board) * body + (lit - board) * glow
                row.append(int(max(0, min(255, value))))
        rows.append(bytes(row))
    return rows


def raster(frame, cell=CELL, margin=MARGIN):
    """A frame as PNG scanlines."""
    sprites = {level: led_sprite(level, cell) for level in range(8)}
    edge = bytes(BOARD) * (COLS * cell + 2 * margin)
    side = bytes(BOARD) * margin
    lines = [edge] * margin
    for row in frame:
        for y in range(cell):
            lines.append(side + b"".join(sprites[level][y] for level in row) + side)
    lines += [edge] * margin
    return lines


def chunk(kind, data):
    body = kind + data
    return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))


def encode(lines):
    return zlib.compress(b"".join(b"\0" + line for line in lines), 9)


def write_png(path, images):
    """A plain PNG for a single image, an animated one (looping) for several."""
    width, height = len(images[0][0][0]) // 3, len(images[0][0])
    out = [b"\x89PNG\r\n\x1a\n", chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))]
    if len(images) > 1:
        out.append(chunk(b"acTL", struct.pack(">II", len(images), 0)))
    sequence = 0
    for index, (lines, delay_ms) in enumerate(images):
        if len(images) > 1:
            out.append(chunk(b"fcTL", struct.pack(">IIIIIHHBB", sequence, width, height, 0, 0, delay_ms, 1000, 0, 0)))
            sequence += 1
        data = encode(lines)
        if index == 0:
            out.append(chunk(b"IDAT", data))
        else:
            out.append(chunk(b"fdAT", struct.pack(">I", sequence) + data))
            sequence += 1
    out.append(chunk(b"IEND", b""))
    with open(path, "wb") as f:
        f.write(b"".join(out))


def contact_sheet(labels, per_row):
    """Every code at a glance, each on its own small matrix."""
    cell, margin, gap = 11, 6, 6
    tile_w, tile_h = COLS * cell + 2 * margin, ROWS * cell + 2 * margin
    tiles = [raster(screen(label, BRIGHT), cell, margin) for label in labels]
    rows = (len(tiles) + per_row - 1) // per_row
    width = per_row * tile_w + (per_row - 1) * gap
    background = bytes((30, 36, 46))
    lines = []
    for r in range(rows):
        if r:
            lines += [background * width] * gap
        row_tiles = tiles[r * per_row:(r + 1) * per_row]
        for y in range(tile_h):
            parts = []
            for i in range(per_row):
                parts.append(row_tiles[i][y] if i < len(row_tiles) else background * tile_w)
            lines.append((background * gap).join(parts))
    return lines


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    for name, images in SCREENS.items():
        write_png(os.path.join(here, name + ".png"), [(raster(frame), delay) for frame, delay in images])
    write_png(os.path.join(here, "sigle-centralina.png"), [(contact_sheet(ENGINE_CODES, 4), 1000)])
    write_png(os.path.join(here, "sigle-immobilizer.png"), [(contact_sheet(IMMOBILISER_CODES, 4), 1000)])


if __name__ == "__main__":
    main()
