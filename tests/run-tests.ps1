param([switch]$Layout)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$project = Join-Path $root 'SC6000DelayedMonitor'
$out = Join-Path $root 'test-results'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'Visual Studio MSBuild is required.' }
$runtimeConfig = Join-Path $project 'bin\Release\config.ini'
$savedConfig = if (Test-Path -LiteralPath $runtimeConfig) { [IO.File]::ReadAllBytes($runtimeConfig) } else { $null }
try {
    & $msbuild (Join-Path $root 'SC6000DelayedMonitor.sln') /t:Build /p:Configuration=Release /v:minimal /nologo
    if ($LASTEXITCODE) { throw 'Build failed' }
} finally {
    if ($null -ne $savedConfig) { [IO.File]::WriteAllBytes($runtimeConfig, $savedConfig) }
}
$csc = Join-Path (Split-Path $msbuild) 'Roslyn\csc.exe'
$sdk = 'C:\Program Files\VisionMaster4.4.50\Development\V4.x\ComControls\Assembly'
$refs = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll')
$refs += @('VM.Core','VM.PlatformSDKCS','VMControls.BaseInterface','VMControls.Interface','VMControls.RenderInterface','VMControls.Winform.Release') | ForEach-Object { Join-Path $sdk ($_ + '.dll') }
$arguments = @('/nologo','/target:exe','/main:BufferTests',('/out:' + (Join-Path $out 'BufferTests.exe')))
$arguments += $refs | ForEach-Object { '/r:' + $_ }
$arguments += Get-ChildItem -LiteralPath $project -Filter '*.cs' | ForEach-Object FullName
$arguments += Join-Path $PSScriptRoot 'BufferTests.cs'
$arguments += Join-Path $PSScriptRoot 'QueueTests.cs'
& $csc @arguments
if ($LASTEXITCODE) { throw 'Test compilation failed' }
Copy-Item -LiteralPath (Join-Path $project 'App.config') -Destination (Join-Path $out 'BufferTests.exe.config')
& (Join-Path $out 'BufferTests.exe')
if ($LASTEXITCODE) { throw 'Buffer tests failed' }
if ($Layout) {
    & $csc /nologo /target:exe ('/win32manifest:' + (Join-Path $project 'app.manifest')) ('/out:' + (Join-Path $out 'LayoutTests.exe')) /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'LayoutTests.cs')
    if ($LASTEXITCODE) { throw 'Layout test compilation failed' }
    Copy-Item -LiteralPath (Join-Path $project 'App.config') -Destination (Join-Path $out 'LayoutTests.exe.config')
    & (Join-Path $out 'LayoutTests.exe') (Join-Path $project 'bin\Release\SC6000DelayedMonitor.exe')
    if ($LASTEXITCODE) { throw 'Layout tests failed' }
}
