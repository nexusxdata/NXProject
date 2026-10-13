// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NXProject.Services
{
    /// <summary>
    /// Quem pode sincronizar neste board — o equivalente do `Adm_NX` do DevOps.
    ///
    /// No DevOps o campo guarda um GRUPO de identidade e o servidor resolve os membros. No GitHub
    /// isso só existe pela metade: **time é coisa de organização**; board de conta pessoal não tem
    /// times, só colaboradores do repositório. Então o campo aceita as duas formas, e a forma é
    /// deduzida do próprio valor:
    ///
    ///   • `@minha-org/time-de-projetos` → time: os membros vêm da API (exige `read:org` no token);
    ///   • `ana, bruno, carla`           → lista de logins, separada por vírgula.
    ///
    /// Vazio libera, como no DevOps: um board sem a regra configurada não deve travar ninguém.
    /// E falha TÉCNICA (rede, escopo que falta) também libera — a mesma decisão que já vale no
    /// DevOps, pela mesma razão: deixar de sincronizar porque a checagem caiu seria transformar
    /// um problema de leitura em bloqueio de trabalho.
    /// </summary>
    public static class GitProjectAdminService
    {
        /// <summary>O valor configurado é um time da organização, e não uma lista de logins?</summary>
        public static bool IsTeam(string? valor)
        {
            var v = (valor ?? "").Trim();
            return v.StartsWith("@", StringComparison.Ordinal) && v.Contains('/');
        }

        /// <summary>Organização e time, a partir de `@org/time`.</summary>
        public static (string Org, string Team) ParseTeam(string? valor)
        {
            var v = (valor ?? "").Trim().TrimStart('@');
            var i = v.IndexOf('/');
            return i <= 0 ? ("", "") : (v[..i].Trim(), v[(i + 1)..].Trim());
        }

        /// <summary>
        /// O usuário está autorizado, dado o valor configurado e — quando for time — a lista de
        /// membros já resolvida.
        ///
        /// A parte que decide fica separada da rede para caber em teste: autorização é a regra
        /// que mais machuca quando erra, nos dois sentidos (travar quem pode, liberar quem não).
        /// </summary>
        public static bool IsAllowed(string? configurado, string? usuario, IEnumerable<string>? membros = null)
        {
            var regra = (configurado ?? "").Trim();
            if (regra.Length == 0) return true;            // sem regra configurada: libera

            var quem = (usuario ?? "").Trim().TrimStart('@');
            if (quem.Length == 0) return true;             // não sei quem é: não é aqui que se bloqueia

            if (IsTeam(regra))
            {
                // Membros não lidos (sem `read:org`, rede fora) é falha técnica, não negativa.
                var lista = membros?.ToList();
                if (lista == null) return true;
                return lista.Any(m => Igual(m, quem));
            }

            return regra.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim().TrimStart('@'))
                        .Any(x => Igual(x, quem));
        }

        /// <summary>
        /// Checagem completa: descobre o valor no board, resolve o time quando for o caso e diz
        /// se o usuário atual pode gravar. Devolve também o que estava configurado, para a
        /// mensagem poder dizer QUAL é a regra que barrou.
        /// </summary>
        public static async Task<(bool Allowed, string Rule)> CanSyncAsync(
            GitProjectOptions options, GitProjectClient.ProjectSnapshot snap, GitProjectClient cliente)
        {
            var regra = ValorConfigurado(options, snap);
            if (string.IsNullOrWhiteSpace(regra)) return (true, "");

            try
            {
                var (quem, _) = await cliente.WhoAmIAsync();
                List<string>? membros = null;
                if (IsTeam(regra))
                {
                    var (org, time) = ParseTeam(regra);
                    membros = await cliente.TeamMembersAsync(org, time);
                }
                return (IsAllowed(regra, quem, membros), regra);
            }
            catch
            {
                return (true, regra);   // falha técnica não bloqueia (igual ao DevOps)
            }
        }

        /// <summary>
        /// O valor do campo de administração no board. Procura primeiro no item de nível Project
        /// — é onde o DevOps o guarda, no Work Item Project —, e aceita qualquer item preenchido
        /// como reserva, porque nem todo board tem o nível Project.
        /// </summary>
        public static string ValorConfigurado(GitProjectOptions options, GitProjectClient.ProjectSnapshot snap)
        {
            if (snap?.Items == null) return "";
            var porNumero = snap.Items.ToDictionary(i => i.Number);

            var raiz = snap.Items.FirstOrDefault(i =>
                string.Equals(GitProjectBoardService.LevelKindOf(i, porNumero, options), "Project",
                              StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(i.AdmGroup));

            return (raiz?.AdmGroup
                    ?? snap.Items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.AdmGroup))?.AdmGroup
                    ?? "").Trim();
        }

        private static bool Igual(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
            && string.Equals(a!.Trim(), b!.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }
}
