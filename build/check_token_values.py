"""Guard rail: every colour token used in the window markup must resolve to the same value the
inline literal used to have. Run this after touching the palette so a "tokenize" pass can never
silently shift a colour.

Usage:
    python check_token_values.py
"""
from pathlib import Path
import re
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ROOT = Path(__file__).resolve().parents[1]
THEME = ROOT / 'source' / 'HowToFishTrainer' / 'themes' / 'theme.xaml'
WINDOWS = [
    ROOT / 'source' / 'HowToFishTrainer' / 'HowToFishTrainer' / 'App' / 'MainWindow.xaml',
    ROOT / 'source' / 'HowToFishTrainer' / 'HowToFishTrainer' / 'App' / 'ActivationWindow.xaml',
]

# Baseline palette taken from the verified build before tokenization. Any token that resolves to a
# different value than the literal it replaced would show up here.
EXPECTED = {
    'AccentWashBrush': '#F2F6F7',
    'SuccessSoftBrush': '#EDF8F2',
    'WarningSoftBrush': '#FFF9EA',
    'DangerSoftBrush': '#FCE8EA',
    'AccentLineBrush': '#B9CDD5',
    'WarningLineBrush': '#ECD9A8',
    'DangerLineBrush': '#E8A6AE',
    'SurfaceBrush': '#F1F5F7',
    'SurfaceBorderBrush': '#D6E3E8',
    'FieldBorderBrush': '#D2E0E5',
    'ProgressTrackBrush': '#EBEFF1',
    'CardStateAppliedBrush': '#E6F4EC',
    'CardStateWaitBrush': '#FFF0C8',
    'CardStateIdleBrush': '#EDF1F3',
    'CardStateCountBrush': '#EDF2F4',
    'CardStateRiskSoftBrush': '#FFF5EF',
    'CardStateRiskBrush': '#FFFBFB',
    'AccentTintBrush': '#F4F8F9',
    'AccentEdgeBrush': '#A8C3CC',
    # Deliberately changed from the inherited #B9CDD5. The input border is the only boundary cue
    # on a white field, so it was darkened within the same hue: contrast on white 1.65:1 -> 2.28:1.
    # A deliberate palette change, not a refactor, so it is pinned here explicitly.
    'FieldLineBrush': '#96B0BB',
}

# Literals that are intentionally inline: pure white fills and the neutral black press overlay
# (a scrim, not a palette colour).
ALLOWED_LITERALS = {'"#FFFFFFFF"', '"#FFFFFF"', '"#00FFFFFF"', '"#000000"'}

# The core palette from the hand-off; these must never drift.
CORE = {
    'AccentBrush': '#466E7D',
    'AccentDarkBrush': '#32525E',
    'AccentSoftBrush': '#E9EEF1',
    'BgBrush': '#F8F6FA',
    'PanelBrush': '#FCFBFD',
    'CardBrush': '#FFFFFF',
    'TextBrush': '#251F2A',
    'MutedBrush': '#776D7C',
    'LineBrush': '#DEE5E8',
    'SuccessBrush': '#347B5C',
    'WarningBrush': '#95651F',
    'DangerBrush': '#B24C62',
    'GoldBrush': '#8C6A32',
}


def main() -> None:
    theme = THEME.read_text(encoding='utf-8')
    tokens = {k: v.upper() for k, v in re.findall(
        r'<SolidColorBrush x:Key="([^"]+)" Color="(#[0-9A-Fa-f]{6})"', theme)}

    failures = []
    print(f'theme tokens: {len(tokens)}')
    for name, want in {**CORE, **EXPECTED}.items():
        got = tokens.get(name)
        if got is None:
            failures.append(f'{name}: missing from theme')
            print(f'  MISSING  {name}')
            continue
        status = 'OK  ' if got == want.upper() else 'WRONG'
        if got != want.upper():
            failures.append(f'{name}: {got} != {want}')
        print(f'  {status} {name:24} {got}  (expected {want})')

    # Any literal colour left in the window markup is a future maintenance trap.
    for path in WINDOWS:
        text = path.read_text(encoding='utf-8')
        literals = sorted({c for c in re.findall(r'"#[0-9A-Fa-f]{6,8}"', text)} - ALLOWED_LITERALS)
        print(f'\n{path.name}: {len(literals)} inline literals remaining (excluding white/scrim)')
        for item in literals:
            print('   ' + item)
            failures.append(f'{path.name}: inline literal {item}')

    print()
    if failures:
        print(f'FAILURES ({len(failures)}):')
        for item in failures:
            print('   ' + item)
        raise SystemExit(1)
    print('palette verified: every token matches the value it replaced, no inline literals left')


if __name__ == '__main__':
    main()
