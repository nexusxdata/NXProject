using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace NXProject.Community.Services
{
    // Resolve fontes do Windows para o PDFsharp 6.x (que não usa GDI+ por padrão)
    internal sealed class WindowsFontResolver : IFontResolver
    {
        private static readonly string FontsFolder =
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        // Mapeamento família → arquivo(s) de fonte no Windows
        private static readonly Dictionary<string, (string regular, string bold)> Map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Segoe UI"]  = ("segoeui",  "segoeuib"),
                ["Arial"]     = ("arial",    "arialbd"),
                ["Helvetica"] = ("arial",    "arialbd"),
                ["Calibri"]   = ("calibri",  "calibrib"),
            };

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (!Map.TryGetValue(familyName, out var files))
                files = ("arial", "arialbd"); // fallback seguro

            var face = isBold ? files.bold : files.regular;
            return new FontResolverInfo(face);
        }

        public byte[]? GetFont(string faceName)
        {
            var path = Path.Combine(FontsFolder, faceName + ".ttf");
            if (File.Exists(path)) return File.ReadAllBytes(path);

            // Tenta variantes comuns
            path = Path.Combine(FontsFolder, faceName + ".otf");
            if (File.Exists(path)) return File.ReadAllBytes(path);

            // Fallback final: Arial regular
            path = Path.Combine(FontsFolder, "arial.ttf");
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
    }

    internal static class PdfExportService
    {
        private static bool _fontResolverRegistered;

        private static void EnsureFontResolver()
        {
            if (_fontResolverRegistered) return;
            GlobalFontSettings.FontResolver = new WindowsFontResolver();
            _fontResolverRegistered = true;
        }
        // Margens e dimensões em pontos PDF (1 pt = 1/72 pol)
        private const double Margin     = 20;
        private const double HeaderH    = 44; // altura da faixa de cabeçalho
        private const double FooterH    = 28;
        private const double SepLine    = 0.8;
        private const double SepGap     = 4;

        /// <summary>
        /// Gera PDF paisagem A4 com cabeçalho (logo da empresa + título do projeto)
        /// e rodapé (nome do projeto | data | logo NXProject).
        /// </summary>
        public static void Export(
            FrameworkElement visual,
            string projectName,
            BitmapImage? nxLogo,
            string companyName,
            BitmapImage? companyLogo,
            string filePath,
            string exportedOnLabel = "Exportado em",
            string scheduleSubject = "Cronograma NXProject")
        {
            EnsureFontResolver();
            var pngBytes = RenderToPng(visual, dpi: 150);

            var doc = new PdfDocument();
            doc.Info.Title   = projectName;
            doc.Info.Subject = scheduleSubject;
            doc.Info.Creator = "NXProject Community";

            var page = doc.AddPage();
            page.Orientation = PdfSharp.PageOrientation.Landscape;
            page.Size        = PdfSharp.PageSize.A4;

            using var gfx = XGraphics.FromPdfPage(page);

            double pageW = page.Width.Point;
            double pageH = page.Height.Point;

            bool hasHeader = companyLogo != null || !string.IsNullOrWhiteSpace(companyName);

            // ── Cabeçalho da empresa ───────────────────────────────────────
            double contentTop = Margin;
            if (hasHeader)
            {
                DrawHeader(gfx, pageW, companyName, companyLogo, projectName);
                contentTop = Margin + HeaderH + SepLine + SepGap;
            }

            // ── Imagem do cronograma ───────────────────────────────────────
            double footerTop = pageH - Margin - FooterH;
            double imgY = contentTop;
            double imgH = footerTop - SepGap - SepLine - imgY;
            double imgW = pageW - Margin * 2;

            using var imgStream = new MemoryStream(pngBytes);
            using var xImg = XImage.FromStream(imgStream);
            gfx.DrawImage(xImg, Margin, imgY, imgW, imgH);

            // ── Rodapé NXProject ──────────────────────────────────────────
            DrawFooter(gfx, pageW, pageH, projectName, nxLogo, exportedOnLabel);

            doc.Save(filePath);
        }

        // ── Cabeçalho ─────────────────────────────────────────────────────

        private static void DrawHeader(XGraphics gfx, double pageW,
                                       string companyName, BitmapImage? companyLogo,
                                       string projectName)
        {
            double logoH = HeaderH - 8;
            double curX  = Margin;
            double midY  = Margin + HeaderH / 2.0;

            // Logo da empresa (esquerda)
            if (companyLogo != null)
            {
                try
                {
                    var bytes = BitmapToPng(companyLogo);
                    using var ms  = new MemoryStream(bytes);
                    using var img = XImage.FromStream(ms);
                    double logoW = logoH * img.PixelWidth / (double)img.PixelHeight;
                    gfx.DrawImage(img, curX, Margin + 4, logoW, logoH);
                    curX += logoW + 12;
                }
                catch { /* logo opcional */ }
            }

            // Nome da empresa (se houver)
            if (!string.IsNullOrWhiteSpace(companyName))
            {
                var fComp = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
                gfx.DrawString(companyName, fComp, new XSolidBrush(XColor.FromArgb(30, 30, 30)),
                    new XRect(curX, midY - 14, 200, 16), XStringFormats.CenterLeft);
                curX += 210;
            }

            // Título do projeto (centro-esquerda após logo/empresa)
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                var fTitle = new XFont("Segoe UI", 14, XFontStyleEx.Bold);
                double titleX = curX + 10;
                double titleW = pageW - titleX - Margin;
                gfx.DrawString(projectName, fTitle, new XSolidBrush(XColor.FromArgb(30, 67, 132)),
                    new XRect(titleX, midY - 9, titleW, 20), XStringFormats.CenterLeft);
            }

            // Linha separadora abaixo do cabeçalho
            double lineY = Margin + HeaderH;
            var pen = new XPen(XColor.FromArgb(43, 87, 154), SepLine);
            gfx.DrawLine(pen, Margin, lineY, pageW - Margin, lineY);
        }

        // ── Rodapé ────────────────────────────────────────────────────────

        private static void DrawFooter(XGraphics gfx, double pageW, double pageH,
                                       string projectName, BitmapImage? nxLogo,
                                       string exportedOnLabel)
        {
            double lineY = pageH - Margin - FooterH;
            double midY  = lineY + SepLine + 6;

            var linePen = new XPen(XColor.FromArgb(200, 200, 200), SepLine);
            gfx.DrawLine(linePen, Margin, lineY, pageW - Margin, lineY);

            // Nome do projeto (esquerda)
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                var f = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
                gfx.DrawString(projectName, f, XBrushes.DarkSlateGray,
                    new XRect(Margin, midY, pageW / 2.0, 14), XStringFormats.CenterLeft);
            }

            // Data (centro)
            {
                var f     = new XFont("Segoe UI", 8, XFontStyleEx.Regular);
                var label = $"{exportedOnLabel} {DateTime.Now:dd/MM/yyyy HH:mm}";
                gfx.DrawString(label, f, new XSolidBrush(XColor.FromArgb(140, 140, 140)),
                    new XRect(0, midY, pageW, 14), XStringFormats.Center);
            }

            // Logo NXProject + texto (direita)
            double rightX = pageW - Margin;
            var fRight = new XFont("Segoe UI", 8, XFontStyleEx.Regular);
            const string brand = "NXProject Community";
            double txtW = 120;
            gfx.DrawString(brand, fRight, new XSolidBrush(XColor.FromArgb(100, 100, 100)),
                new XRect(rightX - txtW, midY, txtW, 14), XStringFormats.CenterRight);
            rightX -= txtW + 4;

            if (nxLogo != null)
            {
                try
                {
                    var bytes = BitmapToPng(nxLogo);
                    using var ms  = new MemoryStream(bytes);
                    using var img = XImage.FromStream(ms);
                    double lh = 14;
                    double lw = lh * img.PixelWidth / (double)img.PixelHeight;
                    gfx.DrawImage(img, rightX - lw, midY, lw, lh);
                }
                catch { /* opcional */ }
            }
        }

        // ── Utilitários ───────────────────────────────────────────────────

        private static byte[] RenderToPng(FrameworkElement element, double dpi)
        {
            double scale = dpi / 96.0;
            int pixW = (int)Math.Round(element.ActualWidth  * scale);
            int pixH = (int)Math.Round(element.ActualHeight * scale);

            if (pixW <= 0 || pixH <= 0)
                throw new InvalidOperationException("O elemento não tem tamanho renderizável.");

            var rtb = new RenderTargetBitmap(pixW, pixH, dpi, dpi, PixelFormats.Pbgra32);
            var dv  = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
                ctx.DrawRectangle(new VisualBrush(element), null,
                    new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            rtb.Render(dv);

            return EncodePng(rtb);
        }

        private static byte[] BitmapToPng(BitmapImage src)
        {
            return EncodePng(src);
        }

        private static byte[] EncodePng(BitmapSource src)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
    }
}
