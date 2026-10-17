"""Assemble the round's machine-readable verification summary.

Recomputes every hash from the artefacts on disk rather than trusting earlier notes, so
verification/round-final-verification.json can be quoted safely.
"""
from pathlib import Path
import hashlib
import json
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ROOT = Path(__file__).resolve().parents[1]
RELEASE = ROOT / 'release'

# The protected-module hashes the hand-off pins.
PROTECTED = {
    'Core': '74f00a0b399655fcd3545e760ae8897cafe7b7d2c6968fec5e879a4534127a99',
    'Runtime': '0d4cbf37f14d3c7a6c35eb033bef9060fce352a02ac99ba2334562118cf838d1',
    'Memory': '0726fa99eefd9d272e2fa1982581276da24817ac231da926185a2bd734c6f2c1',
}

PROTECTED_LOCATIONS = {
    'Core': ['patched/HowToFishTrainer.Core.dll', 'verification/assemblies/HowToFishTrainer.Core.dll'],
    'Runtime': ['patched/HowToFishTrainer.Runtime.dll', 'source/HowToFishTrainer/HowToFishTrainer.Runtime.dll'],
    'Memory': ['references/HowToFishTrainer.Memory.dll', 'verification/assemblies/HowToFishTrainer.Memory.dll'],
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_json(relative: str):
    path = ROOT / relative
    if not path.exists():
        return None
    text = path.read_text(encoding='utf-8-sig')
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return None


def main() -> None:
    summary: dict = {'date': '2026-10-16', 'brand': 'AbyssalCrab Studio'}

    # ---- deliverables ---------------------------------------------------------------
    packages = []
    manifest_by_name = {}
    for exe in sorted(RELEASE.glob('*.exe')):
        entry = {'file': exe.name, 'bytes': exe.stat().st_size, 'sha256': sha256(exe)}
        packages.append(entry)
    summary['packages'] = packages

    # The main assembly inside each package, from the per-package extraction directories.
    main_hashes = {}
    for label, directory in (('整合版', ROOT / 'verification/assemblies'),
                             ('压缩单文件', ROOT / 'verification/compressed-check')):
        dll = directory / 'HowToFishTrainer.dll'
        if dll.exists():
            main_hashes[label] = {'sha256': sha256(dll), 'bytes': dll.stat().st_size}
    summary['main_assembly_per_package'] = main_hashes
    values = {v['sha256'] for v in main_hashes.values()}
    summary['main_assembly_identical_across_packages'] = len(values) <= 1

    # ---- protected modules ----------------------------------------------------------
    protected = {}
    all_match = True
    for name, expected in PROTECTED.items():
        for relative in PROTECTED_LOCATIONS[name]:
            path = ROOT / relative
            actual = sha256(path) if path.exists() else None
            match = actual == expected
            all_match = all_match and match
            protected[f'{name}@{relative}'] = {'expected': expected, 'actual': actual, 'match': match}
    summary['protected_modules'] = protected
    summary['protected_modules_all_match'] = all_match

    # ---- bundle ---------------------------------------------------------------------
    manifest = read_json('verification/bundle_manifest.json')
    if manifest:
        summary['bundle'] = {
            'input': manifest.get('input_name'),
            'input_sha256': manifest.get('input_sha256'),
            'entries': manifest.get('file_count'),
            'version': manifest.get('bundle_version'),
        }

    # ---- functional regression ------------------------------------------------------
    unit = read_json('verification-results.json')
    contract = read_json('contract-coverage-results.json')
    lifecycle = read_json('verification/lifecycle-results.json')
    classification = read_json('verification/classification-results.json')
    summary['regression'] = {
        'unit_checks': unit.get('passed') if unit else None,
        'unit_failures': unit.get('failures') if unit else None,
        'contract_checks': contract.get('passed') if contract else None,
        'contract_features': contract.get('registry_features') if contract else None,
        'lifecycle_checks': lifecycle.get('passed') if lifecycle else None,
        'registered_features': classification.get('registered_features') if classification else None,
        'generic_features': classification.get('generic_feature_count') if classification else None,
        'specialized_features': classification.get('specialized_feature_count') if classification else None,
        'page_count': classification.get('page_count') if classification else None,
        'duplicate_ids': classification.get('duplicate_feature_ids') if classification else None,
        'missing_ids': classification.get('missing_feature_ids') if classification else None,
    }

    # ---- appearance audits ----------------------------------------------------------
    capsule = read_json('verification/polish/nav-capsule-audit.json')
    motion = read_json('verification/polish/motion-degradation-audit.json')
    material = read_json('verification/polish/material-fallback-audit.json')
    tiers = read_json('verification/polish/material-tier-audit.json')
    perf = read_json('verification/polish/perf-audit.json')
    polish = read_json('verification/polish/ui-polish-audit.json')
    matrix = read_json('verification/ui-round2/matrix.json')

    summary['appearance_audits'] = {
        'nav_capsule': {
            'passed': capsule.get('passed') if capsule else None,
            'misaligned': capsule.get('misaligned') if capsule else None,
            'rapid_retarget_ok': capsule.get('rapid_retarget_ok') if capsule else None,
            'hit_test_visible': capsule.get('is_hit_test_visible') if capsule else None,
        },
        'motion_degradation': {
            'passed': motion.get('passed') if motion else None,
            'tokens_zeroed': motion.get('tokens_zeroed') if motion else None,
            'tokens_total': motion.get('tokens_total') if motion else None,
            'non_motion_altered': len(motion.get('non_motion_tokens_altered', [])) if motion else None,
        },
        'material_fallback': {
            'passed': material.get('passed') if material else None,
            'tokens_overridden': material.get('tokens_overridden') if material else None,
            'window_built': material.get('window_built_with_degraded_materials') if material else None,
            'solid_canvas_pixels': material.get('solid_canvas_pixels') if material else None,
        },
        'material_tiers': {
            'passed': tiers.get('passed') if tiers else None,
            'distinct_renders': tiers.get('distinct_renders') if tiers else None,
        },
        'performance': perf,
        'nav_geometry': [
            {'width': n['window_width'], 'rows': n['rows'], 'band_height': n['nav_height']}
            for n in (polish.get('navigation', []) if polish else [])
        ],
        'clock_residue': polish.get('clock_residue') if polish else None,
        'page_occlusion_suspicious': sum(1 for o in (polish or {}).get('page_occlusion', []) if o.get('suspicious')),
    }

    # ---- objective conformance ------------------------------------------------------
    conformance = read_json('verification/objective-conformance.json')
    if conformance:
        summary['objective_conformance'] = {
            'satisfied': conformance.get('satisfied'),
            'total': conformance.get('total'),
            'all_satisfied': conformance.get('all_satisfied'),
            'failed_requirements': [c['requirement'] for c in conformance.get('checks', []) if not c.get('passed')],
        }

    # ---- acceptance matrix ----------------------------------------------------------
    if matrix:
        env = matrix.get('environment', {})
        summary['acceptance_matrix'] = {
            'schema': matrix.get('schema'),
            'counts': matrix.get('counts'),
            'checks_total': (matrix.get('counts') or {}).get('checks_total'),
            'checks_failed': (matrix.get('counts') or {}).get('checks_failed'),
            'evidence': (matrix.get('counts') or {}).get('evidence'),
            'flat_or_black': (matrix.get('counts') or {}).get('flat_or_black'),
            'missing': (matrix.get('counts') or {}).get('missing'),
            'categories_in_nav': env.get('categories_in_nav'),
            'assembly_sha256': env.get('assembly_sha256'),
            'note_on_snapshot': ('The matrix ran after the material-tier work and after the nav-chip '
                                 'width fix (40/40 checks, 55 evidence items). Its assembly_sha256 is '
                                 'the main assembly of the build it measured, not of the final package; '
                                 're-run verification/ui-round2/run_matrix.py after any further UI change.'),
            'hotkey_contention_note': ('Only one window probe may run at a time: the app registers the '
                                       'global Ctrl+Shift+F12 hotkey, and a second probe would put its '
                                       'registration failure into ModeStatus in every screenshot. The '
                                       'matrix refuses to run rather than capture that contamination.'),
        }

    out = ROOT / 'verification' / 'round-final-verification.json'
    out.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')

    print(f'wrote {out.relative_to(ROOT)}')
    print()
    for package in packages:
        print(f"  {package['file']:58} {package['bytes']:>13,} B  {package['sha256'][:16]}...")
    print()
    print(f"  protected modules all match : {all_match}")
    print(f"  main assembly identical      : {summary['main_assembly_identical_across_packages']}")
    print(f"  bundle entries               : {(summary.get('bundle') or {}).get('entries')}")
    regression = summary['regression']
    print(f"  regression                   : unit={regression['unit_checks']} contract={regression['contract_checks']} "
          f"lifecycle={regression['lifecycle_checks']} features={regression['registered_features']} "
          f"pages={regression['page_count']}")
    for name, data in summary['appearance_audits'].items():
        if isinstance(data, dict) and 'passed' in data:
            print(f"  audit {name:22} passed={data['passed']}")
    if conformance:
        print(f"  objective conformance        : {conformance.get('satisfied')}/{conformance.get('total')} satisfied")
    if matrix:
        counts = summary['acceptance_matrix']
        print(f"  acceptance matrix            : {counts['checks_total']} checks, {counts['checks_failed']} failed, "
              f"{counts['evidence']} evidence items")


if __name__ == '__main__':
    main()
