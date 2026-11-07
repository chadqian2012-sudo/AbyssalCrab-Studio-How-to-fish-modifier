param(
    [ValidateSet('SingleFile','CompressedSingleFile','Folder','FrameworkDependent')][string]$PackageMode='SingleFile',
    [string]$OutputName='',
    [string]$DotnetPath='',
    [string]$PackageCache='',
    [string]$OutputDirectory=''
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if($DotnetPath){$sdk=(Resolve-Path -LiteralPath $DotnetPath).Path}else{$sdk=(Get-Command dotnet -ErrorAction Stop).Source}
if(!$OutputName){
    $OutputName=if($PackageMode -eq 'SingleFile'){'AbyssalCrab Studio-HowToFish-V1.1.37-整合版.exe'}else{'AbyssalCrab Studio-HowToFish-V1.1.37-'+$PackageMode+'.exe'}
}
if([IO.Path]::GetFileName($OutputName) -ne $OutputName -or [IO.Path]::GetExtension($OutputName) -ne '.exe'){throw 'OutputName must be an EXE filename'}
$output=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $projectRoot ('build\publish-'+$PackageMode)}
if($PackageCache){$PackageCache=(Resolve-Path -LiteralPath $PackageCache).Path}
$savedPackages=$env:NUGET_PACKAGES
$savedCli=$env:DOTNET_CLI_HOME
$savedTelemetry=$env:DOTNET_CLI_TELEMETRY_OPTOUT
$savedFirstTime=$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
try {
    if($PackageCache){$env:NUGET_PACKAGES=$PackageCache}
    $env:DOTNET_CLI_HOME=Join-Path $projectRoot 'build\cli-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
    $project=Join-Path $projectRoot 'source\HowToFishTrainer\HowToFishTrainer.csproj'
    $single=($PackageMode -ne 'Folder').ToString().ToLowerInvariant()
    $contained=($PackageMode -ne 'FrameworkDependent').ToString().ToLowerInvariant()
    $compressed=($PackageMode -eq 'CompressedSingleFile').ToString().ToLowerInvariant()
    & $sdk publish $project -c Release -r win-x64 --self-contained $contained -o $output --ignore-failed-sources "-p:PublishSingleFile=$single" '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:PublishTrimmed=false' "-p:EnableCompressionInSingleFile=$compressed" '-p:PublishReadyToRun=false' '-p:RunAnalyzers=false' '-p:EnableNETAnalyzers=false' '-p:NoWarn=CS8632'
    if($LASTEXITCODE -ne 0){throw 'Single-file publish failed'}
    if($PackageMode -eq 'Folder') {
        Write-Output ('Directory package: '+$output)
        $exe=Join-Path $output 'HowToFishTrainer.exe'
    } else {
        $published=Join-Path $output 'HowToFishTrainer.exe'
        $destination=if($OutputDirectory){$output}else{Join-Path $projectRoot 'release'}
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        $exe=Join-Path $destination $OutputName
        if([IO.Path]::GetFullPath($published) -ne [IO.Path]::GetFullPath($exe)){
            if($OutputDirectory){Move-Item -LiteralPath $published -Destination $exe -Force}
            else{Copy-Item -LiteralPath $published -Destination $exe -Force}
        }
    }
    Get-Item -LiteralPath $exe | Select-Object FullName,Length,@{Name='ProductName';Expression={$_.VersionInfo.ProductName}},@{Name='FileDescription';Expression={$_.VersionInfo.FileDescription}}
} finally {
    $env:NUGET_PACKAGES=$savedPackages
    $env:DOTNET_CLI_HOME=$savedCli
    $env:DOTNET_CLI_TELEMETRY_OPTOUT=$savedTelemetry
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE=$savedFirstTime
}
