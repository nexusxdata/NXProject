// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NXProject.Services
{
    /// <summary>
    /// O WIQL de uma query do DevOps visto como LISTA DE CLÁUSULAS — o que a tela de filtros do
    /// Azure DevOps mostra (E/Ou, Campo, Operador, Valor) e que aqui permite montar a consulta
    /// sem escrever WIQL à mão.
    ///
    /// O recorte é deliberado: só o <c>WHERE</c> é editado em cláusulas. O <c>SELECT</c>, o
    /// <c>FROM</c> e o <c>ORDER BY</c> originais são preservados palavra por palavra, porque são
    /// o que menos muda e o que mais quebra quando reescrito.
    ///
    /// Quando o WHERE tem algo que esta leitura não garante entender — parênteses, subconsulta,
    /// <c>EVER</c> — a propriedade <see cref="Editable"/> vem falsa e a tela cai para o modo
    /// texto. Pior que não oferecer o editor visual seria oferecer e devolver uma query diferente
    /// da que a pessoa tinha.
    /// </summary>
    public sealed class WiqlQueryModel
    {
        /// <summary>Uma linha do filtro: "E/Ou", campo, operador e valor.</summary>
        public sealed class Clause
        {
            /// <summary>"And" ou "Or"; vazio na primeira linha.</summary>
            public string Connector { get; set; } = "And";
            /// <summary>Nome de referência do campo (ex.: System.WorkItemType).</summary>
            public string Field { get; set; } = "";
            /// <summary>Operador WIQL (=, &lt;&gt;, &gt;, Contains, Under, In…).</summary>
            public string Operator { get; set; } = "=";
            /// <summary>Valor como texto, sem as aspas do WIQL.</summary>
            public string Value { get; set; } = "";
        }

        /// <summary>Operadores oferecidos na tela, na ordem em que fazem sentido.</summary>
        public static readonly string[] Operators =
        {
            "=", "<>", ">", "<", ">=", "<=",
            "Contains", "Does Not Contain",
            "In", "Not In",
            "Under", "Not Under",
            "Is Empty", "Is Not Empty"
        };

        /// <summary>Operadores que não levam valor.</summary>
        public static bool OperatorHasNoValue(string op) =>
            op is "Is Empty" or "Is Not Empty";

        public string SelectPart { get; private set; } = "";
        public string FromPart { get; private set; } = "";
        public string OrderByPart { get; private set; } = "";
        public List<Clause> Clauses { get; } = new();

        /// <summary>O WHERE foi entendido por completo? Falso ⇒ a tela só oferece o texto.</summary>
        public bool Editable { get; private set; }

        /// <summary>Por que não é editável (para a tela explicar em vez de só desabilitar).</summary>
        public string NotEditableReason { get; private set; } = "";

        /// <summary>
        /// Lê o WIQL e devolve o modelo. Nunca lança: WIQL estranho vira modelo não editável, com
        /// o texto original preservado.
        /// </summary>
        public static WiqlQueryModel Parse(string wiql)
        {
            var model = new WiqlQueryModel();
            var text = (wiql ?? "").Trim();
            if (text.Length == 0) { model.NotEditableReason = "WIQL vazio"; return model; }

            var iWhere = IndexOfKeyword(text, "WHERE");
            var iOrder = IndexOfKeyword(text, "ORDER BY");

            if (iWhere < 0)
            {
                model.SelectPart = (iOrder >= 0 ? text.Substring(0, iOrder) : text).Trim();
                model.OrderByPart = iOrder >= 0 ? text.Substring(iOrder).Trim() : "";
                model.Editable = true;            // sem WHERE: dá para começar a montar do zero
                return model;
            }

            model.SelectPart = text.Substring(0, iWhere).Trim();
            var whereEnd = iOrder > iWhere ? iOrder : text.Length;
            var where = text.Substring(iWhere + "WHERE".Length, whereEnd - iWhere - "WHERE".Length).Trim();
            model.OrderByPart = iOrder > iWhere ? text.Substring(iOrder).Trim() : "";

            if (where.Contains('(') || where.Contains(')'))
            {
                model.NotEditableReason = "a consulta usa parênteses";
                return model;
            }

            foreach (var (connector, piece) in SplitTopLevel(where))
            {
                var clause = ParseClause(piece);
                if (clause == null)
                {
                    model.Clauses.Clear();
                    model.NotEditableReason = "não reconheci a condição: " + piece.Trim();
                    return model;
                }
                clause.Connector = model.Clauses.Count == 0 ? "" : connector;
                model.Clauses.Add(clause);
            }
            model.Editable = true;
            return model;
        }

        /// <summary>Monta o WIQL de volta: SELECT/FROM/ORDER BY originais + WHERE das cláusulas.</summary>
        public string Build()
        {
            var sb = new StringBuilder();
            sb.Append(SelectPart.Trim());
            var validas = Clauses.Where(c => !string.IsNullOrWhiteSpace(c.Field)).ToList();
            if (validas.Count > 0)
            {
                sb.Append(Environment.NewLine).Append("WHERE ");
                for (var i = 0; i < validas.Count; i++)
                {
                    var c = validas[i];
                    if (i > 0)
                        sb.Append(Environment.NewLine)
                          .Append("  ").Append(string.IsNullOrWhiteSpace(c.Connector) ? "AND" : c.Connector.ToUpperInvariant())
                          .Append(' ');
                    sb.Append('[').Append(c.Field.Trim('[', ']', ' ')).Append("] ").Append(c.Operator);
                    if (!OperatorHasNoValue(c.Operator))
                        sb.Append(' ').Append(FormatValue(c.Value));
                }
            }
            if (!string.IsNullOrWhiteSpace(OrderByPart))
                sb.Append(Environment.NewLine).Append(OrderByPart.Trim());
            return sb.ToString();
        }

        /// <summary>
        /// Valor como o WIQL espera: número e macro (@Me, @Today, @StartOfDay('-20d')) vão crus;
        /// lista do IN vai entre parênteses; o resto vai entre aspas simples, com escape.
        /// </summary>
        private static string FormatValue(string? value)
        {
            var v = (value ?? "").Trim();
            if (v.Length == 0) return "''";
            if (v.StartsWith("@", StringComparison.Ordinal)) return v;
            if (v.StartsWith("(", StringComparison.Ordinal)) return v;
            if (double.TryParse(v, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out _)) return v;
            return "'" + v.Replace("'", "''") + "'";
        }

        /// <summary>Lê "[Campo] Operador valor" numa cláusula; null quando não reconhece.</summary>
        private static Clause? ParseClause(string piece)
        {
            var s = piece.Trim();
            if (s.Length == 0) return null;

            string field;
            if (s.StartsWith("[", StringComparison.Ordinal))
            {
                var close = s.IndexOf(']');
                if (close < 0) return null;
                field = s.Substring(1, close - 1).Trim();
                s = s.Substring(close + 1).Trim();
            }
            else
            {
                var sp = s.IndexOf(' ');
                if (sp < 0) return null;
                field = s.Substring(0, sp).Trim();
                s = s.Substring(sp + 1).Trim();
            }

            // Operadores mais longos primeiro: "Not Under" antes de "Under", ">=" antes de ">".
            var op = Operators
                .OrderByDescending(o => o.Length)
                .FirstOrDefault(o => s.StartsWith(o, StringComparison.OrdinalIgnoreCase));
            if (op == null) return null;
            var rest = s.Substring(op.Length).Trim();

            var value = rest;
            if (value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal)
                && value.Length >= 2)
                value = value.Substring(1, value.Length - 2).Replace("''", "'");

            return new Clause { Field = field, Operator = op, Value = value };
        }

        /// <summary>Quebra o WHERE nos AND/OR de topo, respeitando o que está entre aspas.</summary>
        private static List<(string Connector, string Piece)> SplitTopLevel(string where)
        {
            var parts = new List<(string, string)>();
            var atual = new StringBuilder();
            var connector = "";
            var emAspas = false;

            for (var i = 0; i < where.Length; i++)
            {
                var ch = where[i];
                if (ch == '\'')
                {
                    // '' dentro de texto é aspas escapada, não fim de literal.
                    if (emAspas && i + 1 < where.Length && where[i + 1] == '\'')
                    { atual.Append("''"); i++; continue; }
                    emAspas = !emAspas;
                    atual.Append(ch);
                    continue;
                }
                if (!emAspas && IsKeywordAt(where, i, "AND"))
                {
                    parts.Add((connector, atual.ToString()));
                    connector = "And"; atual.Clear(); i += 2; continue;
                }
                if (!emAspas && IsKeywordAt(where, i, "OR"))
                {
                    parts.Add((connector, atual.ToString()));
                    connector = "Or"; atual.Clear(); i += 1; continue;
                }
                atual.Append(ch);
            }
            parts.Add((connector, atual.ToString()));
            return parts.Where(p => p.Item2.Trim().Length > 0).ToList();
        }

        /// <summary>A palavra-chave começa exatamente aqui, cercada por espaço?</summary>
        private static bool IsKeywordAt(string text, int i, string keyword)
        {
            if (i + keyword.Length > text.Length) return false;
            if (string.Compare(text, i, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            if (i > 0 && !char.IsWhiteSpace(text[i - 1])) return false;
            var after = i + keyword.Length;
            return after >= text.Length || char.IsWhiteSpace(text[after]);
        }

        /// <summary>Posição da palavra-chave fora de aspas; -1 quando não existe.</summary>
        private static int IndexOfKeyword(string text, string keyword)
        {
            var emAspas = false;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\'') emAspas = !emAspas;
                else if (!emAspas && IsKeywordAt(text, i, keyword)) return i;
            }
            return -1;
        }
    }
}
