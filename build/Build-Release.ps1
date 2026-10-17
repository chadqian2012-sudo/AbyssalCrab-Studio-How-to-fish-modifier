param([ValidateSet('SingleFile','CompressedSingleFile','Folder','FrameworkDependent')][string]$PackageMode='SingleFile',[string]$OutputName='AbyssalCrab Studio-HowToFish-V1.1.37-整合版.exe')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$toolRoot=Join-Path (Split-Path -Parent $projectRoot) 'howtofish-local-test\build-tools'
$manifest=Get-Content -LiteralPath (Join-Path $toolRoot 'sdk-download.json') -Raw | ConvertFrom-Json
$sdk=Join-Path $manifest.sdk_directory 'dotnet.exe'
$savedPackages=$env:NUGET_PACKAGES
$savedCli=$env:DOTNET_CLI_HOME
$savedTelemetry=$env:DOTNET_CLI_TELEMETRY_OPTOUT
$savedFirstTime=$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
try {
    $env:NUGET_PACKAGES=Join-Path $toolRoot 'nuget-cache'
    $env:DOTNET_CLI_HOME=Join-Path $projectRoot 'build\cli-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
    $project=Join-Path $projectRoot 'source\HowToFishTrainer\HowToFishTrainer.csproj'
    $output=Join-Path $projectRoot ('build\publish-'+$PackageMode)
    $single=($PackageMode -ne 'Folder').ToString().ToLowerInvariant()
    $contained=($PackageMode -ne 'FrameworkDependent').ToString().ToLowerInvariant()
    $compressed=($PackageMode -eq 'CompressedSingleFile').ToString().ToLowerInvariant()
    & $sdk publish $project -c Release -r win-x64 --self-contained $contained -o $output --ignore-failed-sources "-p:PublishSingleFile=$single" '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:PublishTrimmed=false' "-p:EnableCompressionInSingleFile=$compressed" '-p:PublishReadyToRun=false' '-p:RunAnalyzers=false' '-p:EnableNETAnalyzers=false' '-p:NoWarn=CS8632'
    if($LASTEXITCODE -ne 0){throw 'Single-file publish failed'}
    if($PackageMode -eq 'SingleFile') {
        if([System.IO.Path]::GetFileName($OutputName) -ne $OutputName -or [System.IO.Path]::GetExtension($OutputName) -ne '.exe'){throw 'OutputName must be an EXE filename'}
        $exe=Join-Path $projectRoot ('release\'+$OutputName)
        Copy-Item -LiteralPath (Join-Path $output 'HowToFishTrainer.exe') -Destination $exe -Force
    } elseif($PackageMode -eq 'Folder') {
        Write-Output ('Directory package: '+$output)
        $exe=Join-Path $output 'HowToFishTrainer.exe'
    } else {
        $exe=Join-Path $projectRoot ('release\AbyssalCrab Studio-HowToFish-V1.1.37-'+$PackageMode+'.exe')
        Copy-Item -LiteralPath (Join-Path $output 'HowToFishTrainer.exe') -Destination $exe -Force
    }
    Get-Item -LiteralPath $exe | Select-Object FullName,Length,@{Name='ProductName';Expression={$_.VersionInfo.ProductName}},@{Name='FileDescription';Expression={$_.VersionInfo.FileDescription}}
} finally {
    $env:NUGET_PACKAGES=$savedPackages
    $env:DOTNET_CLI_HOME=$savedCli
    $env:DOTNET_CLI_TELEMETRY_OPTOUT=$savedTelemetry
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE=$savedFirstTime
}
