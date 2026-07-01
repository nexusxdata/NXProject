param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration
)

# Remove arquivos temporários _wpftmp.csproj gerados pelo C# Dev Kit do VS Code.
$wpftmp = Get-ChildItem -Path $PSScriptRoot -Filter "*_wpftmp.csproj" -Recurse -ErrorAction SilentlyContinue
if ($wpftmp) {
    $wpftmp | Remove-Item -Force -ErrorAction SilentlyContinue
    Write-Host "Arquivos temporarios _wpftmp removidos ($($wpftmp.Count))." -ForegroundColor DarkGray
}

function Get-ExePath($configuration) {
    $path = Join-Path $PSScriptRoot "NXProject.Community\bin\$configuration\net10.0-windows\NXProject.Community.exe"
    if (Test-Path $path) { return $path }
    return $null
}

if ($PSBoundParameters.ContainsKey('Configuration')) {
    $exe = Get-ExePath $Configuration
    if ($null -eq $exe) {
        Write-Host "Executavel Community nao encontrado para $Configuration." -ForegroundColor Red
        exit 1
    }
}
else {
    $exe = @(
        Get-ExePath "Debug"
        Get-ExePath "Release"
    ) | Where-Object { $null -ne $_ } |
        Sort-Object { (Get-Item $_).LastWriteTime } -Descending |
        Select-Object -First 1

    if ($null -eq $exe) {
        Write-Host "Nenhuma build Community encontrada em Debug ou Release." -ForegroundColor Red
        exit 1
    }
}

# Assina arquivos ainda não assinados (build já assina, mas garante caso o usuário rode sem build)
$certSubject = "CN=NXProject Dev Local"
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $certSubject } |
        Select-Object -First 1

if (-not $cert) {
    Write-Host "Certificado local nao encontrado. Execute build-community.ps1 para criar." -ForegroundColor Yellow
    Write-Host "Tentando criar agora via sign-nxproject.ps1..." -ForegroundColor Cyan
    $signScript = Join-Path $PSScriptRoot "sign-nxproject.ps1"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $signScript
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq $certSubject } |
            Select-Object -First 1
}

if ($cert) {
    $dir = Split-Path $exe
    $files = Get-ChildItem $dir -Include "*.exe","*.dll" -Recurse -ErrorAction SilentlyContinue
    if ($files) {
        Write-Host "Assinando $($files.Count) arquivo(s)..." -ForegroundColor DarkGray
        foreach ($f in $files) {
            Set-AuthenticodeSignature -FilePath $f.FullName -Certificate $cert -ErrorAction SilentlyContinue | Out-Null
        }
    }
}

$exeSig = Get-AuthenticodeSignature $exe -ErrorAction SilentlyContinue
if ($exeSig.Status -ne "Valid") {
    Write-Host "Executavel sem assinatura valida. Rode build-community.ps1 primeiro." -ForegroundColor Red
    exit 1
}

Write-Host "Iniciando NXProject..." -ForegroundColor Cyan
& $exe
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0 -and $null -ne $exitCode) {
    Write-Host "App encerrou com codigo $exitCode." -ForegroundColor Yellow

    # 0xE0434352 = crash de excecao .NET nao tratada (SAC bloqueando DLL)
    if ($exitCode -eq -532462766 -and $cert) {
        Write-Host "Possivel bloqueio SAC. Re-assinando e tentando novamente..." -ForegroundColor Cyan
        $dir = Split-Path $exe
        Get-ChildItem $dir -Include "*.exe","*.dll" -Recurse -ErrorAction SilentlyContinue |
            ForEach-Object { Set-AuthenticodeSignature -FilePath $_.FullName -Certificate $cert -ErrorAction SilentlyContinue | Out-Null }
        Write-Host "Iniciando NXProject (segunda tentativa)..." -ForegroundColor Cyan
        & $exe
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0 -and $null -ne $exitCode) {
            Write-Host "Falhou novamente (codigo $exitCode). Verifique o Event Log do Windows." -ForegroundColor Red
        }
    }
}
