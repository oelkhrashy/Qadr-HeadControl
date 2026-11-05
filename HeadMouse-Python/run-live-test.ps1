# One command to try the HeadMouse pointer: sets up the Python packages the
# first time, then starts the live test (calibration, pointing, and the
# 18-task movement test on Ctrl+Alt+T). Never clicks.
#
#     powershell -ExecutionPolicy Bypass -File HeadMouse-Python\run-live-test.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
if (-not (Test-Path ".venv\Scripts\python.exe")) {
    Write-Host "First run: creating the Python environment..."
    py -3.11 -m venv .venv
    .venv\Scripts\python -m pip install --upgrade pip
    .venv\Scripts\python -m pip install -r requirements.txt
}
.venv\Scripts\python tools\live_pointer.py
