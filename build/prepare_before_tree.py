"""Compose a before/after sheet from the preserved UI source and the polished build.

The pre-round-2 sources are kept in build/ui-backup-before-round2; this script re-renders the
window from both source sets so the layout changes are visible side by side.
"""
from pathlib import Path
import json
import os
import shutil
import subprocess
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

root = Path(__file__).resolve().parents[1]
base = root.parent / 'howtofish-local-test'
sdk = Path(json.loads((base / 'build-tools/sdk-download.json').read_text(encoding='utf-8-sig'))['sdk_directory'])
net = sdk / 'shared/Microsoft.NETCore.App/8.0.31'
desktop = sdk / 'shared/Microsoft.WindowsDesktop.App/8.0.31'

backup = root / 'build' / 'ui-backup-before-round2'
out = root / 'verification' / 'polish-before'
out.mkdir(parents=True, exist_ok=True)


def main() -> None:
    # Build a "before" assembly by swapping the backed-up XAML into a scratch copy of the source
    # tree, publishing it, and rendering the same pages the after-run renders.
    scratch = root / 'build' / 'before-src'
    if scratch.exists():
        shutil.rmtree(scratch)
    shutil.copytree(root / 'source' / 'HowToFishTrainer', scratch / 'HowToFishTrainer')
    for name, relative in (
        ('MainWindow.xaml', Path('HowToFishTrainer/App/MainWindow.xaml')),
        ('ActivationWindow.xaml', Path('HowToFishTrainer/App/ActivationWindow.xaml')),
        ('theme.xaml', Path('themes/theme.xaml')),
    ):
        shutil.copy2(backup / name, scratch / 'HowToFishTrainer' / relative)
    print('prepared before-source tree at', scratch)
    print('run Build-Release style publish for this tree, then render with run_polish_audit.py')
    print('note: this script only prepares inputs; the caller publishes and renders.')


if __name__ == '__main__':
    main()
