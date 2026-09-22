param(
    [string]$OutputPath,
    [switch]$RunTests,
    [string]$TestOutputPath
)

$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework C# compiler not found.'
}

$developmentDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$applicationDirectory = Split-Path -Parent $developmentDirectory
$outputName = [Text.Encoding]::UTF8.GetString(
    [Convert]::FromBase64String('7ZSE66Gc7KCd7Yq47IOd7ISx6riwLmV4ZQ=='))
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $applicationDirectory $outputName
} else {
    $OutputPath = [IO.Path]::GetFullPath($OutputPath)
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

& $compiler `
    /nologo `
    /target:winexe `
    /platform:anycpu `
    /optimize+ `
    /codepage:65001 `
    "/win32manifest:$developmentDirectory\app.manifest" `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /reference:System.Web.Extensions.dll `
    "/out:$OutputPath" `
    "$developmentDirectory\Program.cs" `
    "$developmentDirectory\GeneratorCore.cs"

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Build complete: $OutputPath"

if ($RunTests) {
    if ([string]::IsNullOrWhiteSpace($TestOutputPath)) {
        $TestOutputPath = Join-Path $outputDirectory 'GeneratorCoreTests.exe'
    } else {
        $TestOutputPath = [IO.Path]::GetFullPath($TestOutputPath)
    }

    if ($TestOutputPath.Equals($OutputPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw '테스트 출력 경로는 앱 출력 경로와 달라야 합니다.'
    }

    $testOutputDirectory = Split-Path -Parent $TestOutputPath
    if (-not (Test-Path -LiteralPath $testOutputDirectory)) {
        New-Item -ItemType Directory -Path $testOutputDirectory | Out-Null
    }

    & $compiler `
        /nologo `
        /target:exe `
        /platform:anycpu `
        /optimize+ `
        /codepage:65001 `
        /reference:System.Web.Extensions.dll `
        "/out:$TestOutputPath" `
        "$developmentDirectory\GeneratorCore.cs" `
        "$developmentDirectory\GeneratorCoreTests.cs"

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    & $TestOutputPath
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    Write-Host "Tests complete: $TestOutputPath"
}
