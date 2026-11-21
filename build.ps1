param(
    [string]$Dotnet = 'dotnet',
    [string]$Python = '',
    [string]$OutputDirectory = '',
    [string]$NuGetConfig = '',
    [switch]$UseSourceWorker
)
$ErrorActionPreference='Stop'
$project=(Resolve-Path -LiteralPath $PSScriptRoot).Path
$qadr=(Resolve-Path -LiteralPath (Join-Path $project '..')).Path
$workspace=(Resolve-Path -LiteralPath (Join-Path $qadr '../..')).Path
if(-not $OutputDirectory){$OutputDirectory=Join-Path $workspace 'outputs/Qadr-Windows/Qadr'}
if(-not $Python){$Python=Join-Path $project 'vision/.venv/Scripts/python.exe'}
if(-not(Test-Path -LiteralPath $Python)){throw 'Provide the Python environment containing MediaPipe, OpenCV, Vosk, sounddevice and PyInstaller.'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$env:MSBUILDDISABLENODEREUSE='1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER='0'
foreach($component in @('Qadr.Guardian','Qadr.Engine','Qadr.Shell','Qadr.Tests')){
 $restoreArgs=@('restore',(Join-Path $project "$component/$component.csproj"),'-r','win-x64','-p:SelfContained=true','-p:NuGetAudit=false','--disable-parallel','-m:2','-nr:false')
 if($NuGetConfig){$restoreArgs+=@('--configfile',$NuGetConfig)}
 & $Dotnet @restoreArgs
 if($LASTEXITCODE -ne 0){throw "Restoring $component failed."}
 & $Dotnet build (Join-Path $project "$component/$component.csproj") --no-restore -c Release -v minimal -m:2 -nr:false
 if($LASTEXITCODE -ne 0){throw "Building $component failed."}
}
& $Dotnet run --project (Join-Path $project 'Qadr.Tests/Qadr.Tests.csproj') --no-build -c Release
if($LASTEXITCODE -ne 0){throw 'Core verification failed.'}
foreach($component in @('Qadr.Guardian','Qadr.Engine','Qadr.Shell')){
    & $Dotnet publish (Join-Path $project "$component/$component.csproj") --no-restore -c Release -r win-x64 --self-contained true -o $OutputDirectory -p:NuGetAudit=false -m:2 -nr:false
    if($LASTEXITCODE -ne 0){throw "Publishing $component failed."}
}
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'brand'),(Join-Path $OutputDirectory 'models'),(Join-Path $OutputDirectory 'vision') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $qadr 'brand/qadr-icon.png') -Destination (Join-Path $OutputDirectory 'brand/qadr-icon.png') -Force
Copy-Item -LiteralPath (Join-Path $qadr 'models/face_landmarker.task') -Destination (Join-Path $OutputDirectory 'models/face_landmarker.task') -Force
Copy-Item -LiteralPath (Join-Path $qadr 'models/vosk-model-small-en-us-0.15') -Destination (Join-Path $OutputDirectory 'models') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $project 'vision/worker.py') -Destination (Join-Path $OutputDirectory 'vision/worker.py') -Force
if(-not $UseSourceWorker){
    $visionBuild=Join-Path $workspace 'work/native-vision-021-dist'
    & $Python -m PyInstaller --noconfirm --clean --onedir --console --name Qadr.Vision --distpath $visionBuild --workpath (Join-Path $workspace 'work/native-vision-021-build') --specpath (Join-Path $workspace 'work') --paths (Join-Path $project 'vision') --hidden-import srt --collect-data mediapipe --collect-binaries mediapipe --collect-binaries vosk --exclude-module jax --exclude-module jaxlib --exclude-module scipy (Join-Path $project 'vision/worker.py')
    if($LASTEXITCODE -ne 0){throw 'Vision packaging failed.'}
    Get-ChildItem -LiteralPath (Join-Path $visionBuild 'Qadr.Vision') | Copy-Item -Destination (Join-Path $OutputDirectory 'vision') -Recurse -Force
}
if(-not(Test-Path -LiteralPath (Join-Path $OutputDirectory 'Qadr.pri'))){throw 'The WinUI resource index was not published.'}
$nugetCache=$env:NUGET_PACKAGES
if(-not $nugetCache){$nugetCache=Join-Path $env:USERPROFILE '.nuget/packages'}
& $Python (Join-Path $project 'collect_licenses.py') $OutputDirectory --nuget $nugetCache
if($LASTEXITCODE -ne 0){throw 'Collecting dependency notices failed.'}
Write-Host "Built $OutputDirectory. Run Qadr.exe. This build still requires pilot, accessibility and Store certification gates."
