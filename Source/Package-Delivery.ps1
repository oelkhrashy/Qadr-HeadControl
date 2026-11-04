$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$delivery=[IO.Path]::GetFullPath((Join-Path $root '..\..\Qadr-HeadControl-Windows-x64.zip'))
$temporary=$delivery+'.updating'
if(Test-Path -LiteralPath $temporary){throw "Temporary package already exists: $temporary"}
$files=@(Get-ChildItem -LiteralPath (Join-Path $root 'Application') -Recurse -File | Where-Object {$_.FullName.Substring($root.Length+1) -notmatch '^Application\\Data\\'})
$files+=@(Get-ChildItem -LiteralPath (Join-Path $root 'Source') -Recurse -File | Where-Object {$_.FullName.Substring($root.Length+1) -notmatch '\\(bin|obj)\\'})
$files+=@(Get-ChildItem -LiteralPath $root -File | Where-Object {$_.Name -notmatch '^(Start-HeadMouse.*|Compare-HeadMouse-Results)\.cmd$'})
$files+=@(Get-ChildItem -LiteralPath (Join-Path $root 'Verification') -File | Where-Object {$_.Name -ne 'archive-integrity.log' -and ($_.Extension -in @('.log','.dll') -or $_.Name -in @('movement-lab-simulation.json','movement-lab-simulation.html','movement-lab-simulation.rest-check.json','movement-lab-preview.png','analog-benchmark.json','stability-benchmark.json','stability-noise-benchmark.json','wave-benchmark.json','wave-noise-benchmark.json','wave-analog-benchmark.json'))})
$review=Join-Path $root 'Verification\guided-recording-analysis'
if(Test-Path -LiteralPath $review){$files+=@(Get-ChildItem -LiteralPath $review -File | Where-Object {$_.Name -in @('review.html','review.json','rejected-slow-band-validation.json')})}
$recoveryReview=Join-Path $root 'Verification\recovery-review'
if(Test-Path -LiteralPath $recoveryReview){$files+=@(Get-ChildItem -LiteralPath $recoveryReview -File | Where-Object {$_.Name -in @('RESULTS.md','session-analysis.json')})}
$zip=[IO.Compression.ZipFile]::Open($temporary,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($file in $files){
        $name='Qadr/'+$file.FullName.Substring($root.Length+1).Replace('\','/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$name,[IO.Compression.CompressionLevel]::Optimal)
    }
}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($temporary)
try {
    if(@($zip.Entries | Where-Object {$_.FullName -like 'Qadr/Application/Data/*'}).Count){throw 'Personal data found in distribution.'}
    if(@($zip.Entries | Where-Object {$_.FullName -match 'Qadr/(Start-HeadMouse.*|Compare-HeadMouse-Results)\.cmd$'}).Count){throw 'Standalone HeadMouse review launchers found without their isolated dependencies.'}
    foreach($name in @('Qadr.Core.dll','Qadr.Engine.dll','Qadr.MovementLab.exe','Qadr.MovementLab.dll','Qadr.MovementLab.deps.json','Qadr.MovementLab.runtimeconfig.json')){
        $entry=$zip.GetEntry('Qadr/Application/'+$name)
        if(!$entry){throw "Package missing $name"}
        $stream=$entry.Open()
        try{$sha=[Security.Cryptography.SHA256]::Create();try{$hash=[Convert]::ToHexString($sha.ComputeHash($stream))}finally{$sha.Dispose()}}finally{$stream.Dispose()}
        if($hash -ne (Get-FileHash -LiteralPath (Join-Path $root "Application\$name") -Algorithm SHA256).Hash){throw "Package mismatch: $name"}
    }
    $count=$zip.Entries.Count
}finally{$zip.Dispose()}
Move-Item -LiteralPath $temporary -Destination $delivery -Force
"PASS: $count distribution entries; controller and test binaries verified; personal Data excluded." | Set-Content -LiteralPath (Join-Path $root 'Verification\archive-integrity.log')
Write-Output $delivery
