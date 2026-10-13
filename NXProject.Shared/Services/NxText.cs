// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;

namespace NXProject.Services
{
    /// <summary>
    /// Texto de mensagem dos SERVIÇOS, nos dois idiomas.
    ///
    /// As telas usam `AppStrings` e os arquivos `Strings.*.xaml`, que vivem no NXProject.Community.
    /// Os serviços estão no NXProject.Shared e não enxergam nada disso — foi por essa fronteira
    /// que todo aviso de importação e de sincronização acabou fixo em português, inclusive para
    /// quem usa o aplicativo em inglês.
    ///
    /// Aqui as duas versões ficam LADO A LADO na chamada:
    ///
    ///     NxText.T("#{0} não está neste board.", "#{0} is not on this board.", numero)
    ///
    /// É deliberado não ser um catálogo de chaves. Mensagem de serviço nasce junto do código que
    /// a produz, e separá-la num arquivo distante foi exatamente o que fez as duas se perderem:
    /// com o texto ao lado, quem muda a regra vê as duas versões e não tem como esquecer uma.
    /// Chave continua sendo o certo para a interface, que tem milhares delas e muda de idioma em
    /// tempo de execução.
    /// </summary>
    public static class NxText
    {
        /// <summary>
        /// Idioma em vigor. Quem define é o aplicativo, no arranque — o Shared não tem como saber
        /// sozinho qual idioma a pessoa escolheu.
        /// </summary>
        public static string Language { get; set; } = "pt-BR";

        /// <summary>Está em inglês? Qualquer variante (en-US, en-GB) conta.</summary>
        public static bool IsEnglish =>
            (Language ?? "").StartsWith("en", StringComparison.OrdinalIgnoreCase);

        /// <summary>A versão do texto no idioma em vigor.</summary>
        public static string T(string pt, string en) => IsEnglish ? (en ?? pt) : pt;

        /// <summary>
        /// Idem, com parâmetros. Formata DEPOIS de escolher o idioma, porque a ordem dos
        /// parâmetros pode mudar entre as duas versões.
        /// </summary>
        public static string T(string pt, string en, params object?[] args)
        {
            var modelo = T(pt, en);
            try { return string.Format(modelo, args ?? Array.Empty<object?>()); }
            catch (FormatException) { return modelo; }   // texto nunca derruba a operação
        }
    }
}
