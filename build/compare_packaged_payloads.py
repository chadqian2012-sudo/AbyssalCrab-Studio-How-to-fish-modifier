from pathlib import Path
import argparse,json,hashlib,subprocess,sys
root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description='Compare the active payloads in explicit default and compressed single-file releases.')
parser.add_argument('default_exe',type=Path)
parser.add_argument('compressed_exe',type=Path)
parser.add_argument('output_directory',type=Path)
args=parser.parse_args()
inputs={'single-file':args.default_exe.resolve(strict=True),'compressed-single-file':args.compressed_exe.resolve(strict=True)}
if len(set(inputs.values()))!=2:raise ValueError('Provide two distinct release files')
output=args.output_directory.resolve()
packages={}
for label,exe in inputs.items():
    directory=output/label/'assemblies'
    subprocess.run([sys.executable,str(root/'build/refresh_extraction.py'),str(exe),str(directory)],check=True)
    manifest=json.loads((directory.parent/'bundle_manifest.json').read_text(encoding='utf-8'))
    if manifest['input_sha256']!=hashlib.sha256(exe.read_bytes()).hexdigest():raise ValueError('Release changed during extraction: '+str(exe))
    entries={entry['path']:entry for entry in manifest['entries']}
    compressed=any(entry['compressed_size']>0 for entry in entries.values())
    if compressed!=(label=='compressed-single-file'):raise ValueError('Unexpected compression mode: '+label)
    for name,entry in entries.items():
        if hashlib.sha256((directory/name).read_bytes()).hexdigest()!=entry['sha256']:raise ValueError('Extracted payload hash mismatch: '+name)
    packages[label]=(directory,manifest,entries)
names=set(packages['single-file'][2])
if names!=set(packages['compressed-single-file'][2]):raise ValueError('The packages contain different active entries')
if not {'HowToFishTrainer.dll','HowToFishTrainer.Core.dll','HowToFishTrainer.Memory.dll'}<=names:raise ValueError('Required application assemblies are missing')
rows=[]
for name in sorted(names):
    hashes={label:package[2][name]['sha256'] for label,package in packages.items()}
    types={package[2][name]['type'] for package in packages.values()}
    if len(set(hashes.values()))!=1 or len(types)!=1:raise ValueError('Payload differs: '+name+' '+str(hashes))
    rows.append({'path':name,'type':next(iter(types)),'sha256_by_package':hashes,'identical':True})
resources=[]
for name in ['HowToFishTrainer.Runtime.dll','0Harmony.dll']:
    payload=(root/'source/HowToFishTrainer'/name).read_bytes()
    for label,(directory,_,_) in packages.items():
        if payload not in (directory/'HowToFishTrainer.dll').read_bytes():raise ValueError('Current embedded bytes are missing: '+label+'/'+name)
    resources.append({'name':name,'sha256':hashlib.sha256(payload).hexdigest(),'matches_current_source_bytes':True})
report={'passed':True,'inputs':{label:{'path':str(inputs[label]),'sha256':package[1]['input_sha256']} for label,package in packages.items()},'rows':rows,'embedded_resources':resources,'executables_started':False}
(output/'package-payload-equivalence.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('Both packages contain',len(rows),'byte-identical active payloads and current Runtime/Harmony bytes')
