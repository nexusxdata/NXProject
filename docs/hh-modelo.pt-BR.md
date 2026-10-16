# O modelo de HH do NXProject

Quatro números descrevem o esforço de uma atividade. Eles não são variações do mesmo valor: cada
um responde uma pergunta diferente, e é a mistura entre eles que costuma gerar defeito.

| Número | Pergunta que responde | Muda com o andamento? |
|---|---|---|
| **HH Original** | Quanto foi planejado **no começo**? | Não — é o registro do plano |
| **HH Estimado** | Quanto se estima **hoje** para a atividade inteira? | Sim |
| **HH Atual** (realizado) | Quanto já foi gasto? | Sim |
| **HH Restante** | Quanto ainda falta? | Sim |

## O ciclo: antes e depois de a atividade começar

**Antes de começar** (0%), só existe uma estimativa. O campo de esforço no servidor guarda esse
número, e o HH Original ainda não tem valor próprio.

**No momento em que começa**, o NXProject guarda a estimativa inicial como **HH Original**. A
partir daí:

- o campo de esforço no servidor passa a ser o **registro do plano** — o andamento **não é mais
  gravado nele**;
- o que muda vai para **HH Atual** e **HH Restante**, cada um no seu campo;
- o **esforço estimado de hoje** deixa de ser um campo e passa a ser **cálculo**:
  `HH Atual + HH Restante`.

É por isso que, numa organização que usa um campo só, **HH Original e Esforço Estimado são o
mesmo campo** — e não há conflito: depois do início ninguém mais escreve a estimativa ali.

## Onde cada um é gravado

| | HH Original | HH Estimado | HH Atual | HH Restante |
|---|---|---|---|---|
| **DevOps — Task** | `Original Estimate` (nativo) | idem | `Completed Work` (nativo) | `Remaining Work` (nativo) |
| **DevOps — Story** | campo configurado (padrão `Esforço Estimado`) | o mesmo campo, até começar | campo configurado (`HH_Atual_float`) | campo configurado (`HH_Restante_float`) |
| **GitProject** | campo de número no board | idem | campo de número no board | campo de número no board |
| **Local** | o próprio cronograma | idem | idem | idem |

Os nomes são todos configuráveis em **Configuração → Nomes dos campos**; o catálogo
(`NxDevOpsFieldCatalog`) é a única fonte deles, e aceita apelidos — por isso `HH Estimado`
funciona numa organização onde o campo se chama `Esforço Estimado`.

Quando o HH Original e o HH Estimado resolvem para o **mesmo campo**, o sync grava **uma vez
só**: a guarda é `typeHoursRef != originalHoursRef`, em `TfsImportService` (bloco dos itens que
não são Task). Sem ela, o mesmo campo receberia dois valores na mesma gravação.

## A duração sai dos números, não do calendário

```
DurationHours = (HH Atual ?? 0) + (HH Restante ?? 0)       quando algum deles é > 0
DurationHours = horas úteis entre Início e Fim             só quando os dois são zero/nulos
```

Atividade de resumo **agrega os filhos em memória** (`TaskViewModel.SumTaskHours`) e **nunca**
grava o resultado no próprio modelo: gravar faz a soma multiplicar a cada abertura do arquivo.

## Para conferir numa organização de verdade

```
NXTestUnit campos                 # o que o import/sync resolve, conceito a conceito
NXTestUnit campos "?Original"     # busca crua na lista de campos da organização
NXTestUnit campos "#1063877"      # campos preenchidos de um work item
NXTestUnit campos "@HH_Original"  # a que campo um RÓTULO do formulário está ligado
```

A última existe porque o rótulo do formulário no DevOps pode ser diferente do nome do campo — e
foi exatamente isso que, uma vez, fez parecer que um campo não existia quando o valor estava à
vista na tela.
