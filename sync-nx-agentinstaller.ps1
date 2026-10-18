<#
.SYNOPSIS
Copia o NX.AgentInstaller (biblioteca compartilhada com o NXPilot) do repositorio NXPilot
para esta pasta. A fonte da verdade e o NXPilot (..\NXPilot\NX.AgentInstaller); aqui fica
uma copia para o NXProject compilar sozinho (quem clona so o NXProject nao tem o NXPilot).

  .\sync-nx-agentinstaller.ps1                     # usa ..\NXPilot ao lado deste repositorio
  .\sync-nx-agentinstaller.ps1 -Source C:\...\NXPilot

Quando o pacote for publicado no NuGet, troque o ProjectReference por PackageReference e
apague a pasta NX.AgentInstaller deste repositorio.
#>
param([string]$Source = (Join-Path $PSScriptRoot "..\NXPilot"))

$from = Join-Path $Source "NX.AgentInstaller"
$to = Join-Path $PSScriptRoot "NX.AgentInstaller"
if (-not (Test-Path $from)) { Write-Host "Nao encontrei $from" -ForegroundColor Red; exit 1 }
New-Item -ItemType Directory -Path $to -Force | Out-Null
Get-ChildItem $from -File | Where-Object { $_.Extension -in ".cs", ".csproj", ".md" } | Copy-Item -Destination $to -Force
Write-Host "NX.AgentInstaller sincronizado de $from" -ForegroundColor Green
git -C $PSScriptRoot status --short -- NX.AgentInstaller
