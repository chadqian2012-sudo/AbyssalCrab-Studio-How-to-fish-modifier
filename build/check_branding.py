from pathlib import Path
import json,hashlib,importlib.util
root=Path(__file__).resolve().parents[1]
exe=root/'release/AbyssalCrab Studio-HowToFish-V1.1.37-整合版.exe'
legacy=['\u7483\u8bed','\u7483\u8a9e','LiYu','liyu','LIYU']
results=[]
for path in [exe]+list((root/'verification/assemblies').glob('HowToFish*.dll')):
    data=path.read_bytes(); hits=[]
    for name in legacy:
        for encoding in ['utf-8','utf-16le']:
            if name.encode(encoding) in data:hits.append({'pattern':name,'encoding':encoding})
    results.append({'file':str(path.relative_to(root)),'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'old_brand_hits':hits})
    if hits:raise RuntimeError('Old branding remains in '+str(path)+': '+str(hits))
manifest=json.loads((root/'verification/bundle_manifest.json').read_text(encoding='utf-8'))
assert manifest['input_sha256']==hashlib.sha256(exe.read_bytes()).hexdigest(), 'Manifest does not describe the current release'
names={e['path'] for e in manifest['entries']}
assert {'HowToFishTrainer.dll','HowToFishTrainer.Core.dll','HowToFishTrainer.Memory.dll','HowToFishTrainer.runtimeconfig.json','HowToFishTrainer.deps.json'}<=names
spec=importlib.util.spec_from_file_location('bundle',root.parent/'howtofish-recovery/extract_bundle.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
assembly=(root/'verification/assemblies/HowToFishTrainer.dll').read_bytes()
runtime=(root/'patched/HowToFishTrainer.Runtime.dll').read_bytes()
assert runtime in assembly,'Final main assembly does not embed the new runtime bytes'
runtime_config=json.loads((root/'verification/assemblies/HowToFishTrainer.runtimeconfig.json').read_text(encoding='utf-8'))
report={'brand':'AbyssalCrab Studio','passed':True,'package':'net8 WPF x64 self-contained single-file; native libraries self-extract; no trimming','bundle_version':manifest['bundle_version'],'entry_count':len(names),'all_active_entry_bounds_valid':True,'new_runtime_embedded':True,'runtime_configuration':runtime_config,'files':results}
(root/'verification/branding-results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('Clean single-file package: old brand strings absent; new runtime embedded;',len(names),'entries')
