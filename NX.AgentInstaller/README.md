# NX.AgentInstaller

Biblioteca compartilhada pelo **NXPilot** e pelo **NXProject** para instalar e localizar os agentes de IA no Windows: **Claude Code** e **Codex CLI**.
Não tem dependências.

## Modos

| Modo | Como funciona | Quando usar |
|---|---|---|
| **Interno** (`InstallMode.Internal`) | O próprio .NET baixa o binário, confere o **SHA-256** publicado pelo fornecedor e instala nas **mesmas pastas do instalador oficial**. Não roda nenhum PowerShell. | Padrão. Também é a saída quando o antivírus bloqueia script. |
| **Script** (`InstallMode.Script`) | Roda o instalador oficial (`claude.ai/install.ps1` ou `chatgpt.com/codex/install.ps1`) numa janela do PowerShell **visível**. | Quando você prefere o caminho oficial ou o formato de distribuição mudou. |

### O que o modo interno faz

- **Claude Code:** obtém a versão em `downloads.claude.ai/claude-code-releases/latest`, pega o checksum no `manifest.json`, baixa o `claude.exe` e confere o SHA-256. Depois roda `claude.exe install <latest|versão>`, o passo oficial que monta `~/.local/bin`.
- **Codex:** obtém a versão em `releases.openai.com/codex` (ou no GitHub, se ele estiver fora do ar). Baixa o `codex-package_SHA256SUMS` e o **pacote completo** (`codex.exe`, sandbox do Windows, `rg`, command-runner), confere o SHA-256 e extrai em `~/.codex/packages/standalone/releases/<versão>`. Por fim, cria as junções `current` e `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin`, como o `install.ps1` oficial.

## Versão

- `VersionChoice.Latest`: instala a última versão publicada. O agente continua se atualizando sozinho.
- `VersionChoice.Tested`: instala a versão testada pelo produto (`AgentInstallOptions.TestedClaudeVersion` e `TestedCodexVersion`). Se essa versão **não existir mais**, a biblioteca instala a última e avisa (`FellBackToLatest`). Isso evita o erro de tentar instalar uma versão retirada.

Cada produto pode informar as versões que **ele** testou:

```csharp
var installer = new AgentInstaller(new AgentInstallOptions { TestedCodexVersion = "0.162.0" });
var r = await installer.InstallAsync(AgentCli.Codex, InstallMode.Internal, VersionChoice.Tested,
                                     new Progress<string>(Console.WriteLine));
Console.WriteLine(r.Message);   // "Codex 0.162.0 instalado." ou o aviso de queda para a última

var claude = AgentLocator.Find(AgentCli.ClaudeCode);   // PATH, instalador oficial, NXProject, extensão do VS Code
```
