from pathlib import Path
import json,hashlib,subprocess,sys
root=Path(__file__).resolve().parents[1]
original=root/'verification/assemblies';compressed=root/'verification/packages/compressed/assemblies'
folder=root/'build/packaging-variants/self-contained-folder'
light=root/'verification/packages/framework-dependent/assemblies'
for variant,directory in [('compressed-single-file',compressed),('framework-dependent-single-file',light)]:
    subprocess.run([sys.executable,str(root/'build/refresh_extraction.py'),str(root/'build/packaging-variants'/variant/'HowToFishTrainer.exe'),str(directory)],check=True,stdout=subprocess.DEVNULL)
rows=[]
for name in ['HowToFishTrainer.dll','HowToFishTrainer.Core.dll','HowToFishTrainer.Memory.dll']:
    hashes={label:hashlib.sha256((directory/name).read_bytes()).hexdigest() for label,directory in [('single-file',original),('compressed-single-file',compressed),('self-contained-folder',folder),('framework-dependent-single-file',light)]}
    assert len(set(hashes.values()))==1,(name,hashes)
    rows.append({'assembly':name,'sha256_by_package':hashes,'identical':True})
(root/'verification/package-payload-equivalence.json').write_text(json.dumps({'passed':True,'rows':rows},indent=2),encoding='utf-8')
print('All four packaging modes contain byte-identical main/Core/Memory payloads')
