$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$variants = @('AbyssalCrab Studio-HowToFish-V1.1.37-整合版.exe', 'AbyssalCrab Studio-HowToFish-V1.1.37-压缩单文件.exe')
$results = foreach ($name in $variants) {
    $exePath = Join-Path $projectRoot ('release\' + $name)
    $preview = Start-Process -FilePath $exePath -ArgumentList '--ui-preview' -PassThru -WindowStyle Hidden
    try {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        do {
            Start-Sleep -Milliseconds 100
            $preview.Refresh()
        } while (!$preview.HasExited -and $preview.MainWindowHandle -eq 0 -and $watch.Elapsed.TotalSeconds -lt 15)
        if ($preview.HasExited) { throw ('Preview exited before window creation: ' + $name) }
        if (!$preview.Responding -or $preview.MainWindowTitle -ne 'AbyssalCrab Studio') { throw ('Preview window is not ready: ' + $name) }
        $title = $preview.MainWindowTitle
        $responding = $preview.Responding
        $windowCreated = $preview.MainWindowHandle -ne 0
        $null = $preview.CloseMainWindow()
        if (!$preview.WaitForExit(10000)) { throw ('Preview did not close normally: ' + $name) }
        if ($preview.ExitCode -ne 0) { throw ('Preview exit code: ' + $preview.ExitCode) }
        [PSCustomObject]@{ ExePath=$exePath; SHA256=(Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant(); MainWindowTitle=$title; Responding=$responding; WindowCreated=$windowCreated; ExitCode=$preview.ExitCode; PreviewMode=$true; RealGamePolling=$false }
    } finally {
        if (!$preview.HasExited) { $preview.Kill(); $preview.WaitForExit() }
        $preview.Dispose()
    }
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $projectRoot 'verification\preferences-native-smoke.json') -Encoding utf8
$results | Format-Table ExePath,Responding,ExitCode
