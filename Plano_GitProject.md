# Plano — TaskBoard com GitHub Projects

> Documento de desenho. Nada aqui está implementado: o combo **GitProject** no TaskBoard avisa
> que o caminho é um estudo e volta para o Azure DevOps.
>
> Público: quem for implementar o conector e quem precisa decidir se vale a pena.

## Por que existiria

O NXProject hoje só conversa com o Azure DevOps. Times que vivem no GitHub — e que não têm
licença nem apetite para o DevOps — ficam de fora, mesmo precisando exatamente do que o NX faz:
cronograma com datas calculadas, alocação de pessoas e um board que respeita a hierarquia do
backlog.

GitHub Projects (Projects v2) é gratuito em repositórios públicos e privados, pessoais e de
organização. Para esses times, o custo de adoção do NX cairia para zero.

## O que o GitHub tem, o que falta

| Conceito no NX | Azure DevOps | GitHub Projects |
|---|---|---|
| Hierarquia Project → EPIC → Feature → Story → Task | tipos de work item, validados pelo servidor | **sub-issues** (até ~8 níveis, ~100 filhas por pai) + **Issue Types** na organização; a hierarquia entre tipos **não é validada** |
| Campos (HH, datas, %) | campos do work item, por tipo | **campos do projeto** (board), não da issue |
| Sprint | Iteration Path, com início e fim | campo **Iteration** do projeto, mais simples |
| Ordem do backlog | `StackRank` / `BacklogPriority` | posição na view do projeto |
| Histórico de mudanças | `/updates`, paginado | timeline de eventos, outro formato |
| Anexos | API de anexos do work item | upload no corpo do comentário |
| API | REST 7.1, JSON-Patch | **GraphQL** |

Três consequências que mudam o produto, não só o código:

1. **A hierarquia passa a ser responsabilidade do NX.** No DevOps o servidor recusa uma Task
   pendurada numa Feature — é o que o teste *"Sync TFS: Task só grava sob Story"* garante. No
   GitHub nada impede, e qualquer pessoa pode bagunçar pela tela do próprio GitHub. O NX teria
   de validar na importação e decidir o que fazer com o que já veio inválido.
2. **Campos são do board, não do item.** `HH Estimado` e `Data_Inicio` viram campos do projeto;
   se a issue sair daquele projeto, o valor vai junto embora. O detector de campos por tipo de
   work item — que acabamos de unificar entre o NX e o Setup — não tem equivalente.
3. **Sem datas de sprint confiáveis.** O recorte "última sprint de cada pessoa" e o
   posicionamento do cronograma dependem de início e fim da iteração.

## O que cada destino sabe fazer

A UI não pode descobrir no clique que o destino não suporta anexo, comentário ou auditoria de
BLOCK. O desenho da **tabela de capacidades por destino** — e por que ela é melhor que espalhar
`if (destino == DevOps)` pelas telas — está em
[Plano_Projeto_Local.md](Plano_Projeto_Local.md#capacidades-por-destino-a-tela-pergunta-não-adivinha).

## Caminho de implementação

A premissa é não duplicar regra: o que é **regra do NX** (hierarquia, cálculo de datas, rateio de
HH, ordem) fica onde está; o que é **conversa com o servidor** vira um provedor.

### Fase 1 — extrair o provedor (vale por si só)

Hoje a UI chama `TfsImportService` direto: **337 chamadas em 33 arquivos**. Extrair uma interface
`IWorkItemProvider` com o que o board realmente usa:

```
ListSprintsAsync        BuildSprintBoardAsync     CreateChildWorkItemAsync
SetWorkItemStateAsync   SetWorkItemParentAsync    SetWorkItemTagsAsync
SetNumericFieldAsync    SetWorkItemStackRankAsync Attachments…
```

`DevOpsProvider` é o código atual, praticamente intacto. Esta fase não muda comportamento e os
112 testes seguram o resultado — e já paga sozinha, separando "regra do NX" de "chamada HTTP".

### Fase 2 — provedor GitHub, só leitura

Monta `SprintBoard` a partir do GraphQL: issues do projeto, sub-issues para a hierarquia, campos
do projeto para HH e datas, campo Iteration para a sprint. Dá para ver o TaskBoard funcionando
sem nenhum risco de escrita.

### Fase 3 — escrita

Estado, responsável, campos, mover pai, criar item. Aqui entra a validação de hierarquia que o
DevOps fazia por nós.

### Fase 4 — o que talvez nunca fique igual

Ordem do backlog, auditoria de BLOCK (hoje reconstruída de `/updates`) e anexos. Para cada um,
decidir entre aproximar, degradar com aviso, ou não oferecer.

## Decisões que precisam de dono

- **Issue Types ou campo "Nível"?** Issue Types são de organização (conta pessoal não tem);
  um campo single-select funciona em qualquer lugar, mas é mais frouxo. O desenho do **mapa de
  níveis configurável** — incluindo o `Work Item Project` acima do EPIC, e a correção da
  limitação atual do conector do DevOps, onde os nomes de tipo estão presos no código — está em
  [Plano_GitProject_Import_Sync.md](Plano_GitProject_Import_Sync.md#os-níveis-o-github-não-tem-essa-hierarquia).
- **O que fazer com hierarquia inválida** vinda do GitHub: recusar a importação, avisar e
  continuar, ou adotar como está.
- **Um projeto do GitHub por cronograma, ou vários?** O NX hoje importa por Iteration Path e
  raiz de portfólio.

## Esforço e risco

A Fase 1 é grande e chata, mas segura. As fases 2 e 3 são código novo contra uma API que não
conhecemos em produção. O risco real não é técnico: é manter **dois conectores vivos** com a
mesma qualidade — cada regra nova passa a ter dois lugares para dar errado.

**Recomendação:** fazer a Fase 1 de qualquer forma; só seguir para a 2 se houver demanda real de
times no GitHub, medida em pessoas, não em hipótese.
