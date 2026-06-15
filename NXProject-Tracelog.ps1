param(
    [switch]$UseDotnet,
    [switch]$Wait
)

$ErrorActionPreference = "Continue"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ExePath = Join-Path $ScriptDir "NXProject.Community.exe"
$DllPath = Join-Path $ScriptDir "NXProject.Community.dll"
$LogDir = Join-Path $ScriptDir "logs"
$Stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$TraceLog = Join-Path $LogDir "NXProject-tracelog-$Stamp.log"
$HostTraceLog = Join-Path $LogDir "NXProject-hosttrace-$Stamp.log"

New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

Start-Transcript -Path $TraceLog -Force | Out-Null
try {
    Write-Host "NXProject Community - TraceLog"
    Write-Host "Pasta: $ScriptDir"
    Write-Host "Trace: $TraceLog"
    Write-Host "Host trace: $HostTraceLog"
    Write-Host ""

    Write-Host "Sistema:"
    Get-ComputerInfo -Property OsName,OsVersion,WindowsVersion,OsArchitecture | Format-List

    Write-Host ".NET:"
    dotnet --info

    if (-not (Test-Path $DllPath)) {
        Write-Host "NXProject.Community.dll nao encontrado em $ScriptDir" -ForegroundColor Red
        exit 1
    }

    $oldCoreHostTrace = $env:COREHOST_TRACE
    $oldCoreHostTraceFile = $env:COREHOST_TRACEFILE
    $env:COREHOST_TRACE = "1"
    $env:COREHOST_TRACEFILE = $HostTraceLog

    try {
        if ($UseDotnet -or -not (Test-Path $ExePath)) {
            Write-Host "Iniciando via dotnet NXProject.Community.dll..."
            $process = Start-Process -FilePath "dotnet" -ArgumentList @("NXProject.Community.dll") -WorkingDirectory $ScriptDir -PassThru
        }
        else {
            Write-Host "Iniciando via NXProject.Community.exe..."
            $process = Start-Process -FilePath $ExePath -WorkingDirectory $ScriptDir -PassThru
        }

        Start-Sleep -Seconds 5
        if ($process.HasExited) {
            Write-Host "Processo encerrou logo apos iniciar. ExitCode=$($process.ExitCode)" -ForegroundColor Yellow
        }
        else {
            Write-Host "Processo iniciado e ainda em execucao. PID=$($process.Id)" -ForegroundColor Green
        }

        if ($Wait) {
            Write-Host "Aguardando o fechamento do NXProject..."
            $process.WaitForExit()
            Write-Host "Processo finalizado. ExitCode=$($process.ExitCode)"
        }
    }
    finally {
        $env:COREHOST_TRACE = $oldCoreHostTrace
        $env:COREHOST_TRACEFILE = $oldCoreHostTraceFile
    }
}
finally {
    Stop-Transcript | Out-Null
    Write-Host ""
    Write-Host "Logs gerados:"
    Write-Host "  $TraceLog"
    Write-Host "  $HostTraceLog"
}
