// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NXProject.Services
{
    /// <summary>
    /// Bloqueio (BLOCK) de Story e Task no DevOps, num lugar só. Antes cada tela montava a tag,
    /// gravava e torcia: o TaskBoard e a sincronização acabaram se comportando diferente — o
    /// desbloqueio do board mandava um PATCH de uma operação só com as tags vazias, que o DevOps
    /// aceita (200) e NÃO aplica. Aqui a operação é completa e igual para todo mundo:
    ///
    ///   1. tag de bloqueio ligada/desligada, preservando as demais tags;
    ///   2. trâmite (System.History) na MESMA revisão — deixa o motivo visível no DevOps e, de
    ///      quebra, garante a segunda operação no PATCH;
    ///   3. conferência do que o DevOps realmente gravou (não confia no 200);
    ///   4. duração total do impedimento no campo configurado, quando habilitado.
    ///
    /// Use por TaskBoard, Export → Sincronizar, grade de Task do tech lead e o que vier depois.
    /// </summary>
    public static class BlockService
    {
        /// <summary>Resultado de bloquear/desbloquear.</summary>
        /// <param name="Tags">Tags que ficaram no item.</param>
        public sealed record BlockResult(bool Ok, string Message, string Tags);

        /// <summary>Marca ou desmarca o bloqueio. <paramref name="currentTags"/> são as tags que o
        /// chamador conhece (as do board/cronograma), usadas para preservar as demais.</summary>
        public static async Task<BlockResult> SetBlockedAsync(
            TfsConnectionOptions options, int id, bool blocked, string? currentTags,
            string? byUser = null, string? note = null, CancellationToken ct = default)
        {
            if (options == null || id <= 0) return new BlockResult(false, "item inválido", currentTags ?? "");

            var tag = string.IsNullOrWhiteSpace(options.BlockedTagName) ? "BLOCK" : options.BlockedTagName.Trim();
            var newTags = TfsImportService.ToggleTag(currentTags, tag, blocked);
            var comment = string.IsNullOrWhiteSpace(note) ? DefaultNote(blocked, tag, byUser) : note!;

            var (ok, msg) = await TfsImportService.SetWorkItemTagsAsync(
                options, id, newTags, ct, historyComment: comment);
            return new BlockResult(ok, msg, ok ? newTags : (currentTags ?? ""));
        }

        private static string DefaultNote(bool blocked, string tag, string? byUser)
        {
            var who = string.IsNullOrWhiteSpace(byUser) ? "NXProject" : byUser!.Trim();
            return blocked
                ? $"🔒 Bloqueada no NXProject (tag {tag}) por {who}."
                : $"🔓 Desbloqueada no NXProject (tag {tag} removida) por {who}.";
        }

        /// <summary>
        /// Está habilitado gravar a duração do impedimento? Exige o checkbox ligado E o nome do
        /// campo preenchido — sem isso o NX não toca no item, que é o combinado para quem não
        /// tem (ou não pode criar) o campo no processo do DevOps.
        /// </summary>
        public static bool DurationEnabled(TfsConnectionOptions options) =>
            options is { BlockDurationFieldEnabled: true }
            && !string.IsNullOrWhiteSpace(options.BlockDurationFieldName);

        /// <summary>
        /// Marca o campo de duração com 1 ao BLOQUEAR. É só uma marca de "impedida agora": serve
        /// para o card mostrar o ícone de auditoria sem ninguém ler o histórico. O valor real só
        /// é calculado no desbloqueio.
        /// </summary>
        public static Task<(bool Ok, string Message)> MarkBlockedAsync(
            TfsConnectionOptions options, int id, CancellationToken ct = default) =>
            DurationEnabled(options)
                ? TfsImportService.SetNumericFieldAsync(options, id, options.BlockDurationFieldName, 1, ct)
                : Task.FromResult((true, string.Empty));

        /// <summary>
        /// Recalcula o tempo TOTAL de impedimento do item a partir do histórico do DevOps e grava
        /// no campo configurado, em horas inteiras (menos de 1 hora vira 0).
        ///
        /// O total vem SEMPRE do histórico, nunca de somar em cima do que está no campo: o 1
        /// gravado no bloqueio é marca, e um valor editado à mão no DevOps propagaria o erro para
        /// sempre. As horas são as ÚTEIS do calendário do projeto — impedimento que atravessa o
        /// fim de semana não custou 48 h de trabalho.
        ///
        /// Chame DEPOIS de liberar a tela: é uma leitura de histórico por item, e o usuário não
        /// pode ficar esperando por ela. Falhar aqui não desfaz o desbloqueio, que já foi aplicado.
        /// </summary>
        public static async Task<(bool Ok, string Message)> UpdateTotalDurationAsync(
            TfsConnectionOptions options, int id, CancellationToken ct = default)
        {
            if (!DurationEnabled(options)) return (true, string.Empty);
            try
            {
                var audit = await TfsImportService.LoadBlockAuditAsync(options, id, ct);
                if (audit == null) return (true, string.Empty);

                var now = DateTime.Now;
                var hours = audit.Periods.Sum(p =>
                    Math.Max(0, ProjectCalendarService.CountWorkingHours(p.Start, p.End ?? now)));
                var whole = (int)Math.Floor(hours);      // menos de 1 hora vira 0, por combinado

                return await TfsImportService.SetNumericFieldAsync(
                    options, id, options.BlockDurationFieldName, whole, ct);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
