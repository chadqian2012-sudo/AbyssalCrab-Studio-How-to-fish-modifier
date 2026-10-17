"""Route every literal animation Duration in the theme through a motion token.

Why: 123 animations used literal durations, so there was no way to make the whole UI degrade to
"reduced motion" without editing each one. Mapping them onto Motion* tokens means a single
resource swap can neutralise motion, and the durations stay byte-identical in value.

Idempotent: running it twice makes no further changes.
"""
from pathlib import Path
import collections
import re
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ROOT = Path(__file__).resolve().parents[1]
TARGET = ROOT / 'source' / 'HowToFishTrainer' / 'themes' / 'theme.xaml'

# literal -> motion token, chosen by the semantic role of the dominant group.
# Values are unchanged: MotionHover/Status/Release/Switch/Page/Select carry exactly these numbers.
MAPPING = {
    '0:0:0.12': 'MotionPress',    # press-in and the tightest hover beats
    '0:0:0.15': 'MotionStatus',   # hover / colour / opacity transitions
    '0:0:0.18': 'MotionRelease',  # release, focus, expand
    '0:0:0.22': 'MotionSwitch',   # switch travel, larger moves
    '0:0:0.24': 'MotionPage',     # page cross-fade
}


def main() -> None:
    text = TARGET.read_text(encoding='utf-8')
    before = collections.Counter(re.findall(r'Duration="([^"]+)"', text))

    # 1) route literals through tokens
    total = 0
    for literal, token in MAPPING.items():
        pattern = re.compile(r'Duration="' + re.escape(literal) + r'"')
        text, count = pattern.subn(f'Duration="{{StaticResource {token}}}"', text)
        total += count

    # 2) the dangling historical key: it is not declared in this theme, so the markup compiler
    #    would fail. It sat on a 0.18s animation; MotionRelease is the same value and is declared.
    text, dangling = re.subn(r'Duration="\{StaticResource DurationBase\}"',
                             'Duration="{StaticResource MotionRelease}"', text)

    TARGET.write_text(text, encoding='utf-8', newline='\n')

    after = collections.Counter(re.findall(r'Duration="([^"]+)"', text))
    print(f'rewrote {total} literal durations through motion tokens')
    print(f'replaced {dangling} dangling DurationBase reference(s)')
    print()
    print('before:', dict(before))
    print('after :', dict(after))
    remaining = {k: v for k, v in after.items() if 'StaticResource' not in k}
    if remaining:
        print('\nSTILL LITERAL (unexpected):', remaining)
        raise SystemExit(1)
    print('\nall Duration attributes now reference a motion token')


if __name__ == '__main__':
    main()
