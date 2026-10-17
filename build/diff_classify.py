"""Classify the pixel differences between two render passes.

For each differing pixel compare the before/after RGB values. One uniform
(old,new) colour pair per region means a colour swap; scattered colours across
many hues means the content moved (glyph antialiasing), which is a layout change.
"""
from pathlib import Path
import sys
from collections import Counter

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
BEFORE = ROOT / 'verification' / 'polish-baseline'
AFTER = ROOT / 'verification' / 'polish'


def main() -> None:
    name = sys.argv[1] if len(sys.argv) > 1 else 'polish-1460x900.png'
    before = Image.open(BEFORE / name).convert('RGB')
    after = Image.open(AFTER / name).convert('RGB')
    if before.size != after.size:
        print(f'size differs: {before.size} vs {after.size}')
        return

    width, height = before.size
    bdata = before.load()
    adata = after.load()
    pairs: Counter = Counter()
    rows: Counter = Counter()
    cols: Counter = Counter()
    for y in range(height):
        for x in range(width):
            pb = bdata[x, y]
            pa = adata[x, y]
            if pb != pa:
                pairs[(pb, pa)] += 1
                rows[y] += 1
                cols[x] += 1

    total = sum(pairs.values())
    print(f'{name}: {total} differing pixels, {len(pairs)} distinct colour pairs')
    print('\ntop colour transitions (old -> new):')
    for (old, new), count in pairs.most_common(12):
        print(f'   {old} -> {new}   x{count}')
    print(f'\ndistinct old colours: {len({p[0] for p in pairs})}')
    print(f'distinct new colours: {len({p[1] for p in pairs})}')

    if rows:
        print(f'\naffected rows: {min(rows)}..{max(rows)}  ({len(rows)} rows)')
        top_rows = sorted(rows.items())[:6]
        print(f'   first rows: {top_rows}')
    if cols:
        print(f'affected cols: {min(cols)}..{max(cols)}  ({len(cols)} cols)')


if __name__ == '__main__':
    main()
