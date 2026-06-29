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

function Invoke-UserCertificateSetup {
    param(
        [switch]$Recreate
    )

    Write-Host ""
    if ($Recreate) {
        Write-Host "A chave local esta ausente ou quebrada. Recriando automaticamente..." -ForegroundColor Yellow
    }
    else {
        Write-Host "Preparando assinatura local do usuario..." -ForegroundColor Cyan
    }

    $arguments = @()
    if ($Recreate) {
        $arguments += "-RecreateCertificate"
    }

    $signScript = Join-Path $PSScriptRoot "sign-nxproject.ps1"
    $processArguments = @(
        "-NoProfile"
        "-ExecutionPolicy"
        "Bypass"
        "-File"
        $signScript
    ) + $arguments

    & powershell.exe @processArguments
    return $LASTEXITCODE -eq 0
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

# Assina os binarios com certificado local para contornar Smart App Control / WDAC
function Invoke-SignBinaries($exePath) {
    $certSubject = "CN=NXProject Dev Local"
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq $certSubject } |
            Select-Object -First 1

    if (-not $cert) {
        if (-not (Invoke-UserCertificateSetup)) {
            return $false
        }

        $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
                Where-Object { $_.Subject -eq $certSubject } |
                Select-Object -First 1

        if (-not $cert) {
            return $false
        }
    }

    $dir = Split-Path $exePath
    $files = Get-ChildItem $dir -Include "*.exe","*.dll" -Recurse -ErrorAction SilentlyContinue
    foreach ($f in $files) {
        $sig = Get-AuthenticodeSignature $f.FullName -ErrorAction SilentlyContinue
        if ($sig.Status -ne "Valid") {
            try {
                $result = Set-AuthenticodeSignature -FilePath $f.FullName -Certificate $cert -ErrorAction Stop
                if ($result.Status -ne "Valid") {
                    if (-not (Invoke-UserCertificateSetup -Recreate)) {
                        return $false
                    }
                    break
                }
            }
            catch {
                if (-not (Invoke-UserCertificateSetup -Recreate)) {
                    return $false
                }
                break
            }
        }
    }

    $exeSignature = Get-AuthenticodeSignature $exePath -ErrorAction SilentlyContinue
    if ($exeSignature.Status -ne "Valid") {
        Write-Host "O executavel permaneceu sem uma assinatura valida." -ForegroundColor Red
        return $false
    }

    return $true
}

if (-not (Invoke-SignBinaries $exe)) {
    Write-Host "O executavel nao sera iniciado enquanto a assinatura nao estiver valida." -ForegroundColor Red
    Write-Host "A assinatura local nao exige permissao de administrador." -ForegroundColor Yellow
    Write-Host "Para tentar manualmente:" -ForegroundColor Yellow
    Write-Host "  powershell -ExecutionPolicy Bypass -File `"$PSScriptRoot\sign-nxproject.ps1`" -RecreateCertificate" -ForegroundColor Cyan
    exit 1
}

Write-Host "Iniciando NXProject..." -ForegroundColor Cyan
$t0 = [datetime]::UtcNow
& $exe
$elapsed = ([datetime]::UtcNow - $t0).TotalSeconds

if ($elapsed -lt 3) {
    Write-Host "App encerrou em $([math]::Round($elapsed,1))s. Provavel bloqueio de certificado. Recriando e tentando novamente..." -ForegroundColor Yellow
    if (-not (Invoke-UserCertificateSetup -Recreate)) {
        Write-Host "Falha ao recriar certificado. Abortando." -ForegroundColor Red
        exit 1
    }
    Write-Host "Reiniciando NXProject..." -ForegroundColor Cyan
    & $exe
}
