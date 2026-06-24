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

# Assina os binarios com certificado local para contornar Smart App Control / WDAC
function Invoke-SignBinaries($exePath) {
    $certSubject = "CN=NXProject Dev Local"
    $cert = Get-ChildItem Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq $certSubject } |
            Select-Object -First 1

    if (-not $cert) {
        Write-Host ""
        Write-Host "AVISO: Certificado de assinatura nao encontrado." -ForegroundColor Yellow
        Write-Host "Se o aplicativo for bloqueado pelo Windows, rode o comando abaixo" -ForegroundColor Yellow
        Write-Host "em um Terminal como ADMINISTRADOR:" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  powershell -ExecutionPolicy Bypass -File `"$PSScriptRoot\sign-nxproject.ps1`"" -ForegroundColor Cyan
        Write-Host ""
        return
    }

    $dir = Split-Path $exePath
    $files = Get-ChildItem $dir -Include "*.exe","*.dll" -Recurse -ErrorAction SilentlyContinue
    foreach ($f in $files) {
        $sig = Get-AuthenticodeSignature $f.FullName -ErrorAction SilentlyContinue
        # Só assina se não tiver assinatura válida ou for mais novo que a assinatura
        if ($sig.Status -ne "Valid") {
            Set-AuthenticodeSignature -FilePath $f.FullName -Certificate $cert -ErrorAction SilentlyContinue | Out-Null
        }
    }
}

Invoke-SignBinaries $exe

Start-Process $exe
