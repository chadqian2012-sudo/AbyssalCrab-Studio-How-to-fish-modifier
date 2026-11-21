param(
    [Parameter(Mandatory=$true)][string]$DotnetPath,
    [Parameter(Mandatory=$true)][string]$GameManagedDirectory,
    [Parameter(Mandatory=$true)][string]$DnlibPath,
    [string]$ParentRuntimePath='',
    [string]$OutputDirectory='',
    [switch]$WorldDrops,
    [switch]$ProjectileR29,
    [string]$CargoPath='',
    [string]$ParentCorePath='',
    [switch]$RustKernels
)
$ErrorActionPreference='Stop'
if($ProjectileR29){throw 'Build the historical r29 from its merged source. Use -RustKernels to patch r29 with the current source.'}
if($RustKernels -and $WorldDrops){throw 'Select either -RustKernels or -WorldDrops.'}
$root=Split-Path -Parent $PSScriptRoot
$stage=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}elseif($RustKernels){Join-Path $PSScriptRoot 'staging\rust-kernels'}elseif($WorldDrops){Join-Path $PSScriptRoot 'staging\world-drops'}else{Join-Path $PSScriptRoot 'staging\combat-challenge'}
$runtime=Join-Path $root 'source\HowToFishTrainer\HowToFishTrainer.Runtime.dll'
$parentName=if($RustKernels){'r29-runtime.dll'}elseif($WorldDrops){'r27-runtime.dll'}else{'r23-runtime.dll'}
$parent=Join-Path $stage $parentName
$expected=if($RustKernels){'6c99e369c784d76ee76b045ec37590ba52586cae478f2d0f7164d80596fc2efe'}elseif($WorldDrops){'de14b34c34e894d8e1b992d549918c643a30d1c8cfb4ed3b0ab4b24f8704787a'}else{'36d5f71f02f9dd21ffcd37aaf7f0266732c6783c84b86802d6186ffc37db56ee'}
$DotnetPath=(Resolve-Path -LiteralPath $DotnetPath).Path
$GameManagedDirectory=(Resolve-Path -LiteralPath $GameManagedDirectory).Path
$DnlibPath=(Resolve-Path -LiteralPath $DnlibPath).Path
$sdkRoot=Split-Path -Parent $DotnetPath
$sdk=Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'sdk') -Directory | Where-Object {$_.Name -match '^8\.\d+\.\d+$'} | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$refs=Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'packs\Microsoft.NETCore.App.Ref') -Directory | Where-Object {$_.Name -match '^8\.\d+\.\d+$'} | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
if(!$sdk -or !$refs){throw 'A .NET 8 SDK and reference pack are required'}
$csc=Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
if(!(Test-Path -LiteralPath $parent)){
    $parentSource=if($ParentRuntimePath){(Resolve-Path -LiteralPath $ParentRuntimePath).Path}else{$runtime}
    if((Get-FileHash -LiteralPath $parentSource -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw "Provide the original $parentName with -ParentRuntimePath"}
    Copy-Item -LiteralPath $parentSource -Destination $parent
}
if((Get-FileHash -LiteralPath $parent -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw "The retained $parentName hash changed"}
function Compile([string]$name,[string[]]$sources,[string[]]$references,[string]$main=''){
    $options=@('/nologo','/nowarn:0436','/langversion:12','/optimize+','/deterministic+','/nostdlib+')
    $options+=if($main){'/target:exe';'/main:'+$main}else{'/target:library'}
    $options+='/out:"'+(Join-Path $stage ($name+'.dll'))+'"'
    $options+=$references | ForEach-Object {'/reference:"'+$_+'"'}
    $options+=$sources | ForEach-Object {'"'+$_+'"'}
    $rsp=Join-Path $stage ($name+'.rsp');$options|Set-Content -LiteralPath $rsp -Encoding utf8
    & $DotnetPath $csc ('@'+$rsp)
    if($LASTEXITCODE -ne 0){throw "Compilation failed: $name"}
    if($main){'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}'|Set-Content -LiteralPath (Join-Path $stage ($name+'.runtimeconfig.json')) -Encoding utf8}
}
$names=@('AimAssistOptimizedRuntime.cs','GameplayTargeting.cs','GameplayRuntime.cs','CombatBridgeRuntime.cs','CombatFeedbackRuntime.cs','CombatFeedbackState.cs','MagicRangeRenderer.cs')
$gameReferences=@('mscorlib.dll','System.dll','System.Core.dll','System.Numerics.dll','System.Numerics.Vectors.dll','netstandard.dll','Assembly-CSharp.dll','FishNet.Runtime.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.AudioModule.dll','UnityEngine.TextRenderingModule.dll','Unity.InputSystem.dll','Newtonsoft.Json.dll')|ForEach-Object {Join-Path $GameManagedDirectory $_}
$gameReferences+=@($parent,(Join-Path $root 'source\HowToFishTrainer\0Harmony.dll'))
$netReferences=Get-ChildItem -LiteralPath (Join-Path $refs.FullName 'ref\net8.0') -Filter '*.dll' -File | Select-Object -ExpandProperty FullName
if([IO.Path]::GetFullPath($DnlibPath) -ne (Join-Path $stage 'dnlib.dll')){Copy-Item -LiteralPath $DnlibPath -Destination (Join-Path $stage 'dnlib.dll') -Force}
Compile 'PatchTargetOptions' @((Join-Path $PSScriptRoot 'PatchSpin.cs'),(Join-Path $PSScriptRoot 'PatchTargetOptions.cs')) ($netReferences+@($DnlibPath)) 'PatchTargetOptions'
if($RustKernels){
    Compile 'RustKernels' @(($names+@('GameplayHooks.cs','OverlayMeshKernel.cs','OverlayBatchRenderer.cs'))|ForEach-Object {Join-Path $PSScriptRoot $_}) $gameReferences
    $candidate=Join-Path $stage 'candidate-runtime.dll'
    & $DotnetPath (Join-Path $stage 'PatchTargetOptions.dll') --rust-kernels $parent (Join-Path $stage 'RustKernels.dll') $candidate (Join-Path $GameManagedDirectory 'Assembly-CSharp.dll')
    if($LASTEXITCODE -ne 0){throw 'Rust kernels Runtime audit failed'}
    if(!$CargoPath){$CargoPath=(Get-Command cargo -ErrorAction Stop).Source}
    $nativeTarget=Join-Path $stage 'native'
    & $CargoPath rustc --release --offline --locked --target x86_64-pc-windows-msvc --target-dir $nativeTarget --manifest-path (Join-Path $root 'native\overlay\Cargo.toml') -- -C target-feature=+crt-static
    if($LASTEXITCODE -ne 0){throw 'Rust overlay build failed'}
    $nativeCandidate=Join-Path $nativeTarget 'x86_64-pc-windows-msvc\release\abyssalcrab_overlay.dll'
    $auditPath=Join-Path $stage 'runtime-audit.json'
    $audit=Get-Content -Raw -LiteralPath $auditPath | ConvertFrom-Json
    $nativeHash=(Get-FileHash -LiteralPath $nativeCandidate -Algorithm SHA256).Hash.ToLowerInvariant()
    $audit|Add-Member -NotePropertyName nativeSha256 -NotePropertyValue $nativeHash
    $audit|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $auditPath -Encoding utf8
    $files=[ordered]@{runtime=$runtime;native=(Join-Path $root 'source\HowToFishTrainer\abyssalcrab_overlay.dll')}
    $candidates=@{runtime=$candidate;native=$nativeCandidate}
    $hashes=@{runtime=$audit.runtime;native=$nativeHash}
}elseif($WorldDrops){
    $core=Join-Path $root 'patched\HowToFishTrainer.Core.dll'
    $parentCore=Join-Path $stage 'r27-core.dll'
    if(!(Test-Path -LiteralPath $parentCore)){
        $coreSource=if($ParentCorePath){(Resolve-Path -LiteralPath $ParentCorePath).Path}else{$core}
        if((Get-FileHash -LiteralPath $coreSource).Hash -ne 'a2cac4e5393b41c98c843f37acd8662c29b8b5475662c28e9115bc8874199bfc'){throw 'Provide the original r27 Core with -ParentCorePath'}
        Copy-Item -LiteralPath $coreSource -Destination $parentCore
    }
    # The shipped patch retains property accessor methods but lacks C# property metadata.
    Compile 'world-drops-runtime' @($names|ForEach-Object {Join-Path $PSScriptRoot $_}) $gameReferences
    Compile 'world-drops-core' @((Join-Path $PSScriptRoot 'GameplayRegistry.cs'),(Join-Path $PSScriptRoot 'GameplayPolicy.cs')) ($netReferences+@($parentCore))
    & $DotnetPath (Join-Path $stage 'PatchTargetOptions.dll') --world-drops $stage (Join-Path $GameManagedDirectory 'Assembly-CSharp.dll') (Join-Path $PSScriptRoot 'PatchTargetOptions.cs') (Join-Path $PSScriptRoot 'PatchSpin.cs')
    if($LASTEXITCODE -ne 0){throw 'Cleanup patch or original-method verification failed'}
    $audit=Get-Content -Raw -LiteralPath (Join-Path $stage 'runtime-audit.json') | ConvertFrom-Json
    $files=[ordered]@{runtime=$runtime;core=$core}
    $candidates=@{runtime=(Join-Path $stage 'candidate-runtime.dll');core=(Join-Path $stage 'candidate-core.dll')}
    $hashes=@{runtime=$audit.runtime.sha256;core=$audit.core.sha256}
}
if($RustKernels -or $WorldDrops){
    $suffix=[guid]::NewGuid().ToString('N')
    $installed=@()
    try{
        foreach($kind in $files.Keys){
            $path=$files[$kind]+'.'+$suffix+'.new'
            Copy-Item -LiteralPath $candidates[$kind] -Destination $path
            if((Get-FileHash -LiteralPath $path).Hash -ne $hashes[$kind]){throw "Staged $kind changed after verification"}
        }
        foreach($file in $files.Values){
            if([IO.File]::Exists($file)){
                [IO.File]::Replace($file+'.'+$suffix+'.new',$file,$file+'.'+$suffix+'.previous')
            }else{
                [IO.File]::Move($file+'.'+$suffix+'.new',$file)
            }
            $installed+=$file
        }
    }catch{
        foreach($file in $installed){
            $backup=$file+'.'+$suffix+'.previous'
            if([IO.File]::Exists($backup)){
                [IO.File]::Replace($backup,$file,[System.Management.Automation.Language.NullString]::Value)
            }else{
                [IO.File]::Delete($file)
            }
        }
        throw
    }finally{
        foreach($file in $files.Values){[IO.File]::Delete($file+'.'+$suffix+'.new')}
    }
    foreach($file in $installed){[IO.File]::Delete($file+'.'+$suffix+'.previous')}
    Write-Output "Verified $($files.Keys -join ' and ') installed in $root"
    return
}
Compile 'EntityOverlayRuntime' @($names|ForEach-Object {Join-Path $PSScriptRoot $_}) $gameReferences
$candidate=Join-Path $stage 'candidate-runtime.dll'
& $DotnetPath (Join-Path $stage 'PatchTargetOptions.dll') $parent (Join-Path $stage 'EntityOverlayRuntime.dll') $candidate (Join-Path $GameManagedDirectory 'Assembly-CSharp.dll') (Join-Path $PSScriptRoot 'PatchTargetOptions.cs')|Tee-Object -FilePath (Join-Path $stage 'patch-audit.log')
if($LASTEXITCODE -ne 0){throw 'Runtime method-preservation audit failed'}
$fixture=Join-Path $root 'verification\EntityOverlayVerification.cs'
Compile 'EntityOverlayVerification' @($fixture) ($netReferences+@($DnlibPath)) 'EntityOverlayVerification'
& $DotnetPath (Join-Path $stage 'EntityOverlayVerification.dll') $parent $candidate (Join-Path $PSScriptRoot 'MagicRangeRenderer.cs') (Join-Path $stage 'verification') $GameManagedDirectory
if($LASTEXITCODE -ne 0){throw 'Entity-overlay geometry and runtime verification failed'}
$auditPath=Join-Path $stage 'runtime-audit.json';$audit=Get-Content -LiteralPath $auditPath -Raw|ConvertFrom-Json
$hashes=[ordered]@{}
foreach($name in $names+@('PatchSpin.cs','PatchTargetOptions.cs','Build-TargetOptions.ps1')){$hashes['build/'+$name]=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $name) -Algorithm SHA256).Hash.ToLowerInvariant()}
$hashes['verification/EntityOverlayVerification.cs']=(Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
$audit|Add-Member -NotePropertyName sourceHashes -NotePropertyValue $hashes
$audit|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $auditPath -Encoding utf8
Copy-Item -LiteralPath $candidate -Destination $runtime -Force
if((Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash.ToLowerInvariant() -ne $audit.runtimeSha256){throw 'Embedded Runtime copy hash mismatch'}
Write-Output ('Verified entity-overlay Runtime installed: '+$runtime)
