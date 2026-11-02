param([string]$Dotnet='dotnet',[switch]$ProcessSmoke)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project=Join-Path $PSScriptRoot 'HeadControl.Tests\HeadControl.Tests.csproj'
$config=Join-Path $PSScriptRoot 'NuGet.Offline.Config'
Push-Location $PSScriptRoot
try {
    & $Dotnet restore $project --configfile $config -p:NuGetAudit=false
    if($LASTEXITCODE -ne 0){throw 'Restore failed. Install a compatible .NET 10 SDK.'}
    & $Dotnet build $project --no-restore -c Release -v minimal -m:2 -nr:false
    if($LASTEXITCODE -ne 0){throw 'Build failed.'}
    $tests=Join-Path $PSScriptRoot 'HeadControl.Tests\bin\Release\net10.0\HeadControl.Tests.dll'
    & $Dotnet $tests
    if($LASTEXITCODE -ne 0){throw 'Controller tests failed; application was not updated.'}
    $core=Join-Path $PSScriptRoot 'RecoveredCore\bin\Release\net10.0\Qadr.Core.dll'
    $original=Join-Path $root 'Verification\Original-Qadr.Core.dll'
    if(Test-Path -LiteralPath $original){
        & $Dotnet $tests --api $original $core
        if($LASTEXITCODE -ne 0){throw 'Public-interface checks failed; application was not updated.'}
    }
    Copy-Item -LiteralPath $core -Destination (Join-Path $root 'Application\Qadr.Core.dll') -Force
    if($ProcessSmoke){
        $data=Join-Path $root ('Verification\process-test-'+[Guid]::NewGuid().ToString('N'))
        & $Dotnet $tests --process-smoke (Join-Path $root 'Application') $data
        if($LASTEXITCODE -ne 0){throw 'Process smoke check failed.'}
    }
    Write-Host 'Qadr controller built, verified and staged. Run Start-Qadr.cmd.'
} finally {Pop-Location}
