# Plano — TaskBoard com Projeto Local (sem servidor)

> Documento de desenho **e de estado**: o modo Projeto Local já funciona no TaskBoard — board,
> gravação no arquivo, pasta de artefatos e log de BLOCK. O que falta está na tabela abaixo.
>
> Público: quem for implementar o que resta e quem precisa decidir o escopo da sincronização.
>
> Atualizado em 02/10/2026.

## Estado da implementação

### Pronto e no build

| O quê | Onde |
|---|---|
| Combo de destino no TaskBoard, no Importar e no Sincronizar, travado com pendência na fila | [TfsSprintWindow](NXProject.Community/Views/TfsSprintWindow.xaml.cs) |
| Board local de LEITURA: cronograma aberto → colunas, cards, sprints, pessoas | [LocalBoardService](NXProject.Shared/Services/LocalBoardService.cs) |
| Board local de ESCRITA: as pendências viram alteração no `ProjectTask` e o arquivo é salvo | [LocalBoardWriter](NXProject.Shared/Services/LocalBoardWriter.cs) |
| Tabela de capacidades por destino (objeto imutável) | [NxBackend](NXProject.Shared/Services/NxBackend.cs) |
| Destino por projeto no Portfólio, com pasta obrigatória no modo local | [PortfolioTargetWindow](NXProject.Community/Views/PortfolioTargetWindow.xaml.cs) |
| Pasta do projeto: artefatos por atividade, anexar, abrir, e `block.jsonl` append-only | [LocalProjectFolder](NXProject.Shared/Services/LocalProjectFolder.cs) |
| Auditoria de BLOCK lida do log da pasta, inclusive o bloqueio ainda aberto | `LocalBlockAudit` no TaskBoard |
| Testes: projeção do board e capacidades por destino | [NXTestUnit](NXTestUnit/Program.cs) |

### Pendente

| # | O quê | Por que ficou de fora | Tamanho |
|---|---|---|---|
| 1 | **Board nascer no destino do Portfólio.** O `BackendKind` é gravado por projeto, mas as telas ainda abrem em DevOps e a pessoa troca no combo. | decisão de quando ler: ao abrir o cronograma ou ao abrir cada tela | pequena |
| 2 | **🔗 virar 📅 no modo local.** Hoje o botão chama o caminho do DevOps, que sem organização configurada simplesmente não faz nada. | precisa do `Action<int>` de focar no Gantt, como a janela de queries já recebe | pequena |
| 3 | **A tela consultar a capacidade, não o nome do destino.** `_caps` já existe e é resolvido, mas `ApplyCapabilities` ainda compara `_backend == Local`; comentários e outros botões de servidor continuam visíveis no modo local. | é a troca que faz um destino novo não exigir revisar cada botão | média |
| 4 | **Carimbos de data no `.nxp`** (criação, mudança de estado, encerramento) — sem eles, o filtro "Closed dos últimos N dias" e as datas do card ficam vazios no modo local. | mexe no schema do arquivo | pequena |
| 5 | **Interface de provedor para o board** (fase 1): hoje o modo local é um `if` em pontos escolhidos do TaskBoard, não um provedor atrás de contrato. | funcionou sem ela; vira necessária quando entrar o terceiro destino | grande |
| 6 | **Sincronizar local → DevOps** (fase 5): adiado por decisão. O checkbox "Sincronizar no DevOps" do Portfólio **já guarda a intenção** por projeto, e não faz nada ainda. | um board local que grava errado no servidor é pior que um que não grava | grande e arriscada |

O caminho curto para a próxima versão são os itens 1 a 4; o 5 só se entrar GitProject, e o 6 é
conversa própria — ver "Sincronizar depois".

## A ideia

Usar o TaskBoard sobre o **cronograma salvo em arquivo** (`.nxproject`), sem servidor nenhum.
Quem planeja no NX já tem ali a hierarquia, as datas, o HH e os responsáveis — falta só poder
trabalhar o dia a dia nesse mesmo material: arrastar card entre estados, marcar Doing/Done,
anotar HH realizado, bloquear.

Serve a três situações concretas:

1. **Avaliar o NX sem DevOps.** Hoje o board exige conexão, token e campos criados na
   organização. Com o modo local, dá para abrir um cronograma de exemplo e ver o produto
   funcionando em um minuto.
2. **Trabalhar fora do ar.** Em viagem, em cliente sem VPN, ou com o DevOps indisponível.
3. **Rascunhar antes de publicar.** Montar o plano da sprint inteiro no board e só depois
   decidir o que sobe.

## A regra que define tudo: o ajuste é LOCAL

Quando o cronograma veio do DevOps (ou, no futuro, do GitHub), cada item já tem id de servidor.
Trabalhar localmente **não** pode escrever lá — e também não pode fingir que o servidor não
existe. A regra:

> No modo **Projeto Local**, toda alteração é gravada **apenas no arquivo**. O item mantém o id de
> origem e ganha a marca de "alterado localmente". Nada vai para o servidor até alguém pedir.

Isso vale inclusive para o que hoje é imediato no board (estado, tags, HH realizado): no modo
local, tudo vira mudança no `.nxproject`.

## Onde ficam as Tasks

Esta é a pergunta que decide o escopo, e ela já tem resposta no produto:

- No cronograma, **a Story é a folha** — o detalhe fica no DevOps. O nível Task é opcional
  (`schedule-story-is-leaf`).
- Então um cronograma pode ter Task ou não.

Daí duas situações:

| O arquivo tem… | O que o board local faz |
|---|---|
| Story **com** Tasks | board completo: cards de Task nas colunas de estado, igual ao de hoje |
| Story **sem** Task | board de Stories (a visão Projeto & Story já funciona assim); criar Task passa a ser uma edição local do cronograma |

Quando a origem é DevOps/GitProject, o board local deve **verificar se há Task no arquivo** antes
de abrir: sem Task, ou o usuário trabalha no nível Story, ou importa as Tasks do servidor uma vez
e passa a trabalhar nelas localmente.

## O que precisa existir

1. ~~**Provedor local**~~ — **feito de outra forma.** Em vez da `IWorkItemProvider` da Fase 1 do
   [Plano_GitProject.md](Plano_GitProject.md), o modo local entrou como caminho próprio no
   TaskBoard (`LocalBoardService` para ler, `LocalBoardWriter` para gravar). A interface continua
   valendo quando entrar um terceiro destino — é o item 5 dos pendentes.
2. ~~**Modelo de estado no arquivo**~~ — **já existia.** Ver "Correção de uma premissa" abaixo:
   estado, tags, sprint, prioridade, rank e HH realizado já eram gravados. Falta só o item 4 dos
   pendentes (carimbos de data).
3. **Marca de alteração local.** Por item: "alterado localmente desde a última sincronização",
   com o valor antigo, para o dia da sincronização saber o que mudou de cada lado.
   **Pendente** — só faz falta com a sincronização ligada (item 6).
4. ~~**Sprint sem servidor**~~ — **feito**: as sprints vêm do próprio cronograma
   (`LocalBoardService.Sprints`). O recorte "última sprint" segue dependendo de início e fim
   cadastrados.

## Sincronizar depois (fase 2)

Com a marca de alteração local, a sincronização vira um `Export → Sincronizar` igual ao de hoje,
com uma diferença: é preciso **comparar os dois lados**. O NX já tem a peça — `Sync_version` e
`Sync_Name` detectam "o servidor mudou depois de você" e já geram o relatório de conflito. O modo
local reaproveita isso: item alterado dos dois lados vira conflito a resolver, não sobrescrita
silenciosa.

**Decidido assim:** a primeira versão nasceu **sem sincronização nenhuma** (local é local). Um
board local que grava errado no DevOps é pior do que um board local que não grava.

O que existe hoje é só a **intenção registrada**: o checkbox "Sincronizar no DevOps" por projeto
no Portfólio (`SyncLocalBoardToDevOps`). Ele não dispara nada — serve para, quando a volta for
implementada, saber quais projetos a querem.

## O que NÃO faz sentido no modo local

- **Links** do DevOps (o 🔗 vira "ir para o cronograma").
- Auditoria de BLOCK **pelo histórico do servidor** e **anexos no work item** deixam de existir
  nessa forma, mas têm equivalente local — ver "A pasta do projeto".
- **Grupo administrador (`Adm_NX`)**: controle de quem sincroniza é do servidor.

## O destino é escolhido no Portfólio, não no board

O combo do TaskBoard resolve o caso do momento, mas a decisão "este cronograma trabalha contra o
quê" é **do projeto**, não da tela. O lugar natural é o **Portfólio de Projetos**, onde cada
projeto já tem nome, arquivo `.nxproject`, centro de custo e tipo OPEX/CAPEX
([PortfolioProjectConfig](NXProject.Shared/Services/TfsConnectionStore.cs)).

**Feito** ([PortfolioTargetWindow](NXProject.Community/Views/PortfolioTargetWindow.xaml.cs),
aberta no Mapa de Alocação → Fonte → "Destino dos projetos…"): um campo **Tipo de destino** por
projeto do portfólio, com quatro valores:

| Valor | O que significa |
|---|---|
| **Default** | usa o destino padrão do NXProject (hoje, Azure DevOps) — é o valor de quem não quer escolher |
| **DevOps** | sempre Azure DevOps, mesmo que o padrão mude depois |
| **GitProject** | GitHub Projects |
| **Projeto Local** | só o arquivo; o board não fala com servidor nenhum. Habilita o campo **Pasta do projeto** (artefatos e log de BLOCK), que por padrão é a pasta de mesmo nome ao lado do `.nxproject` |

Como isso muda as telas:

- **Abrir o cronograma já traz o destino certo.** O combo do TaskBoard, do Importar e do
  Sincronizar deve **nascer** no valor do portfólio, em vez de sempre em DevOps.
  **Pendente** (item 1): o valor é gravado, mas ainda não é lido na abertura das telas.
- **O combo continua existindo**, para experimentar sem mexer no cadastro — e continua travado
  quando há pendência na fila, pelo mesmo motivo de hoje.
- **Projeto Local exige cronograma aberto.** Diferente dos outros dois, o board local não tem de
  onde carregar sem um `.nxproject` aberto: sem arquivo, a opção fica desabilitada com essa
  explicação, em vez de abrir um board vazio.

## O botão 🔗 no modo local: ir para o cronograma

No DevOps o 🔗 abre o work item no navegador. Sem servidor não há o que abrir — mas há um destino
melhor: **a própria atividade no cronograma**.

O NX já faz exatamente isso em outro lugar: a janela de queries recebe um `Action<int>` e mostra
o botão 📅 "ver no cronograma" nas linhas cujo id está no arquivo aberto
([TfsQueryWindow](NXProject.Community/Views/TfsQueryWindow.xaml.cs)). O board local reusa o mesmo
caminho:

- **DevOps / GitProject:** 🔗 abre no navegador, como hoje.
- **Projeto Local:** o botão vira 📅 e **foca a atividade no Gantt**, rolando até ela.
  **Pendente** (item 2): hoje o botão ainda chama o caminho do DevOps, que sem organização
  configurada não faz nada — falha silenciosa, que é o pior dos mundos.

Com isso o modo local ganha algo que o modo servidor não tem: ida e volta imediata entre o card e
a linha do cronograma — que é, afinal, o mesmo objeto.

## Capacidades por destino: a tela pergunta, não adivinha

Com três destinos possíveis, a tela precisa saber **o que cada um sabe fazer** — senão o botão de
anexo aparece no modo local e só descobre que não funciona quando alguém clica. A ideia é um
ponto único que responda isso.

**Estado:** a tabela existe e é imutável, como desenhada
([NxBackend](NXProject.Shared/Services/NxBackend.cs)), e o TaskBoard já resolve o `_caps` ao
trocar de destino. O que **falta** é a tela passar a perguntar a ela em vez de comparar o nome do
destino (item 3 dos pendentes).

### O formato: tabela de capacidades, não "se for DevOps"

```csharp
/// <summary>Onde o anexo mora neste destino.</summary>
public enum NxAttachmentStore { None, Server, LocalFolder }

public sealed record NxBackendCapabilities(
    NxAttachmentStore Attachments, // anexo: servidor, pasta local, ou não existe
    bool Comments,             // comentários / trâmite
    bool BlockAudit,           // auditoria de BLOCK (precisa de histórico do servidor)
    bool OpenInBrowser,        // 🔗 abrir o item no navegador
    bool ServerStates,         // colunas de estado vindas do processo
    bool ServerPeople,         // pessoas vindas da organização
    bool ConcurrencyControl,   // Sync_version / conflito
    bool BacklogOrderWrite,    // gravar a ordem do backlog
    bool CreateItem,           // criar Story/Task no destino
    bool RequiresOpenSchedule) // Projeto Local só abre com cronograma aberto
{
    public static NxBackendCapabilities For(NxBackendKind kind) => kind switch
    {
        NxBackendKind.DevOps     => new(NxAttachmentStore.Server,      true,  true,  true,  true,  true,  true,  true,  true,  false),
        NxBackendKind.GitProject => new(NxAttachmentStore.None,        true,  false, true,  false, true,  false, false, true,  false),
        // Local: anexo em pasta, auditoria de BLOCK pelo log da pasta, sem comentario nem servidor.
        NxBackendKind.Local      => new(NxAttachmentStore.LocalFolder, false, true,  false, false, false, false, true,  true,  true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
```

### A pasta do projeto: requisito do modo local

No modo local, **o cronograma não basta**: o projeto precisa de um `.nxproject` **e de uma
pasta com o nome dele**. É ali que mora tudo o que, no DevOps, vive no servidor — artefatos e
histórico.

```
MeuProjeto.nxproject
MeuProjeto\
    Artefatos\
        1042 - Validar tabelas\
            especificacao.pdf
            print-do-erro.png
        1043 - Ajustar view\
            consulta.sql
    Logs\
        block.jsonl        <- cada bloqueio/liberação, com data, hora e quem
```

**Onde fica a pasta:** por padrão, ao lado do `.nxproject`, com o nome do projeto. Mas o
caminho é **configurável no Portfólio de Projetos**, junto do tipo de destino — é lá que o
projeto já declara o arquivo `.nxproject`, o centro de custo e o OPEX/CAPEX. Assim dá para
apontar uma pasta de rede, um diretório versionado ou a pasta que a equipe já usa, sem
obrigar ninguém a mover nada.

**Feito assim:** a pasta é **obrigatória** para escolher Projeto Local, e a escolha acontece no
Portfólio — a tela recusa salvar sem caminho e cria a pasta ali, na hora em que a pessoa está
justamente dizendo onde ela deve ficar. Nada é criado escondido ao abrir o board; sem pasta
configurada, anexar avisa e aponta o caminho do cadastro.

#### Anexos

- **Anexar** copia (ou move) o arquivo para `Artefatos\<id - título>`; o card lista o que
  estiver lá, com o mesmo visual de hoje — primeiro anexo no card, o resto no editor.
- **A pasta é a fonte da verdade.** Quem largar um arquivo lá pelo Explorador vê no card na
  próxima abertura; nada de índice paralelo para dessincronizar.
- **Nome da subpasta** = id + título da atividade, sanitizado. Mudar o título **não** renomeia
  (o id na frente mantém o vínculo); renomear é opção manual, nunca automática.
- **O `.nxproject` não engorda:** ele guarda no máximo o caminho-raiz; os arquivos ficam fora,
  como já ficam hoje no servidor.
- **Fora do cronograma, fora do NX.** A pasta é de quem usa: dá para versionar, mandar por
  e-mail ou guardar em rede, sem o NX no meio.

#### BLOCK: log no lugar do histórico do servidor

A auditoria de BLOCK no DevOps é reconstruída do histórico de revisões do work item.
Localmente não há revisão — mas o NX **sabe o momento em que a pessoa bloqueou e liberou**, e
isso basta: cada troca vira uma linha em `Logs\block.jsonl`.

```jsonl
{"id":1042,"acao":"block","quando":"2026-10-02T09:14:22","quem":"Carmo","motivo":"aguardando acesso"}
{"id":1042,"acao":"release","quando":"2026-10-02T15:02:10","quem":"Carmo"}
```

- O cálculo de horas é o **mesmo de hoje** (horas úteis pelo calendário do projeto): muda só a
  origem dos eventos, e o `BlockService` é reaproveitado inteiro.
- O arquivo é **append-only**: nada é reescrito, então falha de gravação não corrompe o que já
  estava lá.
- **Limite honesto:** o log só conhece o que aconteceu **no NX, depois que a pasta existe**.
  Bloqueio feito fora dali (ou antes) não aparece — e a tela de auditoria deve dizer isso, em
  vez de mostrar um total que parece completo.
- Linha fora do formato (editada à mão, truncada) é **ignorada com aviso**, não derruba a tela.

Duas consequências a considerar:

1. **Cronograma em rede compartilhada** passa a ter artefatos e log junto — bom para o time, e
   um motivo a mais para avisar sobre dois usuários no mesmo arquivo (risco 3).
2. **Sincronizar depois** ganha um caso novo: subir para o servidor o que está na pasta. Vale
   deixar fora da primeira versão — sincronizar arquivo é outra conversa, com tamanho,
   duplicata e conflito de nome.

### Singleton: sim para o acesso, não para o estado

Vale ter um ponto único de acesso — algo como `NxBackend.Current`, resolvido ao abrir o
cronograma a partir do tipo configurado no Portfólio. O cuidado é não transformá-lo em **estado
global mutável**: o NXProject abre mais de uma janela sobre o mesmo cronograma, e um valor que
muda no meio do caminho deixa telas abertas com capacidades de outro destino.

Desenho seguro:

- o **objeto de capacidades é imutável** (o `record` acima);
- cada janela **recebe o seu** ao abrir (como já recebe `TfsConnectionOptions`), e não vai
  perguntar ao singleton a cada clique;
- o singleton serve para **resolver** qual é o destino atual, não para guardar o que a tela está
  fazendo.

### O que isso resolve na prática

| Hoje | Com capacidades |
|---|---|
| botão aparece e dá erro no clique | botão **não aparece** (ou fica desabilitado com o motivo no tooltip) |
| cada tela decide sozinha o que mostrar | uma tabela só, revisável de uma olhada |
| destino novo = caçar `if` pelo código | destino novo = uma linha na tabela |

Vale desabilitar **com explicação**, não sumir em silêncio: "Anexos não existem no modo Projeto
Local" ensina; um botão ausente deixa a pessoa procurando.

## Análise de viabilidade e esforço (medida no código)

Números levantados no código, não estimados de cabeça:

| Medida | Valor |
|---|---|
| Chamadas ao `TfsImportService` dentro do TaskBoard | **153** |
| Métodos distintos do serviço usados pelo board | **33** (≈20 de escrita, ≈13 de leitura) |
| Campos que o card precisa e o `.nxproject` **já guarda** | estado, tags, sprint, prioridade, rank, responsável, HH estimado, HH atual, descrição, critérios de aceitação, Approved, tipo, pai, data de início fixada, % concluído |
| Campos que o card mostra e o arquivo **não tem** | data de criação, data de mudança de estado, data de encerramento, horas de BLOCK, anexos |

### Correção de uma premissa do próprio plano

A seção "O que precisa existir" dizia que faltava criar no arquivo o estado, as tags e o HH
realizado. **Não falta**: `TfsState`, `TfsTags`, `TfsIterationPath`, `Priority` e `TfsStackRank`
já são gravados e lidos no `.nxp`
([XmlProjectService](NXProject.Shared/Services/XmlProjectService.cs)), junto com `CurrentHours`,
`Description`, `AcceptanceCriteria` e `Approved`.

Isso muda a conta: o trabalho de **modelo de dados** era o que eu apontava como maior, e ele é
pequeno — faltam só carimbos de data (criação, mudança de estado, encerramento) e, se quiser, as
horas de BLOCK.

### Esforço por fase

| Fase | O quê | Tamanho | Estado |
|---|---|---|---|
| 1 | Interface de provedor para o board (os 33 métodos) + `DevOpsProvider` com o código atual | **grande**, mas mecânica e coberta pelos testes | **não feita** — ver abaixo |
| 2 | `LocalProvider` de LEITURA: projetar `Project` → `SprintBoard` | **pequena** — é projeção de dados que já existem | **feita** (`LocalBoardService`) |
| 3 | `LocalProvider` de ESCRITA: aplicar as pendências no `ProjectTask` e salvar o arquivo | **média** — 20 operações, todas em memória | **feita** (`LocalBoardWriter`) |
| 4 | Carimbos de data no `.nxp` (criação, estado, encerramento) | **pequena** | **não feita** |
| 5 | Sincronizar local ⇄ servidor | **grande e arriscada** — ver riscos | **adiada por decisão** |

**A fase 1 foi dispensada, e vale registrar por quê.** O plano dizia que sem a interface o modo
local viraria um `if` em cada um dos 153 pontos. Não foi o que aconteceu: as fases 2 e 3
concentraram a diferença em **poucos pontos de entrada** do TaskBoard — carregar sprints, carregar
o board, salvar, anexos e auditoria de BLOCK. O resto do código do board não sabe de onde vêm os
cards, porque continua trabalhando sobre o mesmo `SprintBoard`.

Isso não cancela a fase 1: com um terceiro destino (GitProject), a conta muda, porque aí são três
caminhos em cada ponto de entrada em vez de dois. Enquanto são dois, a interface seria custo sem
retorno.

## O que NÃO vai funcionar no modo local

Dividido pelo motivo, que é o que importa na hora de decidir:

### Não existe sem servidor (sem substituto)

| Recurso | Por quê |
|---|---|
| **Comentários / trâmite** | o histórico de comentários é do work item |
| **🔗 Abrir no DevOps / ID clicável** | não há item no servidor para abrir |
| **Descoberta de prioridade máxima do processo** | é regra do template de processo |
| **Controle de concorrência (`Sync_version`)** | só faz sentido com dois lados gravando |
| **Grupo administrador (`Adm_NX`)** | quem pode gravar é regra do servidor |

### Funciona, mas com dado diferente

| Recurso | Como fica |
|---|---|
| **Colunas de estado** | hoje vêm do processo do DevOps; local seria um conjunto fixo (New/Active/Closed) ou configurável no cronograma |
| **Pessoas do board** | vêm dos recursos do cronograma, não dos usuários da organização |
| **Sprints** | vêm das sprints do próprio cronograma; sem início/fim cadastrados, os recortes "sprint atual" e "última sprint" ficam limitados |
| **Filtro "Closed dos últimos N dias"** e as datas do card | dependem dos carimbos de data que o arquivo ainda não guarda (pendente 4) |
| **Criar Story/Task** | nasce no cronograma, com id interno negativo, como o NX já faz para item NoDevOps |
| **Anexos** | arquivos em `Artefatos/<id - título>` na pasta do projeto; a pasta é a fonte da verdade — ver "A pasta do projeto" |
| **Auditoria de BLOCK** | vem de `Logs/block.jsonl` na pasta do projeto; não conhece bloqueio feito fora do NX |

### Funciona igual

Arrastar entre estados, mover Task de Story, editar título, responsável, HH, datas, descrição,
critérios de aceitação, prioridade, ordem (rank), tags (BLOCK, Doing, Done), % de conclusão,
Approved, filtros, buscas, agrupamentos e as duas visões do board.

## Riscos — para analisar antes de decidir

| # | Risco | Por que importa | Como descobrir cedo | Saída se acontecer |
|---|---|---|---|---|
| 1 | 🔴 **Divergir do servidor sem ninguém perceber** | a mesma Story muda local e no DevOps; na sincronização, alguém perde trabalho | simular: alterar o item nos dois lados e rodar a sincronização | nascer **sem** sincronização; quando ligar, item alterado dos dois lados vira conflito, nunca sobrescrita |
| 2 | 🟠 **Crescimento do `.nxproject`** | estado, tags e HH realizado por Task engordam o arquivo e o salvar/abrir | medir com o maior cronograma que você tem | guardar só o que difere do importado; manter o schema versionado |
| 3 | 🟠 **Arquivo compartilhado em rede** | duas pessoas no mesmo `.nxproject` sobrescrevem uma à outra | testar com o arquivo numa pasta compartilhada | o NX é monousuário por arquivo: avisar na abertura, como já faz com o cronograma |
| 4 | 🟡 **Cronograma sem Task** | o board local abre vazio e parece quebrado | abrir um `.nxproject` importado só até Story | verificar antes e oferecer: trabalhar no nível Story ou importar as Tasks uma vez |
| 5 | 🟡 **Expectativa de "offline completo"** | quem usa local pode achar que tudo sincroniza depois | deixar escrito na tela o que é local-only | lista explícita do que não volta (auditoria de BLOCK, anexos, links) |

### O que decidir ANTES de escrever código

1. ~~Porta de entrada ou modo de trabalho?~~ **Decidido: as duas, em ordem.** Nasceu como modo de
   trabalho sem sincronização; a volta ao servidor é outra versão.
2. ~~Sem Task no arquivo, cria Task local ou trabalha no nível Story?~~ **Decidido: trabalha no
   nível que o arquivo tem**, e criar Task é edição local do cronograma, como o board já faz.
3. ~~Mesmo schema ou bloco separado?~~ **Não se aplica**: os campos já estavam no schema do
   cronograma. A decisão volta só para os carimbos de data (pendente 4).

Resta uma decisão de produto, para o item 6: na sincronização, **o que ganha** quando os dois
lados mudaram — e se anexo e log de BLOCK sobem junto ou ficam de fora.

## Esforço e risco

Menor que o do GitHub: não há API nova, autenticação nem campo para criar. O trabalho é de
**modelo de dados** — estender o `.nxproject` sem quebrar arquivo antigo (o schema já é
versionado e há testes de salvar/abrir).

O risco é de produto, não técnico: dois lugares onde a mesma Story pode mudar. Por isso a regra
do topo — **local é local** — e a sincronização só depois, explícita e com conflito à vista.

**Recomendação, confirmada pela implementação:** este caminho valia mais a pena que o do GitHub, e
por menos esforço do que o plano previa — porque o `.nxproject` já guardava quase tudo, e porque a
diferença de destino couber em poucos pontos de entrada do board dispensou a fase 1. A primeira
barreira de adoção do produto — precisar de um DevOps configurado só para ver o board funcionando
— está resolvida.
