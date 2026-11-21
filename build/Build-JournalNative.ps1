param([string]$CargoPath='')

$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$stage=Join-Path $PSScriptRoot 'staging\journal'
$target=Join-Path $stage 'native'
if(!$CargoPath){$CargoPath=(Get-Command cargo -ErrorAction Stop).Source}
& $CargoPath rustc --release --offline --locked --target x86_64-pc-windows-msvc --target-dir $target --manifest-path (Join-Path $root 'native\journal\Cargo.toml') -- -C target-feature=+crt-static
if($LASTEXITCODE -ne 0){throw 'Rust journal build failed'}
$library=Join-Path $stage 'abyssalcrab_journal.dll'
Copy-Item -LiteralPath (Join-Path $target 'x86_64-pc-windows-msvc\release\abyssalcrab_journal.dll') -Destination $library -Force
Get-FileHash -LiteralPath $library -Algorithm SHA256
