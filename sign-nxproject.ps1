# Executar como Administrador
# Cria certificado de desenvolvedor local em LocalMachine\My (acessivel a todos os usuarios)
# e assina os binarios do NXProject

$CertSubject = "CN=NXProject Dev Local"
$ProjectBin  = @(
    "c:\Users\carmo\Projetos\NXProject\NXProject.Community\bin\Debug\net10.0-windows"
    "c:\Users\carmo\Projetos\NXProject\NXProject.Community\bin\Release\net10.0-windows"
)

Write-Host "==> Verificando certificado existente em LocalMachine\My..." -ForegroundColor Cyan
$cert = Get-ChildItem Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $CertSubject } |
        Select-Object -First 1

if (-not $cert) {
    Write-Host "==> Criando certificado autoassinado em LocalMachine\My..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $CertSubject `
        -CertStoreLocation "Cert:\LocalMachine\My" `
        -KeyUsage DigitalSignature `
        -FriendlyName "NXProject Dev Local" `
        -NotAfter (Get-Date).AddYears(10)
    Write-Host "   Certificado criado: $($cert.Thumbprint)" -ForegroundColor Green
} else {
    Write-Host "   Certificado ja existe: $($cert.Thumbprint)" -ForegroundColor Green
}

# Exporta e instala como Trusted Publisher e Trusted Root
Write-Host "==> Instalando certificado como confiavel..." -ForegroundColor Cyan
$certFile = "$env:TEMP\NXProjectDev.cer"
Export-Certificate -Cert $cert -FilePath $certFile -Force | Out-Null

try {
    Import-Certificate -FilePath $certFile -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher" | Out-Null
    Import-Certificate -FilePath $certFile -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
    Write-Host "   Certificado instalado como confiavel." -ForegroundColor Green
} catch {
    Write-Host "   Erro ao instalar: $_" -ForegroundColor Red
    exit 1
}

# Assina todos os .exe e .dll nas pastas de build
Write-Host "==> Assinando binarios..." -ForegroundColor Cyan
$signed = 0
$failed = 0
foreach ($dir in $ProjectBin) {
    if (-not (Test-Path $dir)) { continue }
    Get-ChildItem $dir -Include "*.exe","*.dll" -Recurse | ForEach-Object {
        $result = Set-AuthenticodeSignature -FilePath $_.FullName -Certificate $cert -ErrorAction SilentlyContinue
        if ($result.Status -eq "Valid" -or $result.Status -eq "UnknownError") { $signed++ } else { $failed++ }
    }
}
Write-Host "   $signed arquivo(s) assinado(s), $failed falha(s)." -ForegroundColor Green

Write-Host ""
Write-Host "Pronto! Execute o run-community.ps1 normalmente." -ForegroundColor Cyan
Write-Host "O certificado fica em LocalMachine\My e funciona para todos os usuarios." -ForegroundColor DarkGray
