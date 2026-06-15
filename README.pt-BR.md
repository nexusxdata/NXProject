🌐 **Português** | [Read in English](README.md)

---

# NXProject Community

**Visibilidade gerencial sobre o Azure DevOps — sem mudar nada no fluxo da equipe técnica.**

O NXProject permite que Líderes Técnicos, Scrum Masters, Gerentes de Projeto e Gestores de Negócio enxerguem o cenário real do projeto a partir do Azure DevOps: cronograma, dependências, alocação de pessoas e Gantt — em um aplicativo desktop gratuito para Windows.

A equipe técnica continua trabalhando no Azure DevOps exatamente como antes: rastreabilidade de código, pull requests, pipelines e qualidade de entrega intactos. O NXProject lê esses dados e transforma o backlog em uma visão de planejamento que gestores e líderes conseguem usar para tomar decisões.

---

## O problema que o NXProject resolve

Projetos de TI que usam Azure DevOps têm o backlog organizado, sprints definidas e work items atualizados — mas **a gestão não tem uma visão de cronograma integrada**. Perguntas simples ficam sem resposta rápida:

- Quando essa Feature vai terminar, considerando todas as Stories?
- Qual recurso está sobrecarregado no próximo mês?
- Se essa Story atrasar, o que mais é impactado?
- O projeto vai entregar no prazo?

O NXProject importa a hierarquia do Azure DevOps e transforma esses dados em um cronograma gerenciável, com Gantt, dependências, alocação e alertas de atraso — **sem que a equipe técnica precise mudar nada no seu processo**.

---

## Cada perfil vê o que precisa, sem atrito

A equipe de desenvolvimento segue usando o Azure DevOps como fonte da verdade: commits vinculados, code review, automação de pipeline e rastreabilidade completa permanecem inalterados. O NXProject é uma **camada de leitura e planejamento** sobre esses dados, voltada para quem precisa responder perguntas de prazo, capacidade e risco.

---

## A motivação por trás do NXProject

Depois de mais de 20 anos gerenciando projetos de TI, percebi que sempre existia um conflito silencioso: as ferramentas técnicas eram ótimas para a equipe de desenvolvimento, mas não entregavam a visibilidade que a gestão precisava. As ferramentas de gestão, por sua vez, davam os relatórios e o Gantt que os executivos pediam, mas penalizavam a equipe técnica com processos paralelos, retrabalho e perda de rastreabilidade.

A solução que eu encontrava era sempre um meio-termo insatisfatório — ou a equipe sofria, ou a gestão ficava no escuro.

Depois de muito tempo buscando uma saída, decidi criar algo diferente: uma ferramenta que não obriga ninguém a escolher entre rigor técnico e visibilidade gerencial. Que respeite o fluxo da equipe no Azure DevOps e, ao mesmo tempo, entregue ao gestor o cronograma, as dependências e os alertas que ele precisa para tomar decisões com confiança.

Assim nasceu o NXProject — uma nova forma de gerenciar projetos de TI, onde o técnico e o gerencial caminham juntos, sem atrito e sem concessões.

---

## Download

- [Baixar ZIP do NXProject Community com `.exe` e DLLs](../../releases/latest/download/NXProject.Community-Release.zip)
- [Ver notas da versão e downloads do código-fonte](../../releases/latest)

> O binário foi gerado em ambiente com antivírus McAfee. Se preferir compilar você mesmo, veja as instruções abaixo.

---

## Capturas de tela

![Tela principal do NXProject Community](ScreenShot/Tela01.png)
![Tela com hierarquia e Gantt](ScreenShot/Tela02.png)
![Tela de configuração e acompanhamento](ScreenShot/Tela03.png)
![Tela de importação do TFS / Azure DevOps](ScreenShot/Tela04.png)
![Tela conceitual Azure DevOps Backlog](ScreenShot/Tela05-Azure-DevOps-Backlog.svg)

---

## Para quem é o NXProject

| Perfil | O que o NXProject entrega |
|---|---|
| **Gerente de Projeto** | Cronograma integrado ao backlog, alertas de atraso, visão de dependências |
| **Scrum Master / RTE** | Capacidade por sprint, conflito de alocação, impacto de mudanças de data |
| **Tech Lead** | Visão de Features e Stories com predecessoras e estimativas em horas |
| **PMO** | Consolidação de múltiplos projetos, exportação para MS Project / Excel |

---

## Integração com Azure DevOps

### Do backlog ao cronograma em minutos

O NXProject importa a hierarquia completa do seu projeto diretamente do Azure DevOps:

```
Project → Epic → Feature → Story
```

Cada Story vira uma linha do cronograma com data de início, duração calculada em dias úteis, responsável e sprint — tudo extraído dos campos que seu time já preenche no DevOps.

### Lista de Projetos DevOps

Gerencie múltiplos projetos DevOps em um arquivo compartilhado entre toda a equipe. Cada projeto tem nome e ID raiz; ao importar, basta selecionar o projeto da lista, sem precisar lembrar o ID manualmente.

### O que é lido automaticamente

- **Hierarquia**: `Project → Epic → Feature → Story` via links `Child`
- **Estimativas**: campo `HH Estimado` → duração em dias úteis no calendário do projeto
- **Datas**: `Data_Inicio` e `Data_Fim` quando já definidas no DevOps
- **Responsável**: `System.AssignedTo` → recurso do projeto
- **Sprint**: `System.IterationPath` → associação com sprints do NXProject
- **Ordem do backlog**: `Microsoft.VSTS.Common.StackRank`
- **Bloqueios**: Tasks filhas com tag `Block` marcam a Story como bloqueada
- **Estado**: Stories `Closed`/`Resolved` com Tasks filhas ainda em aberto são sinalizadas e corrigidas automaticamente

### Log de importação

Ao importar, o NXProject gera um relatório com:
- Stories cujo estado foi corrigido automaticamente (ex: Story fechada com Task em aberto)
- Predecessoras que apontam para itens fora do escopo importado
- Avisos e inconsistências para revisão antes de publicar o cronograma

### Sincronização de volta ao DevOps

Após ajustar datas, dependências e estimativas no cronograma, o NXProject sincroniza as alterações de volta para o Azure DevOps: título, descrição, horas, datas, estado, tags, sprint e links de predecessora.

### Abrir work item direto no DevOps

Em qualquer tarefa vinculada, o botão **"Abrir no DevOps ↗"** abre o work item no browser. A janela de vínculo também exibe a lista de Tasks filhas com ID, nome e estado — para referência rápida sem sair do NXProject.

---

## Outras funcionalidades

- **Gráfico de Gantt** interativo com zoom por dia, sprint ou período
- **Dependências entre tarefas** (predecessoras), inclusive entre Stories de Epics diferentes
- **Alocação de recursos**: visão de carga por pessoa e período
- **Health Check do Projeto**: lista tarefas atrasadas e sem responsável
- **Calendário configurável**: feriados, dias úteis, horas por dia
- **Exportação**: MS Project XML, OpenProj, Excel XML, CSV
- **Assistente IA** para sugestão de estrutura de tarefas

---

---

## Compilar a partir do código-fonte

Pré-requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) e [VS Code](https://code.visualstudio.com/download).

```powershell
# Preparar ambiente
.\setup-community-vscode.ps1

# Compilar
.\build-community.ps1 -Configuration Release

# Ou gerar o zip de distribuição
.\release-community.ps1 -Configuration Release
```

O executável será gerado em `NXProject.Community\bin\Release\net10.0-windows\`.

---

## Configurar o Azure DevOps

### Personal Access Token

1. No Azure DevOps, clique no ícone de usuário → **Personal access tokens**
2. Clique em **New Token**
3. Em **Scopes**, selecione **Work Items → Read** (adicione **Write** se quiser sincronizar de volta)
4. Copie o token e cole no campo correspondente na tela de importação do NXProject

O token pode ser salvo localmente cifrado com as credenciais do Windows (DPAPI).

### Campos personalizados

Se o seu processo usa nomes de campo diferentes de `HH Estimado`, `Data_Inicio` ou `Data_Fim`, esses nomes podem ser ajustados na área **Campos (avançado)** da janela de importação.

### Calendário de trabalho

Configure feriados, horas úteis por dia e dias da semana em **Exibir → Calendário...**  
O padrão é 8 horas por dia, segunda a sexta.

---

## Licença e contato

- **Empresa**: Nexus XData Tecnologia Ltda
- **Contato comercial**: `comercial.nexus.xdata@gmail.com`

O NXProject usa modelo **Open Core / licenciamento dual**:

| Edição | Uso |
|---|---|
| **Community (gratuita)** | Uso livre para pessoas físicas e empresas, inclusive uso comercial interno, sem limite de usuários. Redistribuição gratuita permitida mantendo o crédito à Nexus XData. |
| **Comercial / Enterprise** | Sem restrições de revenda ou SaaS, suporte oficial, SLA, módulos exclusivos (impressão/PDF, calendário avançado, integrações com IA). Contate-nos para proposta. |

> Vender, cobrar ou oferecer o NXProject como serviço pago exige licença comercial.

---

## Conte como o NXProject está ajudando o seu projeto

Se o NXProject está sendo usado na sua empresa e está fazendo diferença — seja na visibilidade do cronograma, na gestão de equipe ou na integração com o Azure DevOps — **queremos saber**.

Envie um relato curto para `comercial.nexus.xdata@gmail.com` contando:

- O contexto do projeto (tamanho da equipe, segmento, desafio que tinha)
- O que melhorou depois que passou a usar o NXProject
- Se autoriza, divulgamos o caso como referência para a comunidade

Relatos reais ajudam a priorizar melhorias, atraem novos colaboradores e mostram para outras equipes que o produto funciona na prática. **Sua experiência pode ajudar outros projetos.**
