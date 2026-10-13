// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Linq;
using System.Windows;
using NXProject.Services;

namespace NXProject.Views
{
    /// <summary>
    /// Conexão com o GitHub Projects: dono, token e qual board.
    ///
    /// Pede **dono + número do board**, e não um nome, porque é assim que o GitHub identifica
    /// projeto: a URL termina em /projects/7, e renomear o board não muda esse número. O botão
    /// "listar" existe justamente para ninguém precisar decorá-lo.
    /// </summary>
    public partial class GitProjectConfigWindow : Window
    {
        private readonly TfsConnectionOptions _options = TfsConnectionStore.Load();

        public GitProjectConfigWindow()
        {
            InitializeComponent();
            var git = _options.GitProject ?? new GitProjectOptions();
            OwnerBox.Text = git.Owner;
            IsOrgBox.IsChecked = git.OwnerIsOrganization;
            TokenBox.Password = git.Token;
            RepoBox.Text = git.Repository;
            if (git.ProjectNumber > 0)
            {
                BoardCombo.Items.Add($"#{git.ProjectNumber}");
                BoardCombo.SelectedIndex = 0;
            }
            Loaded += (_, _) => OwnerBox.Focus();
        }

        /// <summary>O que está na tela agora, sem gravar.</summary>
        private GitProjectOptions Atual()
        {
            // Colar a URL é o gesto natural de quem está com o board aberto no navegador; exigir
            // "só o login" seria cobrar tradução de quem tem a informação inteira na mão.
            AbsorveUrl();
            // Parte da configuração JÁ SALVA (nomes de campo, tags, classificação) e só troca a
            // conexão: criar um objeto novo aqui apagava tudo o que a tela de campos tinha
            // configurado, sem avisar.
            var git = (_options.GitProject ?? new GitProjectOptions()).Clone();
            git.Owner = OwnerBox.Text?.Trim() ?? "";
            git.OwnerIsOrganization = IsOrgBox.IsChecked == true;
            git.Token = TokenBox.Password ?? "";
            git.ProjectNumber = NumeroEscolhido();
            git.Repository = RepoBox.Text?.Trim() ?? "";
            return git;
        }

        /// <summary>
        /// Entende o que foi colado no campo do dono e preenche o resto. A URL do GitHub já diz
        /// tudo o que esta tela pergunta:
        ///
        ///   https://github.com/users/carmonexus/projects/1  → dono, conta pessoal, board 1
        ///   https://github.com/orgs/nexusxdata/projects/3   → dono, organização, board 3
        ///   https://github.com/carmonexus?tab=projects      → dono, conta pessoal
        ///
        /// Inclusive o "é organização", que é a parte em que mais se erra — e o erro vem com uma
        /// mensagem da API que não ajuda ("Could not resolve to an Organization").
        /// </summary>
        private void AbsorveUrl()
        {
            var txt = OwnerBox.Text?.Trim() ?? "";
            if (txt.Length == 0 || !txt.Contains('/')) return;

            var m = System.Text.RegularExpressions.Regex.Match(txt,
                @"github\.com/(?:(users|orgs)/)?([^/?#]+)(?:/projects/(\d+))?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return;

            var tipo = m.Groups[1].Value.ToLowerInvariant();
            OwnerBox.Text = m.Groups[2].Value;
            if (tipo.Length > 0) IsOrgBox.IsChecked = tipo == "orgs";
            if (m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out var n) && n > 0)
            {
                var rotulo = $"#{n}";
                if (!BoardCombo.Items.Contains(rotulo)) BoardCombo.Items.Insert(0, rotulo);
                BoardCombo.SelectedItem = rotulo;
            }
        }

        /// <summary>
        /// Número do board a partir do item escolhido ("#7 Meu board" → 7). O número é a chave
        /// no GitHub; o título vai junto só para a pessoa reconhecer.
        /// </summary>
        private int NumeroEscolhido()
        {
            var txt = BoardCombo.SelectedItem?.ToString() ?? "";
            var num = new string(txt.TakeWhile(c => c == '#' || char.IsDigit(c)).Where(char.IsDigit).ToArray());
            return int.TryParse(num, out var n) ? n : 0;
        }

        private async void OnListClick(object sender, RoutedEventArgs e)
        {
            var git = Atual();
            if (string.IsNullOrWhiteSpace(git.Owner) || string.IsNullOrWhiteSpace(git.Token))
            {
                StatusText.Text = AppStrings.Get("Git_NeedOwnerToken");
                return;
            }
            StatusText.Text = AppStrings.Get("Git_Listing");
            try
            {
                var boards = await GitProjectDiscovery.ListBoardsAsync(git);
                BoardCombo.Items.Clear();
                foreach (var (n, t) in boards) BoardCombo.Items.Add($"#{n} {t}");
                if (BoardCombo.Items.Count > 0) BoardCombo.SelectedIndex = 0;
                StatusText.Text = boards.Count == 0
                    ? AppStrings.Get("Git_NoBoards", git.Owner)
                    : AppStrings.Get("Git_Found", boards.Count.ToString());
            }
            catch (Exception ex) { StatusText.Text = Explica(ex); }
        }

        private async void OnTestClick(object sender, RoutedEventArgs e)
        {
            var git = Atual();
            if (!git.IsConfigured) { StatusText.Text = AppStrings.Get("Git_NeedAll"); return; }
            StatusText.Text = AppStrings.Get("Git_Testing");
            try
            {
                var board = await new GitProjectBoardSource(git).BuildBoardAsync(Array.Empty<string>());
                var sprints = await new GitProjectBoardSource(git).ListSprintsAsync();
                var cards = board.Stories.Sum(s => s.Tasks.Count);
                StatusText.Text = AppStrings.Get("Git_TestOk",
                    board.Stories.Count.ToString(), cards.ToString(),
                    board.LevelItems.Count.ToString(), sprints.Count.ToString(),
                    string.Join(" | ", board.States));
            }
            catch (Exception ex) { StatusText.Text = Explica(ex); }
        }

        /// <summary>
        /// Erro de escopo é o tropeço mais comum e tem conserto conhecido: em vez de repetir a
        /// mensagem crua da API, diz o que fazer.
        /// </summary>
        private static string Explica(Exception ex)
        {
            var msg = ex.Message ?? "";
            // "Resource not accessible by personal access token" e a resposta do GitHub a um
            // token FINE-GRAINED pedindo um board de conta pessoal: para esses boards a
            // permissao de Projects nao existe no fine-grained, so no token classico. A mensagem
            // crua nao diz nada disso, e a pessoa fica refazendo o token igual.
            if (msg.Contains("not accessible by personal access token", StringComparison.OrdinalIgnoreCase))
                return AppStrings.Get("Git_FineGrainedError", msg);
            return msg.Contains("read:project", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("INSUFFICIENT_SCOPES", StringComparison.OrdinalIgnoreCase)
                ? AppStrings.Get("Git_ScopeError", msg)
                : msg;
        }

        /// <summary>Abre a pagina de tokens do GitHub no navegador padrao.</summary>
        private void OnHyperlinkRequestNavigate(object sender,
            System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch { /* sem navegador padrao: o link continua visivel para copiar */ }
            e.Handled = true;
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var git = Atual();
            if (!git.IsConfigured) { StatusText.Text = AppStrings.Get("Git_NeedAll"); return; }
            _options.GitProject = git;
            // Token cifrado pelo Windows, como o PAT do DevOps — nunca em texto plano.
            TfsConnectionStore.Save(_options, rememberToken: true);
            DialogResult = true;
        }
    }
}
