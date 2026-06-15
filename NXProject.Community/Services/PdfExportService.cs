using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NXProject.Community.Services
{
    internal static class PdfExportService
    {
        // Margens em pontos (1 pt = 1/72 polegada)
        private const double MarginPt   = 20;
        private const double FooterH    = 28; // altura da faixa do rodapé
        private const double FooterLine = 1;

        /// <summary>
        /// Renderiza <paramref name="visual"/> em um PDF paisagem A4 e salva em <paramref name="filePath"/>.
        /// </summary>
        /// <param name="visual">O elemento WPF que será capturado (deve estar visível e medido).</param>
        /// <param name="projectName">Nome do projeto — aparece no rodapé esquerdo.</param>
        /// <param name="logoBitmap">Logo da empresa para o rodapé direito (pode ser null).</param>
        public static void Export(FrameworkElement visual, string projectName, BitmapImage? logoBitmap, string filePath)
        {
            // 1. Captura o visual como PNG em alta resolução
            var pngBytes = RenderToPng(visual, dpi: 150);

            // 2. Cria documento PDF A4 paisagem
            var doc  = new PdfDocument();
            doc.Info.Title   = projectName;
            doc.Info.Subject = "Cronograma NXProject";
            doc.Info.Creator = "NXProject Community";

            var page = doc.AddPage();
            page.Orientation = PdfSharp.PageOrientation.Landscape;
            page.Size        = PdfSharp.PageSize.A4;

            using var gfx = XGraphics.FromPdfPage(page);

            double pageW = page.Width.Point;
            double pageH = page.Height.Point;

            // 3. Desenha a imagem do cronograma
            double imgX = MarginPt;
            double imgY = MarginPt;
            double imgW = pageW - MarginPt * 2;
            double imgH = pageH - MarginPt - FooterH - FooterLine - 6 - MarginPt * 0.5;

            using var imgStream = new MemoryStream(pngBytes);
            using var xImg = XImage.FromStream(imgStream);
            gfx.DrawImage(xImg, imgX, imgY, imgW, imgH);

            // 4. Rodapé
            DrawFooter(gfx, pageW, pageH, projectName, logoBitmap);

            doc.Save(filePath);
        }

        // ------------------------------------------------------------------ helpers

        private static byte[] RenderToPng(FrameworkElement element, double dpi)
        {
            var scale  = dpi / 96.0;
            int pixelW = (int)Math.Round(element.ActualWidth  * scale);
            int pixelH = (int)Math.Round(element.ActualHeight * scale);

            if (pixelW <= 0 || pixelH <= 0)
                throw new InvalidOperationException("O elemento a ser exportado não tem tamanho renderizável.");

            var rtb = new RenderTargetBitmap(pixelW, pixelH, dpi, dpi, PixelFormats.Pbgra32);

            var drawingVisual = new DrawingVisual();
            using (var ctx = drawingVisual.RenderOpen())
            {
                var brush = new VisualBrush(element);
                ctx.DrawRectangle(brush, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }
            rtb.Render(drawingVisual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        private static void DrawFooter(XGraphics gfx, double pageW, double pageH,
                                       string projectName, BitmapImage? logoBitmap)
        {
            double lineY  = pageH - FooterH - FooterLine;
            double textY  = lineY + FooterLine + 6;
            double midY   = textY + (FooterH - FooterLine) / 2.0 - 5;

            // Linha separadora
            var linePen = new XPen(XColor.FromArgb(180, 180, 180), FooterLine);
            gfx.DrawLine(linePen, MarginPt, lineY, pageW - MarginPt, lineY);

            // Nome do projeto (esquerda)
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                var font = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
                gfx.DrawString(projectName, font, XBrushes.DarkSlateGray,
                    new XRect(MarginPt, midY, pageW / 2.0, 14), XStringFormats.CenterLeft);
            }

            // Data de exportação (centro)
            {
                var font  = new XFont("Segoe UI", 8, XFontStyleEx.Regular);
                var label = $"Exportado em {DateTime.Now:dd/MM/yyyy HH:mm}";
                gfx.DrawString(label, font, new XSolidBrush(XColor.FromArgb(130, 130, 130)),
                    new XRect(0, midY, pageW, 14), XStringFormats.Center);
            }

            // Lado direito: logo + "NXProject Community"
            double rightX = pageW - MarginPt;

            // Texto "NXProject Community"
            {
                var font  = new XFont("Segoe UI", 8, XFontStyleEx.Regular);
                var label = "NXProject Community";
                double txtW = 120;
                gfx.DrawString(label, font, new XSolidBrush(XColor.FromArgb(80, 80, 80)),
                    new XRect(rightX - txtW, midY, txtW, 14), XStringFormats.CenterRight);

                rightX -= txtW + 4;
            }

            // Logo (pequeno, à esquerda do texto)
            if (logoBitmap != null)
            {
                try
                {
                    var pngBytes = BitmapImageToPng(logoBitmap);
                    using var logoStream = new MemoryStream(pngBytes);
                    using var logoImg = XImage.FromStream(logoStream);
                    double logoH = 14;
                    double logoW = logoH * (logoBitmap.PixelWidth / (double)logoBitmap.PixelHeight);
                    gfx.DrawImage(logoImg, rightX - logoW, midY, logoW, logoH);
                }
                catch
                {
                    // logo é opcional — falha silenciosa
                }
            }
        }

        private static byte[] BitmapImageToPng(BitmapImage src)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
    }
}
