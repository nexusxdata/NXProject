// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.IO;
using System.Text.Json;

namespace NXProject.Services
{
    /// <summary>
    /// Preferências de EXIBIÇÃO do cronograma — hoje só a hierarquia com que ele abre.
    ///
    /// Fica num arquivo próprio, e não no windowstate.json, porque não é estado de janela: é
    /// escolha de trabalho, e some junto com a configuração do usuário se um dia ele resetar a
    /// janela. Cronograma grande abre fechado por padrão (só o nível de cima), que era o pedido:
    /// ninguém precisa ver mil linhas para começar.
    /// </summary>
    public static class ScheduleViewStore
    {
        /// <summary>Hierarquia ao abrir: 1 = só o nível de cima, 2 = dois níveis, 0 = tudo.</summary>
        public sealed class ScheduleViewPrefs
        {
            public int HierarchyLevels { get; set; } = 1;
        }

        public static ScheduleViewPrefs Load(string storageKey = "NXProject.Community")
        {
            try
            {
                var arquivo = Arquivo(storageKey);
                if (!File.Exists(arquivo)) return new ScheduleViewPrefs();
                return JsonSerializer.Deserialize<ScheduleViewPrefs>(File.ReadAllText(arquivo))
                       ?? new ScheduleViewPrefs();
            }
            catch { return new ScheduleViewPrefs(); }
        }

        public static void Save(ScheduleViewPrefs prefs, string storageKey = "NXProject.Community")
        {
            try
            {
                var arquivo = Arquivo(storageKey);
                Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
                File.WriteAllText(arquivo,
                    JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* preferencia de tela nunca derruba o app */ }
        }

        private static string Arquivo(string storageKey) => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            string.IsNullOrWhiteSpace(storageKey) ? "NXProject.Community" : storageKey,
            "schedule-view.json");
    }
}
