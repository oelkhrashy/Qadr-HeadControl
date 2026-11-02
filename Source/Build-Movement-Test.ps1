param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'Build-Controller.ps1') -Dotnet $Dotnet
$project=Join-Path $PSScriptRoot 'MovementLab\Qadr.MovementLab.csproj'
& $Dotnet restore $project --configfile (Join-Path $PSScriptRoot 'NuGet.Offline.Config') -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'Movement test restore failed. The .NET 10.0.12 self-contained runtime packs must be present in the NuGet cache.'}
& $Dotnet build $project --no-restore -c Release -v minimal -m:2 -nr:false
if($LASTEXITCODE -ne 0){throw 'Movement test build failed.'}
$output=Join-Path $PSScriptRoot 'MovementLab\bin\Release\net10.0-windows\win-x64'
foreach($name in @('Qadr.MovementLab.exe','Qadr.MovementLab.dll','Qadr.MovementLab.deps.json','Qadr.MovementLab.runtimeconfig.json')){
    Copy-Item -LiteralPath (Join-Path $output $name) -Destination (Join-Path $root "Application\$name") -Force
}
& (Join-Path $root 'Application\Qadr.MovementLab.exe') --self-test (Join-Path $root 'Verification\movement-lab-simulation.json')
if($LASTEXITCODE -ne 0){throw 'Guided test simulation failed.'}
Write-Host 'Movement test staged. Run Start-Movement-Test.cmd.'
