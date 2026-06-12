param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [ValidateSet("patch", "minor", "major")]
    [string]$Bump = "patch"
)

$SolutionDir = $PSScriptRoot
$ProjectFile = Join-Path $SolutionDir "NXProject.Community\NXProject.Community.csproj"
$OutputDir = Join-Path $SolutionDir "NXProject.Community\bin\$Configuration\net10.0-windows"
$DistDir = Join-Path $SolutionDir "dist\community"
$StageDir = Join-Path $DistDir "NXProject.Community"
$ReadmePath = Join-Path $StageDir "README-INSTALACAO.txt"
$SharedDllLockPattern = "because it is being used by another process"

# ── Bump de versão ────────────────────────────────────────────────────────────
function Get-ProjectVersion([string]$CsprojPath) {
    $content = Get-Content $CsprojPath -Raw
    if ($content -match '<Version>([^<]+)</Version>') { return $Matches[1] }
    return "1.0.0"
}

function Set-ProjectVersion([string]$CsprojPath, [string]$NewVersion) {
    $content = Get-Content $CsprojPath -Raw
    $content = $content -replace '<Version>[^<]+</Version>',           "<Version>$NewVersion</Version>"
    $content = $content -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$NewVersion.0</AssemblyVersion>"
    $content = $content -replace '<FileVersion>[^<]+</FileVersion>',   "<FileVersion>$NewVersion.0</FileVersion>"
    $content = $content -replace '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$NewVersion</InformationalVersion>"
    Set-Content -Path $CsprojPath -Value $content -Encoding UTF8 -NoNewline
}

function Step-Version([string]$Version, [string]$BumpType) {
    $parts = $Version.Split('.')
    $major = [int]$parts[0]
    $minor = if ($parts.Count -gt 1) { [int]$parts[1] } else { 0 }
    $patch = if ($parts.Count -gt 2) { [int]$parts[2] } else { 0 }
    switch ($BumpType) {
        "major" { $major++; $minor = 0; $patch = 0 }
        "minor" { $minor++; $patch = 0 }
        "patch" { $patch++ }
    }
    return "$major.$minor.$patch"
}

$CurrentVersion = Get-ProjectVersion $ProjectFile
$NewVersion = Step-Version $CurrentVersion $Bump

Write-Host ""
Write-Host ">> Versao: $CurrentVersion → $NewVersion" -ForegroundColor Yellow
Set-ProjectVersion $ProjectFile $NewVersion

$ZipPath = Join-Path $DistDir "NXProject.Community-$Configuration.zip"

function Write-Step($msg) {
    Write-Host ""
    Write-Host ">> $msg" -ForegroundColor Cyan
}

function Stop-NXProjectCommunityProcess {
    $processes = Get-Process -Name "NXProject.Community" -ErrorAction SilentlyContinue
    if ($null -eq $processes) { return }

    Write-Step "Encerrando NXProject.Community em execucao..."
    $processes | Stop-Process -Force
}

function Invoke-DotnetCommandWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ActionLabel,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    $attempt = 1
    while ($attempt -le 2) {
        $output = & $Command 2>&1
        $exitCode = $LASTEXITCODE
        if ($output) {
            $output | ForEach-Object { Write-Host $_ }
        }

        if ($exitCode -eq 0) {
            return
        }

        $combinedOutput = ($output | Out-String)
        $hasDllLock = $combinedOutput -match [regex]::Escape($SharedDllLockPattern)
        if (-not $hasDllLock -or $attempt -eq 2) {
            Write-Host ""
            Write-Host "$ActionLabel falhou." -ForegroundColor Red
            exit 1
        }

        Write-Step "Detectado bloqueio temporario de DLL do .NET. Reiniciando build server e tentando novamente..."
        dotnet build-server shutdown | Out-Host
        Start-Sleep -Seconds 1
        $attempt++
    }
}

Stop-NXProjectCommunityProcess

Write-Step "Compilando NXProject Community ($Configuration)..."
Invoke-DotnetCommandWithRetry -ActionLabel "A compilacao" -Command {
    dotnet build $ProjectFile -c $Configuration --nologo
}

Write-Step "Preparando pasta de distribuicao..."
if (Test-Path $StageDir) {
    Remove-Item -LiteralPath $StageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $StageDir -Force | Out-Null

Copy-Item -Path (Join-Path $OutputDir "*") -Destination $StageDir -Recurse -Force

@"
NXProject Community

Como executar:
1. Extraia todo o conteudo deste .zip para uma pasta local.
2. Execute o arquivo NXProject.Community.exe.

Requisito:
- Microsoft .NET Desktop Runtime 10.0 para Windows
- Download oficial do runtime e SDK: https://dotnet.microsoft.com/en-us/download/dotnet/10.0
- Download do VS Code: https://code.visualstudio.com/download

Se o aplicativo nao abrir por falta do .NET:
1. Instale o runtime ".NET Desktop Runtime 10.0 (x64)".
2. Depois execute novamente o NXProject.Community.exe.

Se voce preferir compilar no VS Code em vez de usar o .zip:
1. Instale o ".NET 10 SDK".
2. Instale o VS Code em https://code.visualstudio.com/download
3. Abra a pasta do repositorio no VS Code.
4. Abra o terminal integrado.
5. Execute .\setup-community-vscode.ps1
6. Execute .\build-community.ps1 -Configuration Release
7. O executavel sera gerado em NXProject.Community\bin\Release\net10.0-windows\NXProject.Community.exe

Sugestao para distribuicao:
- Publique este .zip junto com uma pagina de download e um link para instalacao do .NET Desktop Runtime.

Contato:
- Nexus XData Tecnologia Ltda
- comercial.nexus.xdata@gmail.com
"@ | Set-Content -Path $ReadmePath -Encoding UTF8

$LicenseSrc = Join-Path $SolutionDir "LICENSE.txt"
if (Test-Path $LicenseSrc) {
    Copy-Item -Path $LicenseSrc -Destination (Join-Path $StageDir "LICENSE.txt") -Force
}

Write-Step "Gerando arquivo ZIP..."
if (Test-Path $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}
Compress-Archive -Path (Join-Path $StageDir "*") -DestinationPath $ZipPath -Force

Write-Host ""
Write-Host "Pacote Community gerado com sucesso!" -ForegroundColor Green
Write-Host "  Pasta: $StageDir" -ForegroundColor DarkGray
Write-Host "  Zip:   $ZipPath" -ForegroundColor DarkGray
