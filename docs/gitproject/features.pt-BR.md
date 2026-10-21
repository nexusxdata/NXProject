🌐 **Português** | [Read in English](features.md)

# GitHub Projects no NXProject — funcionalidades, estado e decisões

> Este documento começou como dois planos de desenho (`Plano_GitProject.md` e
> `Plano_GitProject_Import_Sync.md`). O conector foi feito: TaskBoard, importação, sincronização,
> ordem do backlog, auditoria de bloqueio, responsável e anexos funcionam contra um board real.
>
> O que sobrou aqui é o que o código **não** conta: o que foi medido em vez de suposto, o que foi
> decidido não fazer e por quê, e onde o resultado contrariou o plano original. Sem isso, a
> próxima pessoa refaz as mesmas medições para chegar às mesmas conclusões.

## Por que existe

O NXProject nasceu falando só com o Azure DevOps. Times que vivem no GitHub — e que não têm
licença nem apetite para o DevOps — precisavam exatamente do mesmo: cronograma com datas
calculadas, alocação de pessoas e um board que respeita a hierarquia do backlog. O GitHub Projects
é gratuito em repositórios públicos e privados, pessoais e de organização; para esses times o
custo de adoção cai a zero.

## Funcionalidades e estado

Tudo abaixo está **pronto** — o que falta e o que não será feito têm seção própria no fim.

| Funcionalidade | Onde |
|---|---|
| Importar (hierarquia por sub-issues, campos do board, sprints, estado, predecessoras por "blocked by") | `GitProjectImportService` |
| Discovery (listar boards, achar o portfólio) | `GitProjectDiscovery` |
| TaskBoard, com as colunas vindas do `Status` do próprio projeto | `GitProjectBoardService` |
| Salvar do board (estado, título, HH, datas, responsável, labels, sprint, mover de pai) | `GitProjectBoardWriter` |
| Sincronizar (campos, estado da issue, criação de item, responsável, predecessoras como "blocked by") | `GitProjectSyncService` |
| Ordem do backlog | `BacklogOrderService` (regra) + `INxOrderWriter` (como gravar em cada destino) |
| Concorrência: `Sync_version` espelhando a revisão do servidor | `GitProjectSyncService.Revision` |
| Quem pode sincronizar (`Adm_NX`): time da org ou lista de logins | `GitProjectAdminService` |
| Auditoria de BLOCK, da timeline da issue | `GitProjectBlockAuditService` |
| Anexos: arquivo versionado no repositório + link no item | `GitProjectAttachmentService` |
| Criar os campos que faltam no board | "Configurar Campos e Tags" → *Criar campos no board* |
| Mapa de níveis configurável, válido nos **dois** destinos | `NxLevelMap` |
| `EPIC_TYPE = EPIC_GROUP`: EPIC que agrupa EPICs (campo do board) | `NxLevelMap.ParentChildOk`, no import e no sync |

## O que foi medido, e não suposto

As sondas ficam fora da suíte normal (elas gravam em servidor de verdade) e devolvem o board
exatamente como o encontraram:

```
dotnet run --project NXTestUnit -c Release -- gitorder   # ordem do backlog
dotnet run --project NXTestUnit -c Release -- gitpoc     # BLOCK, anexos e responsável
```

| Pergunta | O que o plano supunha | O que a medição mostrou |
|---|---|---|
| A ordem do backlog volta? | 🔴 risco alto: "a ordem não volta" | **Volta.** `updateProjectV2ItemPosition` funciona no topo e no meio, e a releitura reflete. O problema não é fragilidade, é **custo**: ~2s por movimento, 98s para reescrever uma fila de 48 itens. Daí gravar a ordem ser um **diff** — só se move o que está fora de lugar |
| Dá para auditar bloqueio? | "talvez nunca fique igual" | **Dá.** `timelineItems` com `LABELED_EVENT`/`UNLABELED_EVENT` devolve label, hora e autor de cada entrada e saída — o mesmo dado do `/updates` do DevOps, noutro formato |
| Dá para anexar arquivo ao item? | anexo "no corpo do comentário" | **Não existe API de anexo de item.** O caminho da tela do GitHub (upload para um CDN) não é automatizável. O substituto é versionar no repositório da issue |
| Dá para ter predecessora? | não havia | **Dá.** A dependência nativa do GitHub ("blocked by", `addBlockedBy`/`removeBlockedBy`) é o par do link Dependency-Reverse do DevOps: "#A blocked by #B" = B é predecessora de A. O sync respeita a mesma chave `SyncPredecessorLinks` e só mexe em bloqueio entre issues **do board** — bloqueio vindo de fora vira aviso no import e nunca é apagado. Medido com ida e volta no board de teste |
| Dá para sincronizar responsável? | "basta a mutation" | A mutation é trivial; o problema é a **identidade**. 6 de 100 contas num repositório público não tinham nome de exibição, e nome não é único. Resolvido guardando o **login** no recurso durante o import |
| Quem alterou por último? | carimbo nosso (`Sync_Name`) | O servidor já sabe: `creator` do valor de campo mais recente, ou `editor` da issue. Melhor que o carimbo — ele só enxergaria gravações feitas pelo próprio NXProject |

## Decisões que mudaram o desenho original

1. **A Fase 1 não foi feita como estava escrita.** O plano pedia extrair `IWorkItemProvider` das
   337 chamadas a `TfsImportService`. O que existe é `INxBoardSource`, uma interface só para o que
   o board consome. O motivo foi risco: reescrever 33 arquivos antes de saber se o conector novo
   valia seria pagar as duas contas adiantado. **Consequência honesta:** import e sync ainda têm
   dois lugares, e esse é o risco que mais custa com o tempo.
2. **O criador de campos ficou no NXProject, não no Setup.** Criar campo no GitHub **não exige
   administrador da organização**, ao contrário do DevOps — então ele cabe na tela de quem usa,
   junto do detector. O Setup segue sendo a porta de entrada do DevOps.
3. **O mapa de níveis saiu antes do resto**, e corrigiu uma limitação do conector do **DevOps**:
   `IsBoardProjectType`, `IsBoardEpicType`, `IsBoardFeatureType`, `IsStoryType` e `IsTaskType`
   liam nomes fixos no código, deixando de fora quem batizou o tipo de "Iniciativa" ou "Produto".
4. **A classificação do GitProject nasce copiando a do DevOps.** Picklist vira single-select com
   os mesmos valores — no GitHub só o single-select tem lista fechada.
5. **O `Adm_NX` ganhou duas formas.** Time só existe em organização; board de conta pessoal não
   tem nenhum. Então o campo aceita `@org/time` **ou** uma lista de logins separada por vírgula.

## O que não será feito, e por quê

| | Motivo |
|---|---|
| **Aba GitProject no NXProject-Setup** | Duplicaria conexão e campos num segundo lugar. O Setup existe para a dor que o GitHub não tem (Process API, *Project Collection Administrator*), e o NXProject já cria os campos do board. Em vez de uma segunda tela, o instalador **chama** esta: `NXProject.Community.exe --setup=gitproject` (botão *Abrir no NXProject*, no Passo 4) |
| **Excluir issue pelo board** | Apagar é irreversível e exige administrador. O Salvar avisa e sugere fechar o item |
| **Prioridade no board do GitHub** | Não é campo de item lá. Quem precisar cria um campo de número no board |

## O que o GitHub obriga a aceitar

- **Campo é do BOARD, não do item.** Mover a issue para outro projeto leva HH e datas embora.
- **Ninguém valida hierarquia.** Task sob Feature é possível pela tela do GitHub; a validação é do
  NXProject — no import vira aviso (o cronograma é onde se arruma), no sync vira recusa do item.
- **Label é do repositório.** Um board que junta issues de vários repos precisa da mesma label em
  cada um — e por isso o NXProject a cria antes de aplicar.
- **Anexo no repositório é versionado para sempre.** Apagar deixa o histórico do Git. Não é lugar
  para dado pessoal — diferente do anexo do DevOps, que some quando removido.
- **Limites de sub-issues:** ~8 níveis e ~100 filhas por pai.

## Token

Classic, com **`repo`** (as issues, inclusive em repositório privado) e **`project`** (ler e gravar
no board). Dois enganos que custaram tempo e estão explicados na própria tela de configuração:

- `repo:invite` é **filho** de `repo` e só trata de convites de colaboração — não dá acesso a
  issue nenhuma. Marcar só ele produz um board que lê os campos e **nenhum** item;
- token *fine-grained* não serve para board de **conta pessoal**: ele não expõe a permissão de
  Projects. Em organização, serve — e ali dá para liberar só *Issues* + *Projects*, sem acesso ao
  código.

## O que ainda falta

- **Virtualização do board** (vale para os três destinos, não só o GitProject).
- **Teste de chave de string ausente**: o teste atual pega chave duplicada, não faltando.
