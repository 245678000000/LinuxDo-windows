# 构建 LinuxDo for Windows：WPF 窗口、原生侧栏、Fluent 主题和 WebView2 内容。
# 用法：./build.ps1 [release|publish|installer|debug|test] [-Arch x64|arm64]
#   release   = publish + installer（默认）
#   publish   发布自带 .NET 运行时的程序目录，并打包便携版 zip
#   installer 用 Inno Setup 6 生成安装程序（需要先 publish）
param(
    [ValidateSet('release', 'publish', 'installer', 'debug', 'test')] [string] $Mode = 'release',
    [ValidateSet('x64', 'arm64')] [string] $Arch = 'x64'
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$project = 'src/LinuxDo/LinuxDo.csproj'
$version = (Select-Xml -Path $project -XPath '//Version').Node.InnerText
$publish = "artifacts/publish/$Arch"

function Invoke-Checked([scriptblock] $Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "命令失败（退出码 $LASTEXITCODE）：$Command" }
}

if ($Mode -eq 'test') {
    Write-Host '==> 运行核心测试'
    Invoke-Checked { dotnet run --project tests/LinuxDo.Tests -c Release }
    exit 0
}

if ($Mode -eq 'debug') {
    Write-Host '==> 编译调试版'
    Invoke-Checked { dotnet build $project -c Debug }
    Write-Host "==> 完成：src/LinuxDo/bin/Debug/net10.0-windows/LinuxDo.exe"
    exit 0
}

if ($Mode -in 'publish', 'release') {
    Write-Host "==> 发布 win-$Arch（自带 .NET 运行时）"
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    Invoke-Checked { dotnet publish $project -c Release -r "win-$Arch" --self-contained -o $publish }

    Write-Host '==> 打包便携版'
    $zip = "artifacts/LinuxDo-$version-win-$Arch.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory((Resolve-Path $publish), (Join-Path $PSScriptRoot $zip), 'Optimal', $false)
}

if ($Mode -in 'installer', 'release') {
    Write-Host "==> 生成安装程序 $Arch"
    if (-not (Test-Path "$publish/LinuxDo.exe")) { throw "先运行 ./build.ps1 publish -Arch $Arch" }
    $iscc = Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
    if (-not $iscc) {
        $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $iscc) { throw '需要 Inno Setup 6：winget install JRSoftware.InnoSetup' }
    Invoke-Checked { & $iscc "/DAppVersion=$version" "/DArch=$Arch" installer/LinuxDo.iss }
}

Write-Host "==> 完成：$(Join-Path $PSScriptRoot 'artifacts')"
