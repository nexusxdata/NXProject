# NXProject — o que ler antes de mexer

Regras que já custaram defeito quando alguém (humano ou IA) mexeu sem ler.

## Domínio

- **Modelo de HH** — [docs/hh-modelo.pt-BR.md](docs/hh-modelo.pt-BR.md). HH Original, Estimado,
  Atual e Restante são números diferentes; depois que a atividade começa, o campo de esforço
  guarda o PLANO e o andamento vai para Atual/Restante. `DurationHours = Atual + Restante`, e
  resumo agrega os filhos **em memória**.
- **Campos do DevOps e do GitProject** — [docs/devops/README.pt-BR.md](docs/devops/README.pt-BR.md)
  e [docs/gitproject/README.pt-BR.md](docs/gitproject/README.pt-BR.md). O catálogo
  (`NxDevOpsFieldCatalog`) é a ÚNICA fonte dos nomes, apelidos, tipos e escopo — nada de lista
  paralela num conector ou numa tela.
- **Story é folha no cronograma**; o nível Task é opcional e o detalhe fica no servidor.

## Antes de concluir que "o campo não existe"

Medir, com as sondas que já existem (não supor, e não abrir o navegador):

```
NXTestUnit campos                 # o que o import/sync resolve, conceito a conceito
NXTestUnit campos "?texto"        # busca crua nos campos da organização
NXTestUnit campos "#<id>"         # campos preenchidos de um work item
NXTestUnit campos "@<rótulo>"     # a que campo um RÓTULO do formulário está ligado
```

No DevOps o rótulo do formulário pode ser diferente do nome do campo.

## Como construir e testar

- `./build-community.ps1` — compila Release e roda a suíte (é isto que significa "testar").
- `./run-community.ps1` — só executa o exe Release; não compila.
- `./release-community-new-version.ps1` — build + zip + GitHub Release. Só quando pedido.
- Depois de alterar, **pare no build** e deixe o usuário testar.

## Convenções

- Todo `.cs` novo começa com as quatro linhas de copyright da Nexus XData.
- Um destino = uma fachada (`NxBoardSources`, `NxBoardWriters`, `NxBackendCapabilities`). Nada de
  `if` por nome de servidor espalhado nas telas.
- Comentário explica **por que**, não o que a linha faz.
