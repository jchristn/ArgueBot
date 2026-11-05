$ErrorActionPreference = 'Stop'
$output = & (Join-Path $PSScriptRoot 'ArgueBot.Cli.exe') service web
if ($LASTEXITCODE -ne 0) { throw 'ArgueBot could not start its workspace. Run the CLI for startup diagnostics.' }
$prefix = 'Open ArgueBot in your browser: '
$line = $output | Where-Object { $_.StartsWith($prefix) } | Select-Object -First 1
if (!$line) { throw 'ArgueBot did not report its workspace URL.' }
$url = $line.Substring($prefix.Length)
if ($url -notmatch '^https?://127\.0\.0\.1:[0-9]+/dashboard$') { throw 'ArgueBot reported an unexpected workspace URL.' }
Start-Process $url
