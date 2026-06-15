# NXProject Community Distribution

## O que publicar no Git

- `NXProject.Shared`
- `NXProject.Community`
- scripts da Community, como `build-community.ps1`, `run-community.ps1` e `release-community.ps1`

## O que manter fechado

- `NXProject`
- `NXProject.Core`

## Como gerar o pacote para usuario final

1. Execute:
   `.\release-community-new-version.ps1`
2. O pacote sera gerado em:
   `dist\community\NXProject.Community-Release.zip`
3. O `.exe` oficial deve ser gerado via `dotnet publish --self-contained true` para incluir o runtime do .NET no pacote.

## O que vai dentro do ZIP

- executavel `NXProject.Community.exe`
- dependencias da build
- `README-INSTALACAO.txt`

## Aviso sobre o pacote

O `.zip` de distribuicao foi gerado em ambiente com antivirus McAfee. Se houver qualquer duvida sobre o executavel distribuido, ele pode ser gerado novamente com base no codigo-fonte publico deste repositorio.

## Requisito para o usuario final

O pacote oficial e self-contained para Windows x64. O usuario nao precisa instalar o `Microsoft .NET Desktop Runtime 10.0` para abrir o aplicativo pelo `NXProject.Community.exe`.

## Observacao

Se o pacote for gerado manualmente, use `dotnet publish --self-contained true`. Nao use apenas `dotnet build` para o ZIP oficial, porque essa saida depende do runtime instalado na maquina do usuario.
