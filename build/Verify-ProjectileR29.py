"""Compile actual changed projectile methods against an offline fake engine, then compare final geometry IL and Rust."""
import argparse, pathlib, re, subprocess, json, os
p=argparse.ArgumentParser();p.add_argument('--dotnet',required=True);p.add_argument('--game',required=True);p.add_argument('--app',required=True)
p.add_argument('--output', type=pathlib.Path, help='Directory for test assemblies and evidence')
p.add_argument('--dnlib', type=pathlib.Path, help='Path to the build-time dnlib assembly')
a=p.parse_args()
root=pathlib.Path(__file__).resolve().parent.parent;out=(a.output or (root/'build/staging/projectile-r29/verification')).resolve();out.mkdir(parents=True,exist_ok=True)
sdkroot=pathlib.Path(a.dotnet).parent;sdk=sorted((sdkroot/'sdk').glob('8.*'),key=lambda x:tuple(map(int,x.name.split('.'))))[-1]
pack=sorted((sdkroot/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=lambda x:tuple(map(int,x.name.split('.'))))[-1]
source=(root/'build/GameplayRuntime.cs').read_text(encoding='utf-8-sig')
def method(name):
    start=re.search(r'        (?:private|public) static [^\n]+\b'+re.escape(name)+r'\(',source).start()
    opening=source.index('\n',start)
    if '=>' in source[start:opening]:return source[start:opening]+'\n'
    begin=source.index('{',opening);depth=1;i=begin+1
    while depth:
        if source[i]=='{':depth+=1
        elif source[i]=='}':depth-=1
        i+=1
    return source[start:i]+'\n'
fields=source[source.index('        private static TrainerBridgeHost _host;'):source.index('        private static object Call(')]
methods=''.join(method(n) for n in ['GroupReady','Ready','ShotReady','MarkGuidance','ClearProjectiles','Warn'])
methods+=source[source.index('        private static GameplayLaunch Launch('):source.index('        public static bool TryHandle(')]
classes=(root/'build/GameplayTargeting.cs').read_text(encoding='utf-8-sig');classes=classes[classes.index('    public sealed class GameplayLaunch'):classes.rfind('\n}')]
source=(root/'build/AimAssistOptimizedRuntime.cs').read_text(encoding='utf-8-sig')
velocity=method('Velocity')+method('Finite')
(out/'Candidate.cs').write_text('using System;using System.Collections.Generic;using UnityEngine;using HarmonyLib;using HowToFishTrainer.Runtime;\nnamespace AbyssalCrab.Runtime {public static class GameplayRuntime {\n'+fields+methods+'}\n'+classes+'public static class PredictiveAimRuntime {\n'+velocity+'}\n}\n',encoding='utf-8')
def compile_run(name,files,extra,args):
    dll=out/(name+'.dll');rsp=out/(name+'.rsp')
    lines=['/nologo','/target:exe','/optimize+','/langversion:12','/out:"'+str(dll)+'"']
    lines+=['/reference:"'+str(f)+'"' for f in list((pack/'ref/net8.0').glob('*.dll'))+extra]
    lines+=['"'+str(f)+'"' for f in files];rsp.write_text('\n'.join(lines),encoding='utf-8')
    subprocess.run([a.dotnet,str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
    (out/(name+'.runtimeconfig.json')).write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    env=os.environ.copy();env['DOTNET_TieredCompilation']='0'
    subprocess.run([a.dotnet,str(dll)]+list(map(str,args)),check=True,cwd=out,env=env)
compile_run('ProjectileBehavior',[out/'Candidate.cs',root/'verification/ProjectileR29Verification.cs'],[],[out/'projectile-behavior.json'])
dnlib=(a.dnlib or (root/'build/staging/combat-challenge/dnlib.dll')).resolve();(out/'dnlib.dll').write_bytes(dnlib.read_bytes())
compile_run('RustComparison',[root/'verification/OverlayRustComparison.cs'],[dnlib],[root/'source/HowToFishTrainer/HowToFishTrainer.Runtime.dll',root/'source/HowToFishTrainer/abyssalcrab_overlay.dll',out/'rust-comparison.json',pathlib.Path(a.game).resolve(),pathlib.Path(a.app).resolve()])
