param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$config=Join-Path $PSScriptRoot 'NuGet.Offline.Config'
$testsProject=Join-Path $PSScriptRoot 'HeadControl.Tests\HeadControl.Tests.csproj'
$engineProject=Join-Path $PSScriptRoot 'RecoveredEngine\Qadr.Engine.csproj'
foreach($project in @($testsProject,$engineProject)){
    & $Dotnet restore $project --configfile $config -p:NuGetAudit=false
    if($LASTEXITCODE -ne 0){throw 'Offline restore failed; install the .NET 10 SDK and Windows SDK reference packs.'}
    & $Dotnet build $project --no-restore -c Release -v minimal -m:2 -nr:false
    if($LASTEXITCODE -ne 0){throw 'Build failed; application binaries were not staged.'}
}
$tests=Join-Path $PSScriptRoot 'HeadControl.Tests\bin\Release\net10.0\HeadControl.Tests.dll'
$core=Join-Path $PSScriptRoot 'RecoveredCore\bin\Release\net10.0\Qadr.Core.dll'
$engine=Join-Path $PSScriptRoot 'RecoveredEngine\bin\Release\net10.0-windows10.0.19041.0\Qadr.Engine.dll'
& $Dotnet $tests
if($LASTEXITCODE -ne 0){throw 'Controller tests failed.'}
& $Dotnet $tests --api (Join-Path $root 'Verification\Original-Qadr.Core.dll') $core
if($LASTEXITCODE -ne 0){throw 'Original public interface check failed.'}
# A disposable fixture uses hard links only for files the test never overwrites.
$fixture=Join-Path $root ('Verification\recovery-build-'+[Guid]::NewGuid().ToString('N'))
$from=Join-Path $root 'Application'
foreach($file in Get-ChildItem -LiteralPath $from -Recurse -File | Where-Object {$_.FullName -notlike "$from\Data\*"}){
    $relative=$file.FullName.Substring($from.Length+1);$destination=Join-Path $fixture $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    if($relative -in @('Qadr.Engine.dll','Qadr.Core.dll')){continue}
    New-Item -ItemType HardLink -Path $destination -Target $file.FullName | Out-Null
}
Copy-Item -LiteralPath $core -Destination (Join-Path $fixture 'Qadr.Core.dll')
Copy-Item -LiteralPath $engine -Destination (Join-Path $fixture 'Qadr.Engine.dll')
& $Dotnet $tests --process-smoke $fixture (Join-Path $fixture 'TestData')
if($LASTEXITCODE -ne 0){throw 'Engine recovery checks failed; application binaries were not staged.'}
if(Get-Process -Name Qadr,Qadr.Engine,Qadr.Guardian,Qadr.MovementLab -ErrorAction SilentlyContinue){throw 'Close Qadr and the movement test before staging the verified Core and Engine.'}
Copy-Item -LiteralPath $core -Destination (Join-Path $root 'Application\Qadr.Core.dll') -Force
Copy-Item -LiteralPath $engine -Destination (Join-Path $root 'Application\Qadr.Engine.dll') -Force
# Verify the final installed assembly identities; user Data is never written here.
if((Get-FileHash -LiteralPath $core).Hash -ne (Get-FileHash -LiteralPath (Join-Path $root 'Application\Qadr.Core.dll')).Hash){throw 'Core copy verification failed.'}
if((Get-FileHash -LiteralPath $engine).Hash -ne (Get-FileHash -LiteralPath (Join-Path $root 'Application\Qadr.Engine.dll')).Hash){throw 'Engine copy verification failed.'}
Write-Host 'Verified recovery update staged in the existing application folder.'
