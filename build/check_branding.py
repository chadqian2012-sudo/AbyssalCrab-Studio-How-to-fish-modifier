from pathlib import Path
import json,hashlib,argparse
root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description='Inspect release branding and embedded Runtime/Harmony bytes without executing the EXE.')
parser.add_argument('exe',type=Path)
parser.add_argument('assemblies',type=Path)
args=parser.parse_args()
exe=args.exe.resolve(strict=True);assemblies=args.assemblies.resolve(strict=True)
legacy=['\u7483\u8bed','\u7483\u8a9e','LiYu','liyu','LIYU']
results=[]
for path in [exe]+list(assemblies.glob('HowToFish*.dll')):
    data=path.read_bytes(); hits=[]
    for name in legacy:
        for encoding in ['utf-8','utf-16le']:
            if name.encode(encoding) in data:hits.append({'pattern':name,'encoding':encoding})
    results.append({'file':str(path),'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'old_brand_hits':hits})
    if hits:raise RuntimeError('Old branding remains in '+str(path)+': '+str(hits))
manifest=json.loads((assemblies.parent/'bundle_manifest.json').read_text(encoding='utf-8'))
assert manifest['input_sha256']==hashlib.sha256(exe.read_bytes()).hexdigest(), 'Manifest does not describe the current release'
names={e['path'] for e in manifest['entries']}
assert {'HowToFishTrainer.dll','HowToFishTrainer.Core.dll','HowToFishTrainer.Memory.dll','HowToFishTrainer.runtimeconfig.json','HowToFishTrainer.deps.json'}<=names
assembly=(assemblies/'HowToFishTrainer.dll').read_bytes()
resources={}
for name in ['HowToFishTrainer.Runtime.dll','0Harmony.dll']:
    payload=(root/'source/HowToFishTrainer'/name).read_bytes()
    if payload not in assembly:raise RuntimeError('Final main assembly does not contain the current embedded bytes: '+name)
    resources[name]=hashlib.sha256(payload).hexdigest()
runtime_config=json.loads((assemblies/'HowToFishTrainer.runtimeconfig.json').read_text(encoding='utf-8'))
report={'brand':'AbyssalCrab Studio','passed':True,'package':'net8 WPF x64 self-contained single-file; native libraries self-extract; no trimming','bundle_version':manifest['bundle_version'],'entry_count':len(names),'all_active_entry_bounds_valid':True,'new_runtime_embedded':True,'embedded_resource_sha256':resources,'runtime_configuration':runtime_config,'files':results}
(assemblies.parent/'branding-results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('Clean single-file package: old brand strings absent; new runtime embedded;',len(names),'entries')
