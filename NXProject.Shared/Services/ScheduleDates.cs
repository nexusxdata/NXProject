// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;

namespace NXProject.Services
{
    /// <summary>
    /// Data de cronograma que NÃO EXISTE — e como reconhecê-la.
    ///
    /// Em C# uma data não preenchida é <c>01/01/0001</c>, e esse valor atravessava o NXProject
    /// como se fosse uma data de verdade: o banner mostrava "Início: 01/01/01", o Gantt esticava
    /// até o ano 1 e a atividade parecia a mais antiga do projeto. Reconhecer isso num lugar só
    /// evita que cada tela invente o seu próprio limite.
    ///
    /// O corte é 1900: nenhum cronograma real começa antes disso, e qualquer data abaixo veio de
    /// campo vazio — do import, de arquivo antigo ou de item criado sem data.
    /// </summary>
    public static class ScheduleDates
    {
        /// <summary>Primeiro ano que vale como data de cronograma.</summary>
        public const int FirstRealYear = 1900;

        /// <summary>A data está ausente (nunca foi preenchida)?</summary>
        public static bool IsMissing(DateTime data) => data.Year < FirstRealYear;

        /// <inheritdoc cref="IsMissing(DateTime)"/>
        public static bool IsMissing(DateTime? data) => data is not { } d || IsMissing(d);

        /// <summary>A data, ou o padrão quando ela está ausente.</summary>
        public static DateTime OrDefault(DateTime data, DateTime padrao) =>
            IsMissing(data) ? padrao : data;
    }
}
