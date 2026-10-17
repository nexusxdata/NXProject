// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NXProject.Services
{
    /// <summary>
    /// Cronômetro do Sincronizar do GitProject: uma linha por chamada ao GitHub, e um resumo
    /// por fase no fim.
    ///
    /// Existe porque "está lento" não diz ONDE. No GitHub cada valor é uma mutação própria — não
    /// há commit no fim para agrupar —, então o tempo é a soma de idas à rede, e o que importa
    /// medir é quantas idas são e quanto cada uma custa. Com o log, a conversa deixa de ser
    /// sobre palpites (rede? GraphQL? nossa montagem?) e passa a ser sobre a conta.
    /// </summary>
    public static class GitSyncLog
    {
        private static readonly object Lock = new();
        private static readonly Dictionary<string, (int Vezes, long Ms)> PorFase = new();
        private static Stopwatch? _total;

        public static string Path => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NXProject.Community", "git-sync-log.txt");

        /// <summary>Começa uma sincronização: zera o acumulado e abre o cabeçalho.</summary>
        public static void Inicia(string board, int itens)
        {
            lock (Lock)
            {
                PorFase.Clear();
                _total = Stopwatch.StartNew();
            }
            Escreve($"=== Sincronizar GitProject — board '{board}' ({itens} itens no cronograma)");
        }

        /// <summary>
        /// Mede UMA chamada ao GitHub e devolve o que ela devolveu. O custo da medição é o
        /// Stopwatch; o valor é poder dizer "a ordem custou 38 s em 19 movimentos".
        /// </summary>
        public static async System.Threading.Tasks.Task<T> MedeAsync<T>(
            string fase, string detalhe, Func<System.Threading.Tasks.Task<T>> acao)
        {
            var relogio = Stopwatch.StartNew();
            try { return await acao(); }
            finally { Registra(fase, detalhe, relogio.ElapsedMilliseconds); }
        }

        /// <inheritdoc cref="MedeAsync{T}"/>
        public static async System.Threading.Tasks.Task MedeAsync(
            string fase, string detalhe, Func<System.Threading.Tasks.Task> acao)
        {
            var relogio = Stopwatch.StartNew();
            try { await acao(); }
            finally { Registra(fase, detalhe, relogio.ElapsedMilliseconds); }
        }

        private static void Registra(string fase, string detalhe, long ms)
        {
            lock (Lock)
            {
                var (vezes, soma) = PorFase.TryGetValue(fase, out var a) ? a : (0, 0L);
                PorFase[fase] = (vezes + 1, soma + ms);
            }
            Escreve($"  {ms,6} ms  {fase}{(string.IsNullOrEmpty(detalhe) ? "" : "  " + detalhe)}");
        }

        /// <summary>Fecha a sincronização com a conta: por fase, e o total.</summary>
        public static void Encerra()
        {
            List<KeyValuePair<string, (int Vezes, long Ms)>> fases;
            long total;
            lock (Lock)
            {
                fases = PorFase.OrderByDescending(f => f.Value.Ms).ToList();
                total = _total?.ElapsedMilliseconds ?? 0;
                _total = null;
            }
            Escreve("--- resumo (o que levou o tempo) ---");
            foreach (var f in fases)
                Escreve($"  {f.Value.Ms,6} ms  {f.Key}  ({f.Value.Vezes} chamada(s),"
                        + $" {f.Value.Ms / Math.Max(1, f.Value.Vezes)} ms cada)");
            Escreve($"  {total,6} ms  TOTAL (inclui o que nao e rede)");
            Escreve("");
        }

        private static void Escreve(string linha)
        {
            try
            {
                lock (Lock)
                {
                    var arquivo = Path;
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(arquivo)!);
                    if (System.IO.File.Exists(arquivo) && new System.IO.FileInfo(arquivo).Length > 512 * 1024)
                        System.IO.File.Delete(arquivo);
                    System.IO.File.AppendAllText(arquivo,
                        $"{DateTime.Now:dd/MM/yyyy HH:mm:ss}  {linha}{Environment.NewLine}");
                }
            }
            catch { /* diagnostico nunca derruba a sincronizacao */ }
        }
    }
}
