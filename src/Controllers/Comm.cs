using Microsoft.AspNetCore.Mvc;
using UglyToad.PdfPig;
using PDFtoImage;
using System.Drawing;
using SkiaSharp;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.Util;

namespace ComAPI.Controllers;


[ApiController]
[Route("[controller]")]
public class CommentController : ControllerBase
{
    const float paddingX = 15; // Padding in points not pixels!
    const float paddingY = 15;
    const float magXL = 1.5F;

    private readonly ILogger<CommentController> _logger;

    public CommentController(ILogger<CommentController> logger)
    {
        _logger = logger;
    }

    private static void ProcessFile(string sheetname, IWorkbook workbook, Stream fdoc) {

        // using var fdoc = File.OpenRead(path);
        // Console.WriteLine($"[*] Reading PDF file at {fdoc.Name}");

        using PdfDocument pdf = PdfDocument.Open(fdoc);
        // using IWorkbook workbook = new XSSFWorkbook();

        ISheet sheet1 = workbook.CreateSheet(sheetname);
        IDrawing patriarch = sheet1.CreateDrawingPatriarch();

        var pages = pdf.GetPages();

        int row = 1;

        IRow r = sheet1.CreateRow(0);
        r.CreateCell(0).SetCellValue("Comment");
        r.CreateCell(1).SetCellValue("Annotation");

        IFont font = workbook.CreateFont();
        font.IsBold = true;

        for (int i = 0; i <= 1; i++) {
            r.GetCell(i).RichStringCellValue.ApplyFont(font);
            r.GetCell(i).CellStyle.Alignment = HorizontalAlignment.Center;
            r.GetCell(i).CellStyle.VerticalAlignment = VerticalAlignment.Center;
            sheet1.SetColumnWidth(i, 25 * 256);
        }

        r.HeightInPoints = (float)Units.ToPoints(Units.EMU_PER_CENTIMETER);

        foreach (var page in pages) {
            double maxy = page.Height;
            foreach (var ann in page.ExperimentalAccess.GetAnnotations()) {
                double[] lims = [Double.PositiveInfinity, Double.NegativeInfinity, Double.PositiveInfinity, Double.NegativeInfinity];
                // min x - 0
                // max x - 1
                // min y - 2
                // max y - 3

                // PDF info
                // Console.WriteLine(ann.AnnotationDictionary);

                // For Text annotations
                // foreach (var q in ann.QuadPoints) {
                //     foreach (var p in q.Points) {
                //         if (p.X > lims[1]) lims[1] = p.X;
                //         if (p.X < lims[0]) lims[0] = p.X;
                //         if (p.Y > lims[3]) lims[3] = p.Y;
                //         if (p.Y < lims[2]) lims[2] = p.Y;
                //     }
                // };

                if (ann.Type != UglyToad.PdfPig.Annotations.AnnotationType.FreeText) continue;
                lims[0] = ann.Rectangle.Left;
                lims[1] = ann.Rectangle.Right;
                lims[2] = ann.Rectangle.Bottom;
                lims[3] = ann.Rectangle.Top;

                // Do the affine transform, 2 is max y 3 is min y
                lims[2] = maxy - lims[2];
                lims[3] = maxy - lims[3];

                var bbox = new RectangleF(
                    float.Max((float)lims[0] - paddingX, 0),
                    float.Max((float)lims[3] - paddingY, 0),
                    float.Min((float)(lims[1] - lims[0]) + 2 * paddingX, (float)page.Width),
                    float.Min((float)(lims[2] - lims[3]) + 2 * paddingY, (float)maxy)
                );

                // Console.WriteLine(ann.AnnotationDictionary);
                // Console.WriteLine(ann.ToString());
                if (Double.IsFinite(bbox.Width) && Double.IsFinite(bbox.Height) && ann.Content != "") {
                    // Console.WriteLine($"[+] Processing annotation {ann.Name} on page {page.Number}");
                    #pragma warning disable CA1416 // Validate platform compatibility

                    SKBitmap bmap = Conversion.ToImage(
                        fdoc,
                        leaveOpen: true,
                        page: page.Number - 1,
                        options: new(
                            DpiRelativeToBounds: true,
                            Bounds: bbox,
                            Dpi: 300,
                            WithAnnotations: true,
                            WithFormFill: true
                        )
                    );
                    #pragma warning restore CA1416 // Validate platform compatibility
                    IRow ri = sheet1.CreateRow(row);
                    ri.HeightInPoints = bbox.Height * magXL;
                    SKData dat = bmap.Encode(SKEncodedImageFormat.Png, 90);
                    XSSFClientAnchor anchor = new(0, 0, Units.ToEMU(bbox.Width * magXL), Units.ToEMU(bbox.Height * magXL), 1, row, 1, row++);
                    anchor.AnchorType = AnchorType.MoveDontResize;
                    XSSFPicture img = (XSSFPicture)patriarch.CreatePicture(anchor, workbook.AddPicture(dat.ToArray(), PictureType.PNG));
                    img.LineStyle = LineStyle.Solid;
                    img.SetLineStyleColor(0, 0, 0);
                    img.LineWidth = 1;
                    ri.CreateCell(0).SetCellValue(ann.Content);
                }
            }
        }

        // using FileStream sw = File.Create(output);
        // workbook.Write(sw, false);
        // Console.WriteLine($"[*] Saved output at {sw.Name}");
    }

    [HttpPost(Name = "PostFiles")]
    public IActionResult Post()
    {
        if (!Request.HasFormContentType) {
            return new UnsupportedMediaTypeResult();
        }

        if (Request.Form.Files.Count == 0) {
            return UnprocessableEntity();
        }

        using IWorkbook workbook = new XSSFWorkbook();
        foreach (var ffile in Request.Form.Files) {
            try {
                ProcessFile(ffile.FileName, workbook, ffile.OpenReadStream());
            } catch (UglyToad.PdfPig.Core.PdfDocumentFormatException) {
                return UnprocessableEntity();
            }
        }


        var o = new MemoryStream();
        workbook.Write(o, true);
        o.Seek(0, SeekOrigin.Begin);
        // Console.WriteLine("Hello");

        return File(o, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"comments-{DateTime.Now.ToString("s")}.xlsx");
    }

}
