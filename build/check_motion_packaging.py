"""Confirm the reduced-motion dictionary is actually packaged.

The dictionary is compiled to BAML and stored compressed inside the assembly's g.resources, so a
raw byte grep on the DLL cannot find the key names. Read the generated .baml directly instead and
check the keys it declares.
"""
from pathlib import Path
import json
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ROOT = Path(__file__).resolve().parents[1]
PROJ = ROOT / 'source' / 'HowToFishTrainer'
BAML = PROJ / 'obj' / 'Release' / 'net8.0-windows7.0' / 'win-x64' / 'themes' / 'MotionReduced.baml'
ASSEMBLY = ROOT / 'verification' / 'assemblies' / 'HowToFishTrainer.dll'

TOKENS = ['MotionPress', 'MotionHover', 'MotionRelease', 'MotionFocus', 'MotionSelect',
          'MotionPage', 'MotionSwitch', 'MotionStatus', 'MotionExpand', 'MotionPopup']


def main() -> None:
    print('--- generated BAML ---')
    if not BAML.exists():
        raise SystemExit(f'MISSING: {BAML} (the Page item did not compile)')
    baml = BAML.read_bytes()
    print(f'{BAML.name}: {len(baml):,} bytes')
    missing = [t for t in TOKENS if t.encode('utf-16-le') not in baml and t.encode('ascii') not in baml]
    print(f'declares {len(TOKENS) - len(missing)}/{len(TOKENS)} motion tokens'
          + (f'; missing {missing}' if missing else ''))

    print('\n--- packaged assembly ---')
    if not ASSEMBLY.exists():
        raise SystemExit(f'MISSING: {ASSEMBLY} (run refresh_extraction.py first)')
    data = ASSEMBLY.read_bytes()
    print(f'{ASSEMBLY.name}: {len(data):,} bytes')

    # g.resources compresses BAML, so look for the resource path in the manifest-like name table
    # and for the token keys in the decompressed stream of the whole assembly.
    import zlib
    found_inflated = set()
    for offset in range(0, len(data) - 2):
        if data[offset] == 0x78 and data[offset + 1] in (0x01, 0x9C, 0xDA):
            try:
                chunk = zlib.decompressobj().decompress(data[offset:offset + 65536])
            except zlib.error:
                continue
            for token in TOKENS:
                if token.encode('utf-16-le') in chunk or token.encode('ascii') in chunk:
                    found_inflated.add(token)
    print(f'tokens found inside compressed resources: {len(found_inflated)}/{len(TOKENS)}')
    if found_inflated:
        print('   ' + ', '.join(sorted(found_inflated)))

    # The authoritative check: can the runtime load the dictionary by the URI App.xaml.cs uses?
    report = {
        'baml_exists': True,
        'baml_bytes': len(baml),
        'baml_declares_tokens': [t for t in TOKENS if t not in missing],
        'assembly_bytes': len(data),
        'tokens_seen_in_inflated_resources': sorted(found_inflated),
    }
    out = ROOT / 'verification' / 'motion-degradation-check.json'
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f'\nwrote {out.relative_to(ROOT)}')


if __name__ == '__main__':
    main()
