from pathlib import Path
import json,hashlib,struct,zlib,sys

# .NET 6+ bundle format; inspect bytes without loading the executable.
SIGNATURE=bytes.fromhex('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae')
TYPES={0:'Unknown',1:'Assembly',2:'NativeBinary',3:'DepsJson',4:'RuntimeConfigJson',5:'Symbols'}
class Reader:
    def __init__(self,data,pos): self.data,self.pos=data,pos
    def read(self,fmt):
        value=struct.unpack_from(fmt,self.data,self.pos);self.pos+=struct.calcsize(fmt)
        return value[0] if len(value)==1 else value
    def string(self):
        length=0
        for shift in range(0,35,7):
            byte=self.read('<B');length|=(byte&127)<<shift
            if byte<128:break
        else:raise ValueError('Invalid string length')
        if length>32768 or self.pos+length>len(self.data):raise ValueError('String out of bounds')
        value=self.data[self.pos:self.pos+length].decode('utf-8');self.pos+=length
        return value
root=Path(__file__).resolve().parents[1]
exe=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else root/'release/AbyssalCrab Studio-HowToFish-V1.1.37-整合版.exe'
data=exe.read_bytes();marker=data.find(SIGNATURE)
if marker<8:raise ValueError('No bundle signature')
header=struct.unpack_from('<q',data,marker-8)[0]
if not 0<header<len(data)-12:raise ValueError('Invalid header offset')
reader=Reader(data,header)
major,minor,count=reader.read('<IIi')
if major!=6 or not 0<count<100000:raise ValueError('Unsupported bundle header')
bundle_id=reader.string();extra=reader.read('<qqqqQ');out=Path(sys.argv[2]).resolve() if len(sys.argv)>2 else root/'verification/assemblies';out.mkdir(parents=True,exist_ok=True)
entries=[];seen=set()
for _ in range(count):
    offset,size,compressed=reader.read('<qqq');kind=reader.read('<B');name=reader.string()
    destination=(out/name).resolve()
    if not name or ':' in name or not destination.is_relative_to(out.resolve()) or str(destination).casefold() in seen:raise ValueError('Unsafe bundle path')
    seen.add(str(destination).casefold())
    if min(offset,size,compressed)<0 or offset+(compressed or size)>header or size>1024**3:raise ValueError('Invalid entry bounds')
    payload=data[offset:offset+(compressed or size)]
    if compressed:
        decoder=zlib.decompressobj(-15);payload=decoder.decompress(payload,size+1)
        if not decoder.eof or decoder.unused_data or decoder.unconsumed_tail:raise ValueError('Invalid compressed entry')
    if len(payload)!=size:raise ValueError('Entry size mismatch')
    destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(payload)
    entries.append(dict(path=name,type=TYPES.get(kind,str(kind)),offset=offset,size=size,compressed_size=compressed,sha256=hashlib.sha256(payload).hexdigest()))
manifest={'input':str(exe),'input_sha256':hashlib.sha256(data).hexdigest(),'bundle_version':f'{major}.{minor}','bundle_id':bundle_id,'header_offset':header,'header_extra':extra,'file_count':count,'entries':entries}
(out.parent/'bundle_manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print('Validated and extracted',count,'active entries;',manifest['input_sha256'])
