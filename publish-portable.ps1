param(
    [string]$Runtime = "win-x64",
    [switch]$Build,
    [switch]$SingleFile
)

# Gera um pacote PORTATIL (self-contained): inclui o runtime .NET, entao o usuario
# so descompacta a pasta e executa o .exe — sem instalar nada.

$ErrorActionPreference = "Stop"
$SolutionDir = $PSScriptRoot
$ProjectFile = Join-Path $SolutionDir "NXProject.Community\NXProject.Community.csproj"
$DistDir     = Join-Path $SolutionDir "dist\community"
$PublishDir  = Join-Path $SolutionDir "NXProject.Community\bin\Publish\$Runtime"
$StageName   = "NXProject.Community-Portable-$Runtime"
$StageDir    = Join-Path $DistDir $StageName
$ZipPath     = Join-Path $DistDir "$StageName.zip"

function Write-Step($msg) {
    Write-Host ""
    Write-Host ">> $msg" -ForegroundColor Cyan
}

function Get-ProjectVersion([string]$CsprojPath) {
    $content = Get-Content $CsprojPath -Raw
    if ($content -match '<Version>([^<]+)</Version>') { return $Matches[1] }
    return "1.0.0.000"
}

function Convert-ToAssemblyVersion([string]$Version) {
    $parts = $Version.Split('.')
    $major = [int]$parts[0]
    $minor = if ($parts.Count -gt 1) { [int]$parts[1] } else { 0 }
    $patch = if ($parts.Count -gt 2) { [int]$parts[2] } else { 0 }
    $build = if ($parts.Count -gt 3) { [int]$parts[3] } else { 0 }
    return "$major.$minor.$patch.$build"
}

function Step-BuildVersion([string]$Version) {
    $parts = $Version.Split('.')
    $major = [int]$parts[0]
    $minor = if ($parts.Count -gt 1) { [int]$parts[1] } else { 0 }
    $patch = if ($parts.Count -gt 2) { [int]$parts[2] } else { 0 }
    $build = if ($parts.Count -gt 3) { [int]$parts[3] } else { 0 }
    $build++
    if ($build -gt 99) {
        $patch++
        $build = 1
    }
    return "{0}.{1}.{2}.{3:00}" -f $major, $minor, $patch, $build
}

function Set-ProjectVersion([string]$CsprojPath, [string]$NewVersion) {
    $assemblyVersion = Convert-ToAssemblyVersion $NewVersion
    $content = Get-Content $CsprojPath -Raw
    $content = $content -replace '<Version>[^<]+</Version>', "<Version>$NewVersion</Version>"
    $content = $content -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$assemblyVersion</AssemblyVersion>"
    $content = $content -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$assemblyVersion</FileVersion>"
    $content = $content -replace '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$NewVersion</InformationalVersion>"
    Set-Content -Path $CsprojPath -Value $content -Encoding UTF8 -NoNewline
}

function Remove-UnusedSatelliteResourceFolders([string]$PublishDir) {
    $keepCultures = @("pt-BR")
    $cultureFolders = Get-ChildItem -Path $PublishDir -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^[a-z]{2}(-[A-Z][A-Za-z]+)?$' -and $_.Name -notin $keepCultures }

    if ($cultureFolders) {
        $cultureFolders | Remove-Item -Recurse -Force
        Write-Host "  Pastas de recursos removidas: $($cultureFolders.Name -join ', ')" -ForegroundColor DarkGray
    }
}

# Encerra instancia aberta (evita lock de arquivos)
$procs = Get-Process -Name "NXProject.Community" -ErrorAction SilentlyContinue
if ($procs) {
    Write-Step "Encerrando NXProject.Community em execucao..."
    $procs | Stop-Process -Force
    Start-Sleep -Seconds 1
}

if ($Build) {
    $currentVersion = Get-ProjectVersion $ProjectFile
    $newVersion = Step-BuildVersion $currentVersion
    Write-Step "Versionando build Community ($currentVersion -> $newVersion)..."
    Set-ProjectVersion $ProjectFile $newVersion
}

Write-Step "Publicando self-contained ($Runtime, SingleFile=$($SingleFile.IsPresent))..."
if (Test-Path $PublishDir) { Remove-Item -LiteralPath $PublishDir -Recurse -Force }

$publishArgs = @(
    "publish", $ProjectFile,
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", "true",
    "-o", $PublishDir,
    "--nologo"
)
if ($SingleFile) {
    $publishArgs += "-p:PublishSingleFile=true"
    $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
    $publishArgs += "-p:EnableCompressionInSingleFile=true"
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { Write-Host "Falha no publish." -ForegroundColor Red; exit 1 }

Write-Step "Montando pasta de distribuicao portatil..."
if (Test-Path $StageDir) { Remove-Item -LiteralPath $StageDir -Recurse -Force }
New-Item -ItemType Directory -Path $StageDir -Force | Out-Null
Copy-Item -Path (Join-Path $PublishDir "*") -Destination $StageDir -Recurse -Force
Remove-UnusedSatelliteResourceFolders $StageDir

@"
NXProject Community — Pacote Portatil ($Runtime)

Como usar (NAO precisa instalar nada):
1. Descompacte TODO o conteudo deste .zip em uma pasta.
2. Execute o arquivo NXProject.Community.exe.

Este pacote ja inclui o runtime do .NET (self-contained), entao funciona mesmo
em um Windows sem o .NET instalado. Mantenha todos os arquivos na mesma pasta.

Licenca: edicao Community (Open Core) — ver LICENSE.txt no repositorio.
Contato: Nexus XData Tecnologia Ltda — comercial.nexus.xdata@gmail.com
"@ | Set-Content -Path (Join-Path $StageDir "LEIA-ME.txt") -Encoding UTF8

# Inclui a licenca no pacote
$licenseSrc = Join-Path $SolutionDir "LICENSE.txt"
if (Test-Path $licenseSrc) { Copy-Item $licenseSrc -Destination (Join-Path $StageDir "LICENSE.txt") -Force }

# Inclui script de diagnostico/tracelog no pacote
$traceScriptSrc = Join-Path $SolutionDir "NXProject-Tracelog.ps1"
if (Test-Path $traceScriptSrc) {
    Copy-Item $traceScriptSrc -Destination (Join-Path $StageDir "NXProject-Tracelog.ps1") -Force
}

Write-Step "Gerando ZIP portatil..."
if (Test-Path $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
Compress-Archive -Path (Join-Path $StageDir "*") -DestinationPath $ZipPath -Force

$zipSize = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Pacote PORTATIL gerado com sucesso!" -ForegroundColor Green
Write-Host "  Pasta: $StageDir" -ForegroundColor DarkGray
Write-Host "  Zip:   $ZipPath ($zipSize MB)" -ForegroundColor DarkGray
Write-Host "  Exe:   $StageName\NXProject.Community.exe" -ForegroundColor DarkGray
