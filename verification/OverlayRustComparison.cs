using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
static class OverlayRustComparison {
 [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate int NativeBuild(IntPtr a,int n,IntPtr b,int capacity);
 [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate int Abi();
 static readonly List<string> Checks=new();
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Checks.Add(label);}
 static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
 static float[] Input(int count,int seed) {
  var random=new Random(seed);var input=new float[count*13];
  for(int i=0;i<count;i++){int s=i*13;for(int c=0;c<4;c++)input[s+c]=(float)(random.NextDouble()*4000-1000);input[s+4]=100;input[s+5]=50;input[s+6]=2020;input[s+7]=1130;input[s+8]=1;input[s+11]=i%5==0?.55f:1;input[s+12]=1;}
  return input;
 }
 static int RunNative(NativeBuild fn,float[] input,int count,float[] output) {
  var a=GCHandle.Alloc(input,GCHandleType.Pinned);var b=GCHandle.Alloc(output,GCHandleType.Pinned);
  try{return fn(a.AddrOfPinnedObject(),count,b.AddrOfPinnedObject(),output.Length/12);}finally{b.Free();a.Free();}
 }
 static void Main(string[] args) {
  using var module=ModuleDefMD.Load(args[0]);using var game=ModuleDefMD.Load(args[3]);
  var runtime=module.Find("AbyssalCrab.Runtime.GameplayRuntime",false);var manager=game.Find("ProjectileManager",false);
  foreach(var mapping in new[]{("AddProjectile","BeforeSingle"),("AddProjectiles","BeforeMultiple"),("Hit","BeforeProjectileHit"),("Hit","AfterProjectileHit"),("UpdateProjectileScan","BeforeProjectileScan"),("UpdateProjectileScan","AfterProjectileScan"),("HitScan","BeforeHitscan"),("HitScan","AfterHitscan")}) {
   var original=manager.Methods.Single(m=>m.Name==mapping.Item1);var hook=runtime.Methods.Single(m=>m.Name==mapping.Item2);
   foreach(var p in hook.Parameters.Where(p=>!p.Name.StartsWith("__")))Check(original.Parameters.Any(n=>n.Name==p.Name&&n.Type.FullName==p.Type.FullName),mapping.Item2+" binds native argument "+p.Name);
   foreach(var p in hook.Parameters.Where(p=>p.Name=="__result"))Check(original.ReturnType.FullName==p.Type.FullName,mapping.Item2+" has correct native return type");
  }
  foreach(var mapping in new[]{("Creature","_hp"),("PlayerVitals","_syncedHealth")})Check(game.Find(mapping.Item1,false).Fields.Single(f=>f.Name==mapping.Item2).FieldType.FullName=="FishNet.Object.Synchronizing.SyncVar`1<System.Int32>",mapping.Item1+" server health field matches compiled access");
  var bridge=module.Find("AbyssalCrab.Runtime.OverlayBatchRenderer",false);
  foreach(var name in new[]{"LoadLibraryExW","GetProcAddress","FreeLibrary"})Check(bridge.Methods.Single(m=>m.Name==name).ImplMap?.Module.Name=="kernel32.dll","Patched native import retained: "+name);
  foreach(var name in new[]{"OverlayBuildDelegate","OverlayAbiDelegate"})Check(module.Find("AbyssalCrab.Runtime."+name,false).BaseType.FullName=="System.MulticastDelegate","Patched delegate base retained: "+name);
  Check(!module.GetAssemblyRefs().Any(r=>r.Name=="ProjectileR29"),"Final runtime has no helper assembly dependency");
  using(var app=ModuleDefMD.Load(args[4])){
   var resource=(EmbeddedResource)app.Resources.Single(r=>r.Name=="abyssalcrab_overlay.dll");
   Check(Convert.ToHexString(SHA256.HashData(resource.CreateReader().ToArray())).ToLowerInvariant()==Hash(args[1]),"App embeds the compared Rust DLL");
  }
  var alc=new AssemblyLoadContext("r29-pure-kernel",isCollectible:true);
  alc.Resolving+=(context,name)=>{string file=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[3])),name.Name+".dll");return File.Exists(file)?context.LoadFromAssemblyPath(file):null;};
  var asm=alc.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
  var managed=(Func<float[],int,float[],int>)asm.GetType("AbyssalCrab.Runtime.OverlayMeshKernel",true).GetMethod("Build").CreateDelegate(typeof(Func<float[],int,float[],int>));
  IntPtr library=NativeLibrary.Load(Path.GetFullPath(args[1]));
  try {
   var native=Marshal.GetDelegateForFunctionPointer<NativeBuild>(NativeLibrary.GetExport(library,"overlay_build"));
   var imported=asm.GetType("AbyssalCrab.Runtime.OverlayBatchRenderer",true);
   imported.GetMethod("Configure").Invoke(null,new object[]{2,Path.GetFullPath(args[1])});
   var importedCall=(Func<float[],int,float[],int>)imported.GetMethod("NativeBuild").CreateDelegate(typeof(Func<float[],int,float[],int>));
   var probe=Input(12,5);var probeOut=new float[144];
   Check(importedCall(probe,12,probeOut)==managed(probe,12,new float[144]),"Final patched PInvoke loader and delegate actually execute Rust");
   imported.GetMethod("Configure").Invoke(null,new object[]{0,null});
   Check((int)imported.GetField("Backend").GetValue(null)==0,"Experimental renderer switches back to original C#");
   Check(Marshal.GetDelegateForFunctionPointer<Abi>(NativeLibrary.GetExport(library,"overlay_abi"))()==1,"Native ABI version matches");
   Check(native(IntPtr.Zero,1,IntPtr.Zero,1)==-1&&native(IntPtr.Zero,-1,IntPtr.Zero,0)==-1,"Native boundary rejects null and negative input");
   var special=Input(128,17);for(int c=0;c<8;c++){special[(c*3)*13+c]=float.NaN;special[(c*3+1)*13+c]=float.PositiveInfinity;special[(c*3+2)*13+c]=float.NegativeInfinity;}
   for(int i=24;i<30;i++){special[i*13]=0;special[i*13+1]=0;special[i*13+2]=0;special[i*13+3]=0;}
   special[30*13]=float.MaxValue;special[30*13+2]=-float.MaxValue;special[31*13+12]=0;special[32*13+12]=-1;special[33*13+12]=float.NaN;
   foreach(var input in new[]{special,Input(40000,33),Input(0,1)}) {
    int n=input.Length/13;var left=new float[Math.Max(12,n*12)];var right=new float[left.Length];int c=managed(input,n,left),r=RunNative(native,input,n,right);
    Check(c==r,"C# and Rust accept the same segments: "+n);
    Check(left.Take(c*12).Zip(right.Take(r*12),(a,b)=>Math.Abs(a-b)<=.001f).All(v=>v),"C# and Rust vertices agree within 0.001 pixel: "+n);
   }
   var benchmarks=new List<object>();int checksum=0;
   foreach(int entities in new[]{24,64,128}) {
    int n=entities*37;var input=Input(n,entities);var left=new float[n*12];var right=new float[n*12];
    for(int i=0;i<600;i++){checksum+=managed(input,n,left);checksum+=RunNative(native,input,n,right);}
    var cs=new List<double>();var rs=new List<double>();const int repeats=120;
    for(int round=0;round<24;round++)for(int pass=0;pass<2;pass++){
     bool rust=(round+pass)%2==0;long start=Stopwatch.GetTimestamp();
     for(int i=0;i<repeats;i++)checksum+=rust?RunNative(native,input,n,right):managed(input,n,left);
     (rust?rs:cs).Add((Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency/repeats);
    }
    cs.Sort();rs.Sort();benchmarks.Add(new{entities,segments=n,managedMedianMs=cs[cs.Count/2],rustMedianMs=rs[rs.Count/2],managedP95Ms=cs[(int)(cs.Count*.95)],rustP95Ms=rs[(int)(rs.Count*.95)],rustOverManaged=rs[rs.Count/2]/cs[cs.Count/2]});
   }
   var report=new{passed=true,checks=Checks.Count,tests=Checks,benchmarks,checksum,runtimeSha256=Hash(args[0]),nativeSha256=Hash(args[1]),scope=".NET 8 Release, actual patched C# kernel versus Rust DLL, includes pin and interop per batch; excludes Unity model reads, projection, GL, game Mono and FPS"};
   File.WriteAllText(args[2],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));
  }finally{NativeLibrary.Free(library);alc.Unload();}
 }
}
