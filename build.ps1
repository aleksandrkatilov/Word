# Сборка «Пера» на Windows: восстановление пакетов, сборка, тесты ядра, проверка эталонов, публикация.
# Запуск:  powershell -ExecutionPolicy Bypass -File .\build.ps1   [-SkipPublish]
param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host '== restore' -ForegroundColor Cyan
dotnet restore Pero.sln
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Write-Host '== build' -ForegroundColor Cyan
dotnet build Pero.sln -c Release --no-restore
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Write-Host '== core tests' -ForegroundColor Cyan
dotnet run --project tests/Pero.Core.Tests -c Release --no-build
if ($LASTEXITCODE) { exit $LASTEXITCODE }

if (Get-Command python -ErrorAction SilentlyContinue) {
    Write-Host '== reference documents' -ForegroundColor Cyan
    python -m pip install --quiet python-docx pillow
    python samples/make_samples.py samples/docx
    dotnet run --project tools/Pero.Probe -c Release --no-build -- inspect samples/docx
}

if (-not $SkipPublish) {
    Write-Host '== publish' -ForegroundColor Cyan
    dotnet publish src/Pero.App -c Release -r win-x64 --self-contained true -o artifacts/Pero
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    Write-Host "Готово: artifacts\Pero\Pero.exe" -ForegroundColor Green
}
