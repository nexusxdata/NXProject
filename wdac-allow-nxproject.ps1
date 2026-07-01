#Requires -RunAsAdministrator
# Cria e instala uma politica WDAC suplementar que confia em todos os
# arquivos sob C:\Users\carmo\Projetos\NXProject\  sem desativar o SAC.

$ErrorActionPreference = "Stop"

$devPath   = "C:\Users\carmo\Projetos\NXProject"
$policyDir = Join-Path $env:TEMP "NXProjectWDAC"
$xmlPath   = Join-Path $policyDir "NXProjectSupplemental.xml"
$binPath   = Join-Path $policyDir "NXProjectSupplemental.cip"

New-Item -ItemType Directory -Force -Path $policyDir | Out-Null

# GUID unico para esta politica suplementar
$policyId = [System.Guid]::NewGuid().ToString("B").ToUpper()

# BasePolicyID do Smart App Control (GUID fixo da Microsoft)
$sacBaseId = "{A244370E-44C9-4C06-B551-F6016E563076}"

$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<SiPolicy xmlns="urn:schemas-microsoft-com:sipolicy" PolicyType="Supplemental Policy">
  <VersionEx>10.0.0.0</VersionEx>
  <PlatformID>{2E07F7E4-194C-4D20-B96C-1AEF7C91388C}</PlatformID>
  <Rules>
    <Rule><Option>Enabled:Unsigned System Integrity Policy</Option></Rule>
  </Rules>
  <EFSSupported>false</EFSSupported>
  <FileRules>
    <Allow ID="ID_ALLOW_NXPROJECT"
           FriendlyName="NXProject Dev Folder"
           FilePath="${devPath}\*"
           MinimumFileVersion="0.0.0.0"/>
  </FileRules>
  <Signers/>
  <SigningScenarios>
    <SigningScenario Value="12" ID="ID_SIGNINGSCENARIO_UMCI" FriendlyName="UMCI">
      <ProductSigners>
        <FileRulesRef>
          <FileRuleRef RuleID="ID_ALLOW_NXPROJECT"/>
        </FileRulesRef>
      </ProductSigners>
    </SigningScenario>
    <SigningScenario Value="131" ID="ID_SIGNINGSCENARIO_KMCI" FriendlyName="KMCI">
      <ProductSigners/>
    </SigningScenario>
  </SigningScenarios>
  <UpdatePolicySigners/>
  <CISigners/>
  <HvciOptions>0</HvciOptions>
  <BasePolicyID>$sacBaseId</BasePolicyID>
  <PolicyID>$policyId</PolicyID>
</SiPolicy>
"@

Write-Host "Criando politica suplementar WDAC para: $devPath" -ForegroundColor Cyan
$xml | Set-Content -Encoding UTF8 -Path $xmlPath

Write-Host "Convertendo para binario..." -ForegroundColor Cyan
ConvertFrom-CIPolicy -XmlFilePath $xmlPath -BinaryFilePath $binPath

Write-Host "Instalando politica..." -ForegroundColor Cyan
$result = & CiTool --update-policy $binPath 2>&1
Write-Host $result

Write-Host ""
Write-Host "Politica instalada com sucesso!" -ForegroundColor Green
Write-Host "ID: $policyId" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Salve o ID acima para remover depois se necessario:" -ForegroundColor Yellow
Write-Host "  CiTool --remove-policy $policyId" -ForegroundColor Yellow
Write-Host ""
Write-Host "Reinicie o PC para a politica entrar em vigor." -ForegroundColor Cyan
