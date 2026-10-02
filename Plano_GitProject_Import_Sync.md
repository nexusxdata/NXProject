# Plano — Importar e Sincronizar cronograma com GitHub Projects

> Documento de desenho. Nada aqui está implementado: escolher **GitProject** na tela de
> importação ou na confirmação do Sincronizar avisa e volta para o Azure DevOps.
>
> Público: quem for implementar o conector. Complementa o [Plano_GitProject.md](Plano_GitProject.md),
> que trata do TaskBoard — aqui o assunto é o **cronograma**: trazer a hierarquia para dentro do
> NX e devolver o planejamento para o servidor.

## O que o import faz hoje (e que precisa continuar valendo)

A importação do DevOps não é uma cópia de lista: ela monta um cronograma. Em ordem:

1. **Descobre a hierarquia** a partir de um work item raiz (Project/EPIC/Feature), descendo até
   Story e, opcionalmente, Task.
2. **Lê os campos** de planejamento: HH Estimado, Data_Inicio, Data_Fim, % alocação, % conclusão,
   responsável, estado, tags, sprint.
3. **Ordena pelos ranks do backlog** (`StackRank`/`BacklogPriority`) — item sem rank recebe um
   calculado na posição recebida, e isso vira aviso no relatório.
4. **Calcula datas** com o calendário do projeto: fila por pessoa, primeira Story ancorando na
   sprint, HH = 0 virando marco.
5. **Resolve predecessoras**, inclusive as virtuais (mesma pessoa, uma depois da outra).

O sync devolve o que mudou, com três proteções que não podem sumir: **`Sync_version`** (alguém
mexeu depois de você), **`Adm_NX`** (quem pode sincronizar) e a recusa de **nome duplicado sob o
mesmo pai** — sem ela, a próxima importação não sabe qual item é qual.

## O que muda com o GitHub Projects

| Peça do import/sync | DevOps | GitHub Projects | Impacto |
|---|---|---|---|
| Raiz e descida | hierarquia de work items | **sub-issues** | precisa paginar por GraphQL; limite ~8 níveis / ~100 filhas |
| Campos de planejamento | campos do work item | campos **do projeto** | o valor vive no board, não na issue |
| Ordem do backlog | `StackRank` / `BacklogPriority` | posição na view | **sem campo numérico** para gravar a ordem |
| Sprint | Iteration Path (início/fim) | campo Iteration | datas podem não existir |
| Concorrência | `Sync_version` + `Sync_Name` | não há equivalente | precisa de campo próprio no projeto |
| Quem pode sincronizar | `Adm_NX` (Identity) | permissão do repositório/projeto | regra muda de lugar |
| Criar item sob o pai certo | tipos validados pelo servidor | nada valida | validação passa a ser do NX |

## Os níveis: o GitHub não tem essa hierarquia

**Não existe** no GitHub nada equivalente a Project → EPIC → Feature → Story → Task. O que há:

- **Issue Types** — tipos de issue definidos na **organização** (conta pessoal não tem). Vêm três
  de fábrica (Task, Bug, Feature) e dá para criar os seus: `Project`, `EPIC`, `Feature`, `Story`,
  `Task`. É rótulo: **nada valida** qual tipo pode ser pai de qual.
- **Sub-issues** — o aninhamento em si, com limites de ~8 níveis e ~100 filhas por pai.
- **Campo single-select do projeto** — alternativa quando não há organização: um campo "Nível"
  com os cinco valores. Funciona em qualquer conta, mas o valor vive no board, não na issue.

Ou seja: a hierarquia é **convenção**, e quem a sustenta passa a ser o NXProject.

### E o "Work Item Project" acima do EPIC

Esse nível é uma personalização sua no DevOps — um tipo de work item que se comporta como um EPIC
de nível acima, onde moram a data de início do projeto e o `Adm_NX`. No GitHub ele seria só mais
um valor de tipo (`Project`), com o mesmo não-controle dos demais.

Vale registrar que, **hoje, nem no DevOps esse nível é configurável**: o NXProject reconhece os
níveis por nome, em código
([TfsImportService.IsBoardProjectType](NXProject.Shared/Services/TfsImportService.cs)), aceitando
apenas `project`/`projeto`, `epic`/`epico`, `feature`/`funcionalidade`. Quem batizou o tipo de
"Iniciativa", "Programa" ou "Produto" fica de fora sem mexer no código.

### Proposta: mapa de níveis configurável (serve aos dois mundos)

Criar uma configuração de **níveis** que o import e o sync passem a consultar, no lugar dos nomes
fixos:

| Nível do NX | Como é identificado | Exemplo DevOps | Exemplo GitHub |
|---|---|---|---|
| Project (raiz) | nome do tipo | `Work Item Project` | Issue Type `Project` ou Nível = `Project` |
| EPIC | nome do tipo | `Epic` | Issue Type `EPIC` |
| Feature | nome do tipo | `Feature` | Issue Type `Feature` |
| Story | nome do tipo | `User Story` | Issue Type `Story` |
| Task | nome do tipo | `Task` | Issue Type `Task` |

Regras que vêm junto:

1. **Um nível pode ser desligado.** Time sem o nível Project começa no EPIC; time sem Task
   trabalha no nível Story (que o cronograma já trata como folha).
2. **A ordem dos níveis é a validação.** Pai tem de ser o nível imediatamente acima — é o que o
   DevOps faz por nós e que no GitHub teremos de fazer sozinhos. A mensagem de recusa já existe
   ("Task só grava sob Story") e passa a valer para qualquer par.
3. **Onde configurar:** mesma lógica dos campos — a regra mora numa classe única (ao lado do
   `NxDevOpsFieldCatalog`), a tela de configuração do NXProject edita, e o NXProject-Setup
   detecta o que existe no servidor e sugere o mapeamento. No GitHub, "detectar" é listar os
   Issue Types da organização (ou os valores do campo Nível).
4. **Exportável no mesmo `.json`** do Passo 4, para o GP configurar uma vez e distribuir.

Esse item deixa de ser só uma necessidade do GitHub: ele **corrige uma limitação atual do
conector do DevOps**, onde o nome do tipo está preso no código.

## Desenho proposto

### Import

- **Entrada:** URL do projeto (`https://github.com/orgs/<org>/projects/<n>`) ou `owner/repo` +
  número do projeto, no lugar de organização + Team Project.
- **Consulta única em GraphQL** trazendo itens, campos do projeto e sub-issues, paginando por
  `endCursor`. Uma chamada por página, não uma por item — o board do DevOps já aprendeu essa
  lição com o `workitemsbatch`.
- **Mapa de campos**: a mesma ideia do `NxDevOpsFieldCatalog`, mas apontando para **campos do
  projeto** por nome (`HH Estimado`, `Data_Inicio`, …). O detector de campos vira "existe este
  campo neste projeto?", e o criador (hoje no NXProject-Setup) passa a criar **campo de projeto**,
  não campo de processo — o que, aliás, **não exige ser administrador da organização**.
- **Ordem**: sem campo de rank, a ordem vem da posição do item na view. Guardar essa posição
  como rank calculado, exatamente como já fazemos para item sem `StackRank`, e avisar no
  relatório que a ordem não é um campo e pode mudar sozinha.
- **Tipo do item** (EPIC/Feature/Story/Task): por **Issue Type** (se a organização tiver) ou por
  um campo single-select "Nível". A importação **recusa** hierarquia incoerente (Task sob Feature,
  por exemplo) com a mesma mensagem que o sync já usa, porque aqui ninguém valida por nós.

### Sync

- **Concorrência:** criar no projeto os campos `Sync_version` (número) e `Sync_Name` (texto) e
  usar a mesma regra de hoje. Sem eles, o sync deve **recusar-se a gravar** em item alterado por
  outra pessoa — melhor travar do que sobrescrever em silêncio.
- **Permissão:** `Adm_NX` não tem equivalente. Duas saídas: um campo de texto no projeto com o
  nome do time autorizado (fraco, mas igual ao de hoje), ou confiar na permissão de escrita do
  próprio GitHub (mais honesto, menos controle fino).
- **Ordem:** gravar a ordem significa mover o item na view (mutation de posição). É a parte mais
  frágil do plano: a API de reordenação é limitada e muda com o tempo.
- **Criação de item:** criar a issue, aplicar o tipo, ligar como sub-issue do pai e preencher os
  campos do projeto — quatro chamadas onde o DevOps faz uma. Em caso de falha no meio, remover o
  que foi criado ou deixar marcado como incompleto; não deixar órfão.

## Onde se escolhe o destino

A escolha "este cronograma trabalha contra o quê" é do **projeto**, não da tela: ela mora no
**Portfólio de Projetos**, por projeto, com os valores **Default · DevOps · GitProject · Projeto
Local**. Os combos do TaskBoard, do Importar e do Sincronizar passam a nascer nesse valor, e
continuam servindo para experimentar sem mexer no cadastro. O desenho está em
[Plano_Projeto_Local.md](Plano_Projeto_Local.md#o-destino-é-escolhido-no-portfólio-não-no-board).

## O NXProject-Setup precisa acompanhar

O instalador é hoje a porta de entrada: é nele que uma organização nova detecta o que falta e
**cria** o que o NXProject precisa. Com um segundo servidor, ele deixa de poder assumir "DevOps"
e passa a ter três responsabilidades.

### 1. Combo de destino na janela "Configurar campos"

A mesma escolha que já existe no TaskBoard, no Importar e no Sincronizar: **Azure DevOps** (padrão)
ou **GitProject**. Ela muda o que a janela faz em cada botão:

| | Azure DevOps | GitProject |
|---|---|---|
| Conexão | organização + projeto + PAT | URL do projeto (`orgs/<org>/projects/<n>`) + token |
| Detectar | campos do processo, por tipo de work item | campos **do projeto** + Issue Types da organização |
| Criar | Process API (exige **Project Collection Administrator**) | campo de projeto — **não exige administrador da organização** |
| Exportar/importar `.json` | igual | igual, com o destino gravado no arquivo |

Vale destacar a linha da criação: no GitHub o campo é **do projeto**, e criá-lo é permissão de
quem administra aquele projeto. Toda a dor de PAT com escopo *manage* e de Project Collection
Administrator — que o Passo 4 hoje precisa detectar, explicar e contornar — simplesmente não
existe desse lado.

### 2. Configurar os NÍVEIS, não só os campos

Hoje o Passo 4 cuida de campos. Com o mapa de níveis
(ver [Os níveis](#os-níveis-o-github-não-tem-essa-hierarquia)), ele passa a cuidar também da
hierarquia — e isso vale para os **dois** destinos:

- **Detectar:** listar os tipos de work item do processo (DevOps) ou os Issue Types da organização
  / valores do campo Nível (GitHub) e **sugerir** o mapeamento para os cinco níveis do NX:
  Project, EPIC, Feature, Story, Task.
- **Casar por nome, com apelidos**, como já fazemos com campos: `Epic`/`Épico`, `Feature`/
  `Funcionalidade`, `User Story`/`Story`, e o `Work Item Project` da sua instalação.
- **Avisar o que falta.** Sem o nível Project, o NX funciona começando no EPIC — mas aí o
  `Adm_NX` e a data de início do projeto não têm onde morar, e a tela deve dizer isso.
- **Criar o que falta**, quando o servidor permitir: no GitHub, criar o Issue Type (ou o campo
  Nível com os valores) é barato; no DevOps, criar tipo de work item é mexer no processo — a
  recomendação é **detectar e orientar**, não criar tipo automaticamente.

### 3. Dizer o que ainda impede rodar o NXProject

O Passo 4 hoje responde "faltam N campos". Com níveis e dois destinos, a resposta útil passa a
ser uma lista de pendências em ordem de impacto, por exemplo:

```
✔ Níveis: Project → EPIC → Feature → Story → Task mapeados
✖ Campo Data_Inicio não existe na Story  → criar, ou usar o Start Date de fábrica
⚠ Sync_version ausente no EPIC           → sincronização sem controle de concorrência
```

É a diferença entre "detectei campos" e "este servidor está pronto para o NXProject".

### Ordem sugerida para o Setup

1. Combo de destino + conexão do GitHub (só **detectar**, sem criar nada).
2. Mapa de níveis: detectar, sugerir e salvar na configuração — primeiro no DevOps, onde já há
   dado real para testar, e onde isso **corrige a limitação atual** dos nomes presos no código.
3. Criação de campo de projeto no GitHub.
4. Relatório de prontidão unificado (o bloco acima), igual para os dois destinos.

## Ordem de implementação sugerida

1. **Import somente leitura**, sem tocar em nada: já permite abrir um cronograma do GitHub e ver
   o Gantt. É aqui que se descobre se a modelagem de campos e tipos aguenta o uso real.
2. **Sync de campos** (HH, datas, responsável, estado) em itens que já existem — sem criar nem
   reordenar. Risco baixo, valor alto.
3. **Criação de itens** com validação de hierarquia no NX.
4. **Ordem do backlog**, por último, com a expectativa explícita de que pode não ficar igual.

## Riscos — para analisar antes de decidir

Cada linha tem o que fazer para **descobrir cedo**: o objetivo é que nenhum destes vire surpresa
no meio da implementação. Severidade: 🔴 pode inviabilizar · 🟠 muda o escopo · 🟡 conviver com aviso.

| # | Risco | Por que importa | Como descobrir cedo | Saída se acontecer |
|---|---|---|---|---|
| 1 | 🔴 **A ordem do backlog não volta** | ordem é planejamento, não enfeite: o cronograma do NX e a view do GitHub divergem | protótipo de 1 dia que reordena 3 itens por mutation e relê a view | assumir a ordem como "só leitura" no GitHub e avisar no relatório de sync |
| 2 | 🔴 **Hierarquia inválida criada fora do NX** | ninguém valida tipo de pai no GitHub; a importação seguinte traz Task sob Feature | importar um projeto real de um time que já usa sub-issues | recusar na importação (como o sync já faz) e listar os itens a corrigir |
| 3 | 🟠 **Campo de projeto não acompanha a issue** | mover a issue de projeto apaga HH e datas, em silêncio | mover uma issue entre dois projetos no teste | documentar como limitação; detectar item sem campo e avisar no import |
| 4 | 🟠 **Sem `Sync_version` nativo** | duas pessoas gravando o mesmo item, sem conflito detectado | tentar o cenário de duas gravações concorrentes | criar campos próprios no projeto; sem eles, **recusar** gravar item alterado |
| 5 | 🟠 **`Adm_NX` sem equivalente** | hoje é o NX que decide quem sincroniza | definir com você qual regra vale no GitHub | usar a permissão de escrita do próprio GitHub e abrir mão do controle fino |
| 6 | 🟠 **Issue Types só em organização** | conta pessoal fica sem os níveis | testar numa conta pessoal | cair para o campo "Nível" do projeto (já previsto no mapa de níveis) |
| 7 | 🟡 **Limites de sub-issues (~8 níveis, ~100 filhas)** | EPIC grande estoura o limite de filhas | medir no maior projeto que você tiver | avisar na importação e sugerir quebrar o item |
| 8 | 🟡 **GraphQL muda** | a API de Projects v2 ainda evolui | fixar a versão do schema usada e ter teste de contrato | isolar tudo atrás do provedor, para o conserto ser num arquivo só |
| 9 | 🔴 **Dois conectores para manter** | cada regra nova passa a ter dois lugares para errar — é o risco que mais custa com o tempo | contar quantas regras de negócio hoje moram dentro do `TfsImportService` | extrair o provedor **antes** (Fase 1) e cobrir com testes o que for comum |
| 10 | 🟠 **Mapa de níveis mal configurado** | o NX monta a hierarquia errada e o cronograma sai torto | detectar e **mostrar** o mapa antes de importar, no Setup | bloquear a importação enquanto houver nível ambíguo ou sem mapeamento |

### Riscos do lado do NXProject-Setup

| # | Risco | Como descobrir cedo | Saída |
|---|---|---|---|
| 11 | 🟠 **Criar campo de projeto no GitHub exige permissão que nem todo usuário tem** | testar com uma conta sem direito de administrar o projeto | mesma solução de hoje: detectar antes, explicar e indicar quem pode |
| 12 | 🟡 **O `.json` exportado passa a ter destino e níveis** | versionar o arquivo (`Version`) desde já | ler arquivo antigo sem destino como "Azure DevOps" |
| 13 | 🟡 **Detectar níveis pode confundir tipos parecidos** (`Feature` × `Funcionalidade` × `Épico`) | rodar o detector nas organizações que você já conhece | pedir confirmação humana no mapeamento; nunca assumir sozinho |

### O que decidir ANTES de escrever código

1. O GitHub é **alvo de produto** ou experimento? (muda quanto se investe na Fase 1)
2. Ordem do backlog: **requisito** ou "bom ter"? (o risco 1 depende disso)
3. Quem manda na permissão de sincronizar no GitHub? (risco 5)
4. O mapa de níveis entra **primeiro no DevOps** (corrigindo os nomes presos no código) ou só
   junto com o GitHub?

