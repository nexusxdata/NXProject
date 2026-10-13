// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Windows;

namespace NXProject.Setup;

/// <summary>
/// Passo 4: campos do NXProject no servidor.
///
/// Esta janela já teve a lista inteira de campos, com detectar, criar e exportar — uma cópia da
/// tela "Configurar Campos e Tags" do NXProject, alimentada pelo mesmo catálogo. Duas telas para
/// a mesma configuração é o jeito mais confiável de fazê-las divergir, e com o catálogo de campos
/// isso já aconteceu três vezes.
///
/// Hoje ela ENCAMINHA: o NXProject detecta, cria (pedindo o token de administrador na hora, sem
/// gravar), confere o mapa de níveis e exporta/importa o .json com tudo — campos do Azure DevOps,
/// campos e tags do GitProject e os níveis. O instalador só escolhe em que aba abrir.
///
/// O encaminhamento é seguro no fluxo do assistente: o Passo 4 vem DEPOIS da instalação, então o
/// executável do NXProject já existe. Se não existir, a janela diz isso em vez de não fazer nada.
/// </summary>
public partial class DevOpsFieldsWindow : Window
{
    public DevOpsFieldsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Abre a tela de campos do NXProject já na aba do destino escolhido.
    ///
    /// A pasta é a MESMA em que o instalador grava (ver <c>MainWindow._installDir</c>): procurar
    /// noutro lugar diria "não instalado" para quem acabou de instalar.
    /// </summary>
    /// <summary>
    /// Onde está o NXProject nesta máquina.
    ///
    /// Procura em três lugares, nesta ordem: a pasta em que o instalador grava, a pasta do
    /// próprio Setup (quando os dois foram copiados juntos) e a saída de compilação do
    /// repositório. O terceiro existe porque na máquina de DESENVOLVIMENTO o NXProject não está
    /// "instalado" — e mandar instalar para testar o botão seria um incômodo inventado.
    /// </summary>
    private static string? AchaNXProject()
    {
        var candidatos = new System.Collections.Generic.List<string>
        {
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                   "Apps", "NXProject", "NXProject.Community.exe"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "NXProject.Community.exe"),
        };

        // Repositório: sobe da pasta de saída do Setup até a raiz e procura a saída do Community.
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            var tentativa = System.IO.Path.Combine(dir.FullName, "NXProject.Community", "bin",
                "Release", "net10.0-windows", "NXProject.Community.exe");
            candidatos.Add(tentativa);
        }

        foreach (var c in candidatos)
            if (System.IO.File.Exists(c)) return c;
        return null;
    }

    private void OnOpenNxFieldsClick(object sender, RoutedEventArgs e)
    {
        var exe = AchaNXProject();

        if (exe == null)
        {
            Step4StatusText.Text = App.Str("Setup_Step4OpenNxMissing");
            MessageBox.Show(this, App.Str("Setup_Step4OpenNxMissing"),
                App.Str("Setup_Step4OpenNx"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // A tela abre com as DUAS abas: o instalador não precisa escolher por quem vai usar.
        const string arg = "--setup";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, arg)
            { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(exe)! });
            Step4StatusText.Text = App.Str("Setup_Step4OpenNxDone");
        }
        catch (Exception ex)
        {
            Step4StatusText.Text = ex.Message;
        }
    }
}
