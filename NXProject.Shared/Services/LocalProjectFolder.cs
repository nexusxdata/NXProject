// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace NXProject.Services
{
    /// <summary>
    /// A pasta do projeto no modo local: onde moram os artefatos (anexos por atividade) e o log
    /// de BLOCK. É o que, no DevOps, vive no servidor.
    ///
    /// Duas regras que valem a pena ter em mente ao mexer aqui:
    ///
    ///  • **A pasta é a fonte da verdade.** Não há índice paralelo — o card lista o que estiver no
    ///    disco. Quem largar um arquivo lá pelo Explorador vê no board, e quem apagar some do
    ///    board. Nada para dessincronizar.
    ///  • **O log é append-only.** Cada bloqueio/liberação acrescenta uma linha; nada é reescrito.
    ///    Falha de gravação não corrompe o histórico anterior, e linha estragada é ignorada.
    /// </summary>
    public static class LocalProjectFolder
    {
        public const string ArtifactsDirName = "Artefatos";
        public const string LogsDirName = "Logs";
        public const string BlockLogFileName = "block.jsonl";

        /// <summary>
        /// Pasta do projeto: a configurada no Portfólio ou, vazia, a de mesmo nome do arquivo, ao
        /// lado dele. Vazio quando não há arquivo salvo (cronograma novo, nunca gravado).
        /// </summary>
        public static string Resolve(string? projectFilePath, string? configuredFolder)
        {
            if (!string.IsNullOrWhiteSpace(configuredFolder)) return configuredFolder!.Trim();
            if (string.IsNullOrWhiteSpace(projectFilePath)) return "";
            var dir = Path.GetDirectoryName(projectFilePath!) ?? "";
            var name = Path.GetFileNameWithoutExtension(projectFilePath!);
            return string.IsNullOrEmpty(name) ? "" : Path.Combine(dir, name);
        }

        public static bool Exists(string folder) =>
            !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder);

        /// <summary>Cria a pasta e as subpastas. Só é chamado depois de a pessoa confirmar.</summary>
        public static void Create(string folder)
        {
            Directory.CreateDirectory(Path.Combine(folder, ArtifactsDirName));
            Directory.CreateDirectory(Path.Combine(folder, LogsDirName));
        }

        /// <summary>
        /// Subpasta de uma atividade: "id - título", com o título limpo dos caracteres que o
        /// Windows não aceita. O id vem na frente de propósito: renomear a atividade não quebra o
        /// vínculo com a pasta já existente.
        /// </summary>
        public static string ActivityFolder(string folder, int id, string title)
        {
            var limpo = new string((title ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
            if (limpo.Length > 60) limpo = limpo.Substring(0, 60).Trim();
            var nome = string.IsNullOrEmpty(limpo) ? id.ToString(CultureInfo.InvariantCulture) : $"{id} - {limpo}";
            return Path.Combine(folder, ArtifactsDirName, nome);
        }

        /// <summary>
        /// Arquivos anexados a uma atividade. Procura pela pasta que COMEÇA com o id, para o
        /// título ter mudado sem perder os anexos.
        /// </summary>
        public static IReadOnlyList<string> Attachments(string folder, int id)
        {
            try
            {
                var raiz = Path.Combine(folder, ArtifactsDirName);
                if (!Directory.Exists(raiz)) return Array.Empty<string>();
                var prefixo = id.ToString(CultureInfo.InvariantCulture);
                var dir = Directory.EnumerateDirectories(raiz).FirstOrDefault(d =>
                {
                    var n = Path.GetFileName(d);
                    return n.StartsWith(prefixo + " ", StringComparison.Ordinal)
                        || string.Equals(n, prefixo, StringComparison.Ordinal);
                });
                if (dir == null) return Array.Empty<string>();
                return Directory.EnumerateFiles(dir).OrderBy(f => f).ToList();
            }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Copia o arquivo para a pasta da atividade. Devolve o destino, ou "" se falhou.</summary>
        public static string Attach(string folder, int id, string title, string sourceFile)
        {
            try
            {
                var dir = ActivityFolder(folder, id, title);
                Directory.CreateDirectory(dir);
                var destino = Path.Combine(dir, Path.GetFileName(sourceFile));
                // Mesmo nome duas vezes: numera, em vez de sobrescrever o que já estava lá.
                var baseName = Path.GetFileNameWithoutExtension(destino);
                var ext = Path.GetExtension(destino);
                var n = 1;
                while (File.Exists(destino))
                    destino = Path.Combine(dir, $"{baseName} ({n++}){ext}");
                File.Copy(sourceFile, destino);
                return destino;
            }
            catch { return ""; }
        }

        // ── Log de BLOCK ────────────────────────────────────────────────────────────────────

        /// <summary>Um evento de bloqueio/liberação, como gravado no log.</summary>
        public sealed record BlockEvent(int Id, string Acao, DateTime Quando, string Quem, string Motivo);

        public static string BlockLogPath(string folder) =>
            Path.Combine(folder, LogsDirName, BlockLogFileName);

        /// <summary>Acrescenta um evento. Nunca lança: log não pode derrubar o board.</summary>
        public static void AppendBlock(string folder, int id, bool blocked, string who, string? reason = null)
        {
            try
            {
                var path = BlockLogPath(folder);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var linha = JsonSerializer.Serialize(new
                {
                    id,
                    acao = blocked ? "block" : "release",
                    quando = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    quem = who ?? "",
                    motivo = reason ?? ""
                });
                File.AppendAllText(path, linha + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* o board continua, o log é diagnóstico */ }
        }

        /// <summary>
        /// Lê os eventos de uma atividade, na ordem em que aconteceram. Linha fora do formato é
        /// pulada — arquivo editado à mão não pode cegar a auditoria inteira.
        /// </summary>
        public static IReadOnlyList<BlockEvent> ReadBlockEvents(string folder, int id)
        {
            var list = new List<BlockEvent>();
            try
            {
                var path = BlockLogPath(folder);
                if (!File.Exists(path)) return list;
                foreach (var linha in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(linha)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(linha);
                        var r = doc.RootElement;
                        if (!r.TryGetProperty("id", out var idp) || idp.GetInt32() != id) continue;
                        var quando = r.TryGetProperty("quando", out var q)
                            && DateTime.TryParse(q.GetString(), CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out var dt) ? dt : DateTime.MinValue;
                        list.Add(new BlockEvent(id,
                            r.TryGetProperty("acao", out var a) ? a.GetString() ?? "" : "",
                            quando,
                            r.TryGetProperty("quem", out var w) ? w.GetString() ?? "" : "",
                            r.TryGetProperty("motivo", out var m) ? m.GetString() ?? "" : ""));
                    }
                    catch { /* linha estragada: ignora e segue */ }
                }
            }
            catch { /* sem log, sem auditoria — e sem erro na tela */ }
            return list.OrderBy(e => e.Quando).ToList();
        }

        /// <summary>
        /// Horas úteis em BLOCK somando os pares block→release do log. Bloqueio ainda aberto
        /// conta até agora — é o que a pessoa quer ver enquanto o impedimento dura.
        /// </summary>
        public static double BlockedHours(IReadOnlyList<BlockEvent> events, Func<DateTime, DateTime, double> workingHours)
        {
            double total = 0;
            DateTime? aberto = null;
            foreach (var e in events)
            {
                if (string.Equals(e.Acao, "block", StringComparison.OrdinalIgnoreCase))
                    aberto ??= e.Quando;
                else if (aberto is { } ini)
                {
                    total += workingHours(ini, e.Quando);
                    aberto = null;
                }
            }
            if (aberto is { } emAberto) total += workingHours(emAberto, DateTime.Now);
            return total;
        }
    }
}
