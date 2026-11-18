[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$outputFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
if (Test-Path -LiteralPath $outputFile) {
    throw "Choose a new output path. A file already exists at $outputFile"
}
if ([IO.Path]::GetExtension($outputFile) -ine '.exe') {
    throw 'The output path must end in .exe.'
}

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
$ui = Join-Path $PSScriptRoot '现代界面'
$inputs = @('IssueStore.cs', 'CategoryCatalog.cs', 'LocalJson.cs', 'IssueSearch.cs') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$inputs += @(Get-ChildItem -LiteralPath $ui -Filter '*.cs' -File | ForEach-Object FullName)
$resources = Get-ChildItem -LiteralPath $ui -Filter '*.xaml' -File |
    ForEach-Object { "/resource:$($_.FullName),Pocket.$($_.Name)" }
$references = @('System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Runtime.Serialization.dll',
    'System.IO.Compression.dll', 'System.Xaml.dll') | ForEach-Object { "/reference:$_" }
$references += @('PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll') |
    ForEach-Object { "/reference:$(Join-Path $wpf $_)" }
$htmlParser = Join-Path $PSScriptRoot '第三方\HtmlAgilityPack'
$htmlAssembly = Join-Path $htmlParser 'HtmlAgilityPack.dll'
$htmlLicense = Join-Path $htmlParser 'LICENSE.txt'
$references += "/reference:$htmlAssembly"
$resources += @("/resource:$htmlAssembly,Pocket.HtmlAgilityPack.dll", "/resource:$htmlLicense,Pocket.HtmlAgilityPack.LICENSE.txt")

$icon = Join-Path $PSScriptRoot '应用图标.ico'
$manifest = Join-Path $PSScriptRoot '应用清单.manifest'
$options = @('/nologo', '/utf8output', '/codepage:65001', '/target:winexe', '/main:PocketProgram', '/platform:anycpu',
    '/optimize+', '/warnaserror+', "/out:$outputFile", "/win32icon:$icon", "/win32manifest:$manifest")
& $compiler @options @references @resources @inputs
if ($LASTEXITCODE -ne 0) {
    throw "The compiler exited with code $LASTEXITCODE."
}
Get-Item -LiteralPath $outputFile
