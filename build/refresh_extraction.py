from pathlib import Path
import json,hashlib,struct,zlib,importlib.util,sys
root=Path(__file__).resolve().parents[1]
exe=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else root/'release/AbyssalCrab Studio-HowToFish-V1.1.37-整合版.exe'
extractor=root.parent/'howtofish-recovery/extract_bundle.py'
spec=importlib.util.spec_from_file_location('bundle_extract',extractor);bundle=importlib.util.module_from_spec(spec);spec.loader.exec_module(bundle)
data=exe.read_bytes();marker=data.find(bundle.SIGNATURE);assert marker>=8
header=struct.unpack_from('<q',data,marker-8)[0];reader=bundle.Reader(data,header)
major,minor,count=reader.read('<IIi');assert major==6
bundle_id=reader.string();extra=reader.read('<qqqqQ');out=Path(sys.argv[2]).resolve() if len(sys.argv)>2 else root/'verification/assemblies';out.mkdir(parents=True,exist_ok=True)
entries=[];seen=set()
for _ in range(count):
    offset,size,compressed=reader.read('<qqq');kind=reader.read('<B');name=reader.string()
    destination=(out/name).resolve();assert destination.is_relative_to(out.resolve()) and name not in seen;seen.add(name)
    assert offset>=0 and offset+(compressed or size)<=header
    payload=data[offset:offset+(compressed or size)]
    if compressed:payload=zlib.decompress(payload,-15)
    assert len(payload)==size
    destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(payload)
    entries.append(dict(path=name,type=bundle.TYPES[kind],offset=offset,size=size,compressed_size=compressed,sha256=hashlib.sha256(payload).hexdigest()))
manifest={'input':str(exe),'input_sha256':hashlib.sha256(data).hexdigest(),'bundle_version':f'{major}.{minor}','bundle_id':bundle_id,'header_offset':header,'header_extra':extra,'file_count':count,'entries':entries}
(out.parent/'bundle_manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print('Validated and extracted',count,'active entries;',manifest['input_sha256'])
