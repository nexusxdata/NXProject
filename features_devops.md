# Azure DevOps no NXProject — o que faz e as decisões por trás

> Par do [features_gitproject.md](features_gitproject.md). Aqui está o conector que o NXProject
> tem desde o começo: o que ele lê, o que devolve, e as regras que não são óbvias no código —
> especialmente as que existem para **não** estragar dado de quem está do outro lado.
>
> O passo a passo de **uso** está em dois lugares, e eles não se repetem: no aplicativo, em
> **Ajuda → Guia de funcionalidades** (grupo *Azure DevOps*), e no
> [README](README.pt-BR.md#configurar-o-azure-devops). Este documento é o complemento para quem
> vai **mexer** no conector: as regras que o código aplica e o motivo delas.

## Funcionalidades e estado

| Funcionalidade | Estado | Observação |
|---|---|---|
| Importar hierarquia a partir de um work item raiz | **Pronto** | Project → Epic → Feature → Story → Task, com Task opcional |
| Ler planejamento | **Pronto** | HH estimado, HH realizado, datas, % de alocação e conclusão, responsável, estado, tags, sprint |
| Ordem do backlog | **Pronto** | `StackRank` (Agile/CMMI) ou `BacklogPriority` (Scrum), conforme o processo |
| TaskBoard | **Pronto** | Estados, arrastar card, criar item, mover Task de Story, anexos, comentários |
| Sincronizar de volta | **Pronto** | Campos, estado, criação de item, reparent, ordem, marcos |
| Auditoria de BLOCK | **Pronto** | Reconstruída das revisões (`/updates`) do work item |
| Anexos | **Pronto** | API de anexo do próprio work item |
| Setup: detectar e criar campos | **Pronto** | `NXProject-Setup`, Passo 4 |
| Setup: relatório de níveis | **Pronto** (1.2) | diz se os cinco níveis têm tipo e quais tipos ficaram sem nível |
| Mapa de níveis configurável | **Pronto** (1.2) | antes os nomes de tipo estavam presos no código |
| Virtualização do board | **Falta** | o TaskBoard não virtualiza; o custo cresce com o número de linhas |
| Teste de chave de string ausente | **Falta** | o teste atual pega chave duplicada, não faltando |

## Regras que existem para não estragar dado alheio

Estas são as decisões que mais custam quando alguém "simplifica" sem saber por quê — cada uma
tem teste em `NXTestUnit`:

1. **Início só se o TFS já tiver início; fim só se o item estiver encerrado.** O NXProject
   planeja, mas quem executa é o time: sobrescrever a data real com a planejada apagaria o que
   aconteceu de verdade.
2. **Fim gravado é o INCLUSIVO.** O motor guarda fim exclusivo; gravar esse faria toda atividade
   terminar um dia depois.
3. **Concorrência por `Sync_version`.** Item alterado por outra pessoa depois da sua importação
   vira conflito e **não** é gravado — nem os campos que mudaram do seu lado. Resolver é decisão
   humana, na tela de conflitos.
4. **`Adm_NX` é checado AO VIVO**, relendo o grupo no work item Project; o flag gravado no
   arquivo pode estar velho. Campo vazio libera, e falha técnica (rede, escopo) **não bloqueia**:
   transformar falha de leitura em bloqueio de trabalho seria pior que o problema.
5. **Nome duplicado sob o mesmo pai é recusado.** Sem isso, a próxima importação não sabe qual
   item é qual.
6. **ID interno duplicado bloqueia a sincronização**, pelo mesmo motivo — e o erro nomeia a
   atividade.
7. **Task só grava sob Story.** O DevOps recusaria de qualquer forma; recusar antes dá mensagem
   melhor. A exceção é o Marco-Devops, que pode ficar fora de Story.
8. **`EPIC_TYPE = EPIC_GROUP`** é a única exceção à hierarquia: um EPIC que agrupa EPICs (as
   Melhorias abertas por sprint, por exemplo). Acima dele só o Work Item Project, abaixo só
   EPICs, e grupo não aninha em grupo. No cronograma ele consolida como o Work Item Project; no
   TaskBoard aparece acima do nome do EPIC, com um checkbox que suprime todos de uma vez.
9. **Rank encaixa ENTRE os vizinhos** (`BacklogOrderService.NextRank`), em vez de reescrever o
   backlog inteiro: mexer num item não muda o rank de nenhum outro.

## Idioma das mensagens

As telas usam `AppStrings` e os arquivos `Strings.*.xaml`, que vivem no **NXProject.Community**.
Os serviços estão no **NXProject.Shared** e não enxergam nada disso — foi por essa fronteira que
todo aviso de importação e de sincronização acabou fixo em português, inclusive para quem usa o
aplicativo em inglês.

O `NxText` fecha a fronteira, com as duas versões lado a lado na própria chamada:

```csharp
NxText.T("#{0} não está neste board.", "#{0} is not on this board.", numero)
```

Não é um catálogo de chaves de propósito: mensagem de serviço nasce junto do código que a produz,
e separá-la num arquivo distante foi exatamente o que fez as duas se perderem. Com o texto ao
lado, quem muda a regra vê as duas versões e não tem como esquecer uma. Chave continua sendo o
certo para a interface, que tem milhares delas.

**Estado:** os serviços do GitProject estão convertidos. O `TfsImportService` (conector do Azure
DevOps) ainda tem ~47 mensagens fixas em português — é a próxima leva.

## Processos: Agile é o que foi testado

O NXProject foi exercitado **em produção com o processo Agile**. Scrum e CMMI são **tratados no
código**, mas não foram rodados contra um servidor real — e a diferença entre as duas afirmações
importa:

| Peça | Como os três processos são atendidos |
|---|---|
| Ordem do backlog | Scrum grava `BacklogPriority`; Agile e CMMI, `StackRank`. E, antes disso, vale o campo que o work item **realmente tem** |
| Tipo da Story | `User Story` (Agile), `Product Backlog Item` (Scrum) e `Requirement` (CMMI) são nomes de fábrica do mapa de níveis |
| Estados | Abertos: `New`, `Active`, `In Progress`, `To Do`, `Approved` (Scrum), `Proposed` (CMMI), `Committed`. Encerrados: `Closed`, `Resolved`, `Done`, `Completed` |
| Colunas do TaskBoard | Saem dos estados encontrados nos itens, ordenados por uma lista canônica que já inclui o vocabulário do Scrum. Estado desconhecido vai para o fim, nunca some |
| HH, datas, percentuais | São campos **próprios** do NXProject, criados pelo Setup — não dependem do processo |

**O ponto de atenção conhecido:** no padrão de nomes **EN**, o NXProject usa os campos nativos
(`Original Estimate`, `Completed Work`). Eles existem na Task do Agile e do CMMI; no **Scrum** a
Task de fábrica traz `Remaining Work`, e os outros dois podem não existir. Com o padrão **PT**
(campos próprios) isso não acontece, e o "Detectar campos" avisa antes de qualquer importação.

Como nomes de tipo e de campo são configuráveis, adaptar a outro processo é **configuração, não
alteração de código**.

## Campos: o catálogo é a fonte única

`NxDevOpsFieldCatalog` descreve cada conceito do NXProject (HH, datas, percentuais, sync,
aprovação, grupo administrador, bloqueio) com nome em português, nome em inglês, tipos de work
item onde vale e o campo **nativo** equivalente, quando existe.

- **Padrão PT** usa campos próprios do NXProject (`Data_Inicio`, `HH Estimado`).
- **Padrão EN** usa o campo **nativo** do processo sempre que o tipo tiver um (`Start Date`,
  `Finish Date`, `Original Estimate`) e o nome em inglês quando não houver.
- A regra que evita o estrago: no padrão inglês, **só vira nativo o que TEM nativo naquele tipo**.
  Sem isso, a Task ficaria com duas datas de início — a nativa e a do NXProject.

O mesmo catálogo serve ao NXProject e ao NXProject-Setup. Já houve três cópias divergentes dessa
regra entre os dois, e cada cópia virou defeito.

## Níveis: o nome do tipo é configuração, não código

Até a versão 1.2, os níveis eram reconhecidos por nome fixo no código (`epic`/`epico`,
`feature`/`funcionalidade`, `project`/`projeto`). Quem batizou o tipo de "Iniciativa", "Programa"
ou "Produto" ficava de fora sem recompilar.

Hoje isso é o `NxLevelMap`: nomes aceitos por nível, nível desligável (time sem Project começa no
Epic; sem Task, a folha é a Story) e validação pai→filho pulando o que estiver desligado. Vale
para os dois destinos, e a tela **Configurar Campos e Tags → Níveis** edita e detecta o que existe
no servidor.

O **NXProject-Setup** passou a dizer, junto com os campos, se os cinco níveis estão mapeados e
quais tipos do processo ficaram sem nível — a diferença entre "detectei campos" e "este servidor
está pronto para o NXProject".

## O instalador encaminha, não duplica

A tela "Configurar Campos e Tags" existe **uma vez**, no NXProject. O NXProject-Setup a abre por
linha de comando em vez de ter uma cópia:

```
NXProject.Community.exe --setup            # as duas abas
NXProject.Community.exe --setup=devops      # aba Azure DevOps
NXProject.Community.exe --setup=gitproject  # aba GitProject
```

O aplicativo abre só essa janela e encerra quando ela fecha. Duas telas para a mesma configuração
foi exatamente o que fez o catálogo de campos divergir entre os dois aplicativos — três vezes.

## Permissões: duas, e elas se confundem

| Para quê | O que exige |
|---|---|
| Ler e gravar work item (uso normal do NXProject) | PAT com escopo de work item |
| **Criar campo** no processo (NXProject-Setup) | *Project Collection Administrator* — mexe no processo da organização |

O Passo 4 do Setup detecta a segunda **antes** de tentar criar, e explica qual token usar: é o erro
mais comum de quem instala, e descobrir no meio da criação deixaria metade dos campos no ar.

## O que não será feito, e por quê

| | Motivo |
|---|---|
| **Criar tipo de work item automaticamente** | Mexer no processo da organização é decisão de quem administra, não do instalador. O Setup detecta e orienta |
| **Sincronizar sem `Sync_version`** | Sem a versão não há como saber que outra pessoa mexeu depois. Melhor travar do que sobrescrever em silêncio |

## Diferenças em relação ao GitHub Projects

Resumo do que muda para quem conhece um lado e vai mexer no outro — o detalhe está no
[features_gitproject.md](features_gitproject.md):

| | Azure DevOps | GitHub Projects |
|---|---|---|
| Campos | do work item, por tipo | do **board** (projeto) |
| Hierarquia | validada pelo servidor | **ninguém valida**: quem valida é o NXProject |
| Ordem | campo numérico | posição na view (uma chamada por item, ~2s cada) |
| Concorrência | `Rev` do servidor + `Sync_version` | `updatedAt` espelhado no `Sync_version` |
| Quem pode sincronizar | grupo de identidade no `Adm_NX` | time da organização **ou** lista de logins |
| Anexo | API de anexo do item | arquivo versionado no repositório + link no item |
| Criar campo | exige admin do processo | basta acesso ao projeto |
