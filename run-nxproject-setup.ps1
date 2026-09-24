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

# O exe fica travado enquanto o instalador esta aberto, e ai o build falha com MSB3027.
# Encerrar antes de compilar evita ter que fechar a janela na mao a cada teste.
function Stop-NXProjectSetupProcess {
    $processes = Get-Process -Name "NXProject-Setup" -ErrorAction SilentlyContinue
    if ($null -eq $processes) { return }

    Write-Host "Encerrando NXProject-Setup em execucao..." -ForegroundColor DarkGray
    $processes | Stop-Process -Force
    # Stop-Process volta antes do Windows liberar o arquivo; esperar evita a corrida com o build.
    $processes | ForEach-Object { $_.WaitForExit(5000) | Out-Null }
}

function Get-ExePath($configuration) {
    $path = Join-Path $PSScriptRoot "NXProject-Setup\bin\$configuration\net10.0-windows\win-x64\NXProject-Setup.exe"
    if (Test-Path $path) { return $path }
    return $null
}

# Compila antes de rodar (Release por padrao, como o build-community): o que aparece na tela e
# sempre o codigo atual, sem risco de testar um exe velho.
$buildConfig = if ($PSBoundParameters.ContainsKey('Configuration')) { $Configuration } else { "Release" }
Stop-NXProjectSetupProcess

Write-Host "Compilando NXProject-Setup ($buildConfig)..." -ForegroundColor Cyan
& dotnet build (Join-Path $PSScriptRoot "NXProject-Setup\NXProject-Setup.csproj") -c $buildConfig -v q --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build do NXProject-Setup falhou." -ForegroundColor Red
    exit 1
}
$Configuration = $buildConfig

if ($Configuration) {
    $exe = Get-ExePath $Configuration
    if ($null -eq $exe) {
        Write-Host "Executavel NXProject-Setup nao encontrado para $Configuration." -ForegroundColor Red
        Write-Host "Rode antes: dotnet build NXProject-Setup\NXProject-Setup.csproj -c $Configuration" -ForegroundColor DarkGray
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
        Write-Host "Nenhuma build NXProject-Setup encontrada em Debug ou Release." -ForegroundColor Red
        Write-Host "Rode antes: dotnet build NXProject-Setup\NXProject-Setup.csproj" -ForegroundColor DarkGray
        exit 1
    }
}

Write-Host "Iniciando NXProject-Setup..." -ForegroundColor Cyan
& $exe
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0 -and $null -ne $exitCode) {
    Write-Host "App encerrou com codigo $exitCode." -ForegroundColor Yellow
}
