# Executar como Administrador
# Cria política WDAC suplementar que permite executáveis da pasta NXProject

$PolicyName = "NXProject-Allow"
$PolicyFile = "$env:TEMP\NXProject-Allow.xml"
$BinPolicy  = "$env:TEMP\NXProject-Allow.bin"
$DeployDir  = "$env:SystemRoot\System32\CodeIntegrity\CiPolicies\Active"

Write-Host "Criando politica WDAC suplementar para NXProject..." -ForegroundColor Cyan

# Cria política base permitindo o caminho
$xml = @'
<?xml version="1.0" encoding="utf-8"?>
<SiPolicy xmlns="urn:schemas-microsoft-com:sipolicy" PolicyType="Supplemental Policy">
  <VersionEx>10.0.0.0</VersionEx>
  <PlatformID>{2E07F7E4-194C-4D20-B96C-134F0E5C6C65}</PlatformID>
  <Rules>
    <Rule>
      <Option>Enabled:Unsigned System Integrity Policy</Option>
    </Rule>
    <Rule>
      <Option>Enabled:Advanced Boot Options Menu</Option>
    </Rule>
    <Rule>
      <Option>Enabled:UMCI</Option>
    </Rule>
  </Rules>
  <EKUs/>
  <FileRules>
    <Allow ID="ID_ALLOW_NXPROJECT" FriendlyName="NXProject pasta" FilePath="C:\Users\carmo\Projetos\NXProject\*"/>
  </FileRules>
  <Signers/>
  <SigningScenarios>
    <SigningScenario Value="12" ID="ID_SIGNINGSCENARIO_UMCI" FriendlyName="User Mode">
      <ProductSigners>
        <FileRulesRef>
          <FileRuleRef RuleID="ID_ALLOW_NXPROJECT"/>
        </FileRulesRef>
      </ProductSigners>
    </SigningScenario>
  </SigningScenarios>
  <UpdatePolicySigners/>
  <CiSigners/>
  <HvciOptions>0</HvciOptions>
  <BasePolicyID>{A244370E-44C9-4C06-B551-F6016E563076}</BasePolicyID>
  <PolicyID>{D8F07D7B-2B3C-4F1A-8C3E-5A6B9C0D1E2F}</PolicyID>
</SiPolicy>
'@

$xml | Out-File -FilePath $PolicyFile -Encoding UTF8

# Compila o XML em binário .bin
try {
    ConvertFrom-CIPolicy -XmlFilePath $PolicyFile -BinaryFilePath $BinPolicy
    Write-Host "Politica compilada com sucesso." -ForegroundColor Green
} catch {
    Write-Host "Erro ao compilar politica: $_" -ForegroundColor Red
    exit 1
}

# Copia para o diretório de políticas ativas
$destFile = Join-Path $DeployDir "NXProject-Allow.cip"
try {
    Copy-Item -Path $BinPolicy -Destination $destFile -Force
    Write-Host "Politica implantada em: $destFile" -ForegroundColor Green
} catch {
    Write-Host "Erro ao implantar politica: $_" -ForegroundColor Red
    Write-Host "Tente copiar manualmente: $BinPolicy -> $destFile" -ForegroundColor Yellow
    exit 1
}

# Refresca políticas sem reboot (quando possível)
try {
    Invoke-CimMethod -Namespace root\Microsoft\Windows\CI -ClassName PS_UpdateAndCompareCIPolicy `
        -MethodName Update -Arguments @{ FilePath = $destFile } -ErrorAction SilentlyContinue
    Write-Host "Politica aplicada sem reboot." -ForegroundColor Green
} catch {
    Write-Host "Politica sera aplicada no proximo reboot." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Pronto! A pasta C:\Users\carmo\Projetos\NXProject esta liberada." -ForegroundColor Cyan
Write-Host "Se o NX ainda nao abrir, reinicie o computador." -ForegroundColor Yellow
