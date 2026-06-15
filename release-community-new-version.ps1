param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [ValidateSet("build", "patch", "minor", "major")]
    [string]$Bump = "build"
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

function Convert-ToAssemblyVersion([string]$Version) {
    $parts = $Version.Split('.')
    $major = [int]$parts[0]
    $minor = if ($parts.Count -gt 1) { [int]$parts[1] } else { 0 }
    $patch = if ($parts.Count -gt 2) { [int]$parts[2] } else { 0 }
    $build = if ($parts.Count -gt 3) { [int]$parts[3] } else { 0 }
    return "$major.$minor.$patch.$build"
}

function Set-ProjectVersion([string]$CsprojPath, [string]$NewVersion) {
    $assemblyVersion = Convert-ToAssemblyVersion $NewVersion
    $content = Get-Content $CsprojPath -Raw
    $content = $content -replace '<Version>[^<]+</Version>',           "<Version>$NewVersion</Version>"
    $content = $content -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$assemblyVersion</AssemblyVersion>"
    $content = $content -replace '<FileVersion>[^<]+</FileVersion>',   "<FileVersion>$assemblyVersion</FileVersion>"
    $content = $content -replace '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$NewVersion</InformationalVersion>"
    Set-Content -Path $CsprojPath -Value $content -Encoding UTF8 -NoNewline
}

function Step-Version([string]$Version, [string]$BumpType) {
    $parts = $Version.Split('.')
    $major = [int]$parts[0]
    $minor = if ($parts.Count -gt 1) { [int]$parts[1] } else { 0 }
    $patch = if ($parts.Count -gt 2) { [int]$parts[2] } else { 0 }
    $build = if ($parts.Count -gt 3) { [int]$parts[3] } else { 0 }
    switch ($BumpType) {
        "major" { $major++; $minor = 0; $patch = 0; $build = 1 }
        "minor" { $minor++; $patch = 0; $build = 1 }
        "patch" { $patch++; $build = 1 }
        "build" { $build++ }
    }
    return "{0}.{1}.{2}.{3:000}" -f $major, $minor, $patch, $build
}

$CurrentVersion = Get-ProjectVersion $ProjectFile
$NewVersion = Step-Version $CurrentVersion $Bump

Write-Host ""
Write-Host ">> Versao: $CurrentVersion → $NewVersion" -ForegroundColor Yellow
Set-ProjectVersion $ProjectFile $NewVersion

$ZipPath = Join-Path $DistDir "NXProject.Community-Release.zip"

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

        # Erros exclusivamente do _wpftmp (gerado pelo VS Code / MSBuild WPF) nao
        # indicam falha real — mas verificamos se o DLL principal foi de fato gerado
        # nesta rodada (timestamp recente) antes de aceitar como sucesso.
        $realErrors = $output | Where-Object {
            $_ -match '\] ?error' -and $_ -notmatch '_wpftmp\.csproj'
        }
        if (-not $realErrors) {
            $dllPath = Join-Path $OutputDir "NXProject.Community.dll"
            $dllAge  = if (Test-Path $dllPath) {
                (Get-Date) - (Get-Item $dllPath).LastWriteTime
            } else { [TimeSpan]::MaxValue }

            if ($dllAge.TotalSeconds -le 60) {
                Write-Host "  (erros de _wpftmp ignorados — DLL atualizado ha $([int]$dllAge.TotalSeconds)s)" -ForegroundColor DarkGray
                return
            } else {
                Write-Host ""
                Write-Host "Falha: apenas erros de _wpftmp, mas o DLL nao foi atualizado (age=$([int]$dllAge.TotalSeconds)s). Verifique o build." -ForegroundColor Red
                exit 1
            }
        }

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

# Remove arquivos _wpftmp.csproj gerados pelo C# Dev Kit do VS Code antes de compilar.
# Eles podem conflitar com o _wpftmp que o proprio MSBuild cria durante a compilacao WPF.
$wpftmp = Get-ChildItem -Path $SolutionDir -Filter "*_wpftmp.csproj" -Recurse -ErrorAction SilentlyContinue
if ($wpftmp) {
    $wpftmp | Remove-Item -Force -ErrorAction SilentlyContinue
    Write-Host "  Arquivos temporarios _wpftmp removidos ($($wpftmp.Count))." -ForegroundColor DarkGray
}

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
6. Execute .\release-community-build.ps1 -Configuration Release
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

# ── GitHub Release ────────────────────────────────────────────────────────────
Write-Step "Publicando GitHub Release v$NewVersion..."

$tag = "v$NewVersion"
$releaseNotes = "NXProject Community $tag"

$ghAvailable = Get-Command gh -ErrorAction SilentlyContinue
if (-not $ghAvailable) {
    Write-Host "  gh CLI nao encontrado. Instale em https://cli.github.com e faca 'gh auth login'." -ForegroundColor Yellow
    Write-Host "  Para publicar manualmente: gh release create $tag '$ZipPath' --title '$tag' --notes '$releaseNotes'" -ForegroundColor DarkGray
} else {
    gh release create $tag $ZipPath `
        --title "NXProject Community $tag" `
        --notes $releaseNotes `
        --repo nexusxdata/NXProject

    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "Release $tag publicada com sucesso no GitHub!" -ForegroundColor Green
        Write-Host "  https://github.com/nexusxdata/NXProject/releases/tag/$tag" -ForegroundColor DarkGray
    } else {
        Write-Host "  Falha ao criar a release. Verifique se esta autenticado: gh auth login" -ForegroundColor Red
    }
}
