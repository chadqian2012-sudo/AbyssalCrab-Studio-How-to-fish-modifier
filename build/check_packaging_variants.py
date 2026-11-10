"""Measure the supported package modes through the production publish entry point."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import time

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet-path', type=Path, help='Optional dotnet executable, as accepted by Build-Release.ps1.')
parser.add_argument('--package-cache', type=Path, help='Optional existing NuGet package cache.')
parser.add_argument('--output-directory', type=Path, help='New directory for this run; existing directories are rejected.')
args = parser.parse_args()
powershell = shutil.which('pwsh') or shutil.which('powershell')
if not powershell:
    parser.error('PowerShell is required by Build-Release.ps1')
publish = root / 'build/Build-Release.ps1'
command = [powershell, '-NoProfile', '-NonInteractive', '-File', str(publish)]
for option, path in [('-DotnetPath', args.dotnet_path), ('-PackageCache', args.package_cache)]:
    if path is not None:
        command += [option, str(path.resolve(strict=True))]
if args.output_directory is not None:
    output = args.output_directory.resolve()
    output.mkdir(parents=True)  # Never include previous builds in distribution sizes.
else:
    runs = root / 'build/packaging-variants'
    runs.mkdir(parents=True, exist_ok=True)
    output = Path(tempfile.mkdtemp(prefix='run-', dir=runs))

cases = [('single-file', 'SingleFile'), ('compressed-single-file', 'CompressedSingleFile'),
         ('self-contained-folder', 'Folder'), ('framework-dependent-single-file', 'FrameworkDependent')]
report = output / 'packaging-variants.json' if args.output_directory else root / 'verification/packaging-variants.json'
results = []
for name, mode in cases:
    out = output / name
    out.mkdir()
    invocation = command + ['-PackageMode', mode, '-OutputDirectory', str(out), '-OutputName', 'AbyssalCrab Studio.exe']
    start = time.monotonic()
    with (out / 'publish.log').open('w', encoding='utf-8') as log:
        result = subprocess.run(invocation, stdout=log, stderr=subprocess.STDOUT)
    exe = out / ('HowToFishTrainer.exe' if mode == 'Folder' else 'AbyssalCrab Studio.exe')
    succeeded = result.returncode == 0 and exe.is_file()
    files = [p for p in out.rglob('*') if p.is_file() and p.suffix not in ['.pdb', '.log']]
    row = {'variant': name, 'package_mode': mode, 'publish_succeeded': succeeded,
           'process_exit_code': result.returncode, 'build_seconds': round(time.monotonic() - start, 2),
           'required_file_count': len(files), 'distribution_bytes': sum(p.stat().st_size for p in files),
           'exe_bytes': exe.stat().st_size if exe.is_file() else None,
           'exe_sha256': None,
           'runtime_required': '.NET 8 Windows Desktop Runtime x64' if mode == 'FrameworkDependent' else None,
           'output_directory': str(out), 'publish_command': invocation}
    if exe.is_file():
        digest = hashlib.sha256()
        with exe.open('rb') as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b''):
                digest.update(block)
        row['exe_sha256'] = digest.hexdigest()
    results.append(row)
    with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', dir=report.parent, delete=False) as temporary:
        json.dump(results, temporary, ensure_ascii=False, indent=2)
    os.replace(temporary.name, report)
    print(json.dumps(row, ensure_ascii=False), flush=True)
    if not succeeded:
        raise RuntimeError('Publish failed or the expected executable is missing: ' + name + '; see ' + str(out / 'publish.log'))
print('Package matrix report:', report, flush=True)
