// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;

namespace NXProject.Services
{
    /// <summary>
    /// Liga o recurso do cronograma à conta do GitHub que pode ser responsável pela issue.
    ///
    /// Ler é fácil — o GitHub devolve o nome e o login de quem está atribuído, e o import já usa
    /// isso. Gravar é que exige o caminho inverso, e ele não é simétrico:
    ///
    ///   • a mutation não aceita nome, só o ID da conta;
    ///   • o nome de exibição **pode estar vazio** (medido: 6 em 100 num repositório público);
    ///   • nome não é único — duas "Ana Silva" são perfeitamente possíveis;
    ///   • a conta precisa ser colaboradora do REPOSITÓRIO. Quem não tem acesso não pode ser
    ///     responsável, enquanto no DevOps qualquer identidade da organização pode.
    ///
    /// Por isso a regra aqui é conservadora: casa por login ou nome completo, exato e **único**.
    /// Ambíguo ou sem candidato não vira palpite — vira aviso. Responsável errado é pior que
    /// responsável vazio: ele muda a fila de trabalho de duas pessoas de uma vez.
    /// </summary>
    public static class GitProjectPeopleService
    {
        /// <summary>Uma conta candidata do repositório.</summary>
        public sealed record Account(string Id, string Login, string Name);

        /// <summary>Por que um recurso não virou conta. Vazio em <see cref="Resolution.Ok"/>.</summary>
        public sealed record Resolution(Account? Account, string Problem)
        {
            public bool Ok => Account != null;
        }

        /// <summary>
        /// A conta deste recurso entre as candidatas. Compara sem caixa e sem espaços nas pontas:
        /// o nome do recurso é digitado à mão no cronograma.
        /// </summary>
        public static Resolution Resolve(string? recurso, IReadOnlyList<Account> candidatas,
                                         string? loginGravado = null)
        {
            candidatas ??= new List<Account>();

            // O login GRAVADO no recurso manda, e vem do próprio import: é a resposta que o
            // GitHub já deu uma vez, e não depende de ninguém escrever o nome igual.
            var salvo = (loginGravado ?? "").Trim().TrimStart('@');
            if (salvo.Length > 0)
            {
                var conta = candidatas.FirstOrDefault(c => Igual(c.Login, salvo));
                if (conta != null) return new Resolution(conta, "");
                return new Resolution(null, NxText.T(
                    "A conta '{0}' gravada no recurso não tem mais acesso a este repositório.",
                    "The account '{0}' stored on the resource no longer has access to this repository.", salvo));
            }

            var nome = (recurso ?? "").Trim();
            if (nome.Length == 0) return new Resolution(null, "");

            // Sem login gravado: quem escreveu o login no nome do recurso também é atendido —
            // é identidade, não há ambiguidade possível.
            var porLogin = candidatas.Where(c => Igual(c.Login, nome)).ToList();
            if (porLogin.Count == 1) return new Resolution(porLogin[0], "");

            var porNome = candidatas.Where(c => Igual(c.Name, nome)).ToList();
            if (porNome.Count == 1) return new Resolution(porNome[0], "");
            if (porNome.Count > 1)
                return new Resolution(null, NxText.T(
                    "'{0}' corresponde a mais de uma conta ({1}). Use o login no recurso do cronograma para "
                    + "desfazer a ambiguidade.",
                    "'{0}' matches more than one account ({1}). Use the login on the schedule resource to "
                    + "remove the ambiguity.",
                    nome, string.Join(", ", porNome.Select(c => c.Login))));

            return new Resolution(null, NxText.T(
                "'{0}' não tem conta correspondente com acesso ao repositório. No GitHub só quem é "
                + "colaborador pode ser responsável.",
                "'{0}' has no matching account with access to the repository. On GitHub only a collaborator "
                + "can be an assignee.", nome));
        }

        private static bool Igual(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
            && string.Equals(a!.Trim(), b!.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }
}
