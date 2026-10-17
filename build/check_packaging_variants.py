from pathlib import Path
import json,subprocess,time,hashlib,os
root=Path(__file__).resolve().parents[1];tools=root.parent/'howtofish-local-test/build-tools'
sdk=Path(json.loads((tools/'sdk-download.json').read_text(encoding='utf-8-sig'))['sdk_directory'])
env=os.environ.copy();env['NUGET_PACKAGES']=str(tools/'nuget-cache');env['DOTNET_CLI_HOME']=str(root/'build/cli-home');env['DOTNET_CLI_TELEMETRY_OPTOUT']='1'
project=root/'source/HowToFishTrainer/HowToFishTrainer.csproj'
cases=[('compressed-single-file',True,True,True),('self-contained-folder',True,False,False),('framework-dependent-single-file',False,True,False)]
results=[]
for name,self_contained,single,compressed in cases:
    out=root/'build/packaging-variants'/name;out.mkdir(parents=True,exist_ok=True)
    args=[str(sdk/'dotnet.exe'),'publish',str(project),'-c','Release','-r','win-x64','--self-contained',str(self_contained).lower(),'-o',str(out),'--ignore-failed-sources',f'-p:PublishSingleFile={str(single).lower()}',f'-p:EnableCompressionInSingleFile={str(compressed).lower()}','-p:IncludeNativeLibrariesForSelfExtract=true','-p:PublishTrimmed=false','-p:RunAnalyzers=false','-p:EnableNETAnalyzers=false','-p:NoWarn=CS8632','-p:PublishReadyToRun=false']
    start=time.monotonic()
    with (out/'publish.log').open('w',encoding='utf-8') as log:
        result=subprocess.run(args,env=env,stdout=log,stderr=subprocess.STDOUT)
    files=[p for p in out.rglob('*') if p.is_file() and p.suffix not in ['.pdb','.log']]
    exe=out/'HowToFishTrainer.exe'
    row={'variant':name,'publish_succeeded':result.returncode==0,'build_seconds':round(time.monotonic()-start,2),'required_file_count':len(files),'distribution_bytes':sum(p.stat().st_size for p in files),'exe_bytes':exe.stat().st_size if exe.exists() else None,'exe_sha256':hashlib.sha256(exe.read_bytes()).hexdigest() if exe.exists() else None,'runtime_required':None if self_contained else '.NET 8 Windows Desktop Runtime x64','output_directory':str(out)}
    results.append(row)
    (root/'verification/packaging-variants.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(row,ensure_ascii=False),flush=True)
    if result.returncode:raise RuntimeError('Publish failed: '+name)
