param(
    [Parameter(Mandatory=$true)][string]$DotnetPath,
    [Parameter(Mandatory=$true)][string]$GameManagedDirectory,
    [Parameter(Mandatory=$true)][string]$DnlibPath,
    [string]$ParentRuntimePath=''
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$inputs=Join-Path $root 'verification\acceptance-coverage-20261029\movement-inputs'
$stage=Join-Path $PSScriptRoot 'staging\held-motion'
$runtime=Join-Path $root 'source\HowToFishTrainer\HowToFishTrainer.Runtime.dll'
$parent=Join-Path $stage 'r17c-runtime.dll'
$expected='8dfbd831c6434cbbfa1c338922e184875f0c48ec70d4e3a0d246a20c2dfa7327'
$DotnetPath=(Resolve-Path -LiteralPath $DotnetPath).Path
$GameManagedDirectory=(Resolve-Path -LiteralPath $GameManagedDirectory).Path
$DnlibPath=(Resolve-Path -LiteralPath $DnlibPath).Path
$sdkRoot=Split-Path -Parent $DotnetPath
$sdk=Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'sdk') -Directory | Where-Object {$_.Name -match '^8\.\d+\.\d+$'} | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$refs=Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'packs\Microsoft.NETCore.App.Ref') -Directory | Where-Object {$_.Name -match '^8\.\d+\.\d+$'} | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
if(!$sdk -or !$refs){throw 'A .NET 8 SDK with its reference pack is required'}
$csc=Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
if(!(Test-Path -LiteralPath $parent)){
    $parentSource=if($ParentRuntimePath){(Resolve-Path -LiteralPath $ParentRuntimePath).Path}else{$runtime}
    if((Get-FileHash -LiteralPath $parentSource -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'The first build requires the unchanged r17c Runtime; provide it with -ParentRuntimePath'}
    Copy-Item -LiteralPath $parentSource -Destination $parent
}
if((Get-FileHash -LiteralPath $parent -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'The retained r17c parent hash changed'}

function Compile([string]$name,[string[]]$sources,[string[]]$references,[string]$main=''){
    $options=@('/nologo','/langversion:12','/optimize+','/deterministic+','/nostdlib+')
    $options+=if($main){'/target:exe'; '/main:'+$main}else{'/target:library'}
    $options+='/out:"'+(Join-Path $stage ($name+'.dll'))+'"'
    $options+=$references | ForEach-Object {'/reference:"'+$_+'"'}
    $options+=$sources | ForEach-Object {'"'+$_+'"'}
    $rsp=Join-Path $stage ($name+'.rsp')
    $options | Set-Content -LiteralPath $rsp -Encoding UTF8
    & $DotnetPath $csc ('@'+$rsp)
    if($LASTEXITCODE -ne 0){throw "Compilation failed: $name"}
    if($main){'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}' | Set-Content -LiteralPath (Join-Path $stage ($name+'.runtimeconfig.json')) -Encoding UTF8}
}
$gameReferences=@('mscorlib.dll','System.dll','System.Core.dll','System.Numerics.dll','System.Numerics.Vectors.dll','netstandard.dll','Assembly-CSharp.dll','FishNet.Runtime.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','Unity.InputSystem.dll','Newtonsoft.Json.dll') | ForEach-Object {Join-Path $GameManagedDirectory $_}
$gameReferences+=Join-Path $root 'source\HowToFishTrainer\0Harmony.dll'
$gameReferences+=$parent
$owner=Join-Path $PSScriptRoot 'MovementSafetyRuntime.cs'
Compile 'HeldMotionRuntime' @($owner) $gameReferences
$netReferences=Get-ChildItem -LiteralPath (Join-Path $refs.FullName 'ref\net8.0') -Filter '*.dll' -File | Select-Object -ExpandProperty FullName
Compile 'HeldMotionFixture' @($owner,(Join-Path $inputs 'MovementSafetyStubs.cs'),(Join-Path $inputs 'SafetyFixture.cs')) $netReferences 'SafetyFixture'
& $DotnetPath (Join-Path $stage 'HeldMotionFixture.dll') | Tee-Object -FilePath (Join-Path $stage 'fixture.log')
if($LASTEXITCODE -ne 0){throw 'Held-motion behavior checks failed'}
Copy-Item -LiteralPath $DnlibPath -Destination (Join-Path $stage 'dnlib.dll') -Force
Compile 'PatchMovementSafety' @((Join-Path $PSScriptRoot 'PatchSpin.cs'),(Join-Path $PSScriptRoot 'PatchMovementSafety.cs')) ($netReferences+@($DnlibPath)) 'PatchMovementSafety'
$candidate=Join-Path $stage 'candidate-runtime.dll'
& $DotnetPath (Join-Path $stage 'PatchMovementSafety.dll') $parent (Join-Path $stage 'HeldMotionRuntime.dll') $candidate (Join-Path $GameManagedDirectory 'Assembly-CSharp.dll') | Tee-Object -FilePath (Join-Path $stage 'patch-audit.log')
if($LASTEXITCODE -ne 0){throw 'Runtime preservation audit failed'}
$auditPath=Join-Path $stage 'runtime-audit.json'
$audit=Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
$hashes=@{}
foreach($path in @($owner,(Join-Path $PSScriptRoot 'PatchMovementSafety.cs'),(Join-Path $PSScriptRoot 'PatchSpin.cs'),$PSCommandPath)){
    $hashes[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}
$audit | Add-Member -NotePropertyName sourceHashes -NotePropertyValue $hashes
$audit | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $auditPath -Encoding UTF8
Copy-Item -LiteralPath $candidate -Destination $runtime -Force
if((Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash.ToLowerInvariant() -ne $audit.runtimeSha256){throw 'Runtime copy hash mismatch'}
Write-Output ('Verified Runtime installed in this worktree: '+$runtime)
