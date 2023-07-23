using FormsRecognizer.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using System.Text;
using Azure.AI.TextAnalytics;
using System.Drawing;
using PdfSharp.Pdf;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PdfSharp.Drawing;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using NuGet.Versioning;
using PdfSharp.Pdf.Content;
using Newtonsoft.Json;

namespace FormsRecognizer.Controllers
{
    public class HomeController : Controller 
    {
        private readonly ILogger<HomeController> _logger;
        protected XColor BackColor { get; set; }

        protected XColor BackColor2 { get; set; }

        protected XColor ShadowColor { get; set; }

        protected double BorderWidth { get; set; }

        protected XPen BorderPen { get; set; }

        XGraphicsState _state = null!;



        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
            BackColor = XColors.Ivory;
            BackColor2 = XColors.WhiteSmoke;

            BackColor = XColor.FromArgb(212, 224, 240);
            BackColor2 = XColor.FromArgb(253, 254, 254);

            ShadowColor = XColors.Gainsboro;
            BorderWidth = 4.5;
            BorderPen = new XPen(XColor.FromArgb(94, 118, 151), BorderWidth);
        }

        public void BeginBox(XGraphics gfx, int number, string title)
        {
            const int dEllipse = 15;
            var rect = new XRect(0, 20, 300, 200);
            if (number % 2 == 0)
                rect.X = 300 - 5;
            rect.Y = 40 + ((number - 1) / 2) * (200 - 5);
            rect.Inflate(-10, -10);
            var rect2 = rect;
            rect2.Offset(BorderWidth, BorderWidth);
            gfx.DrawRoundedRectangle(new XSolidBrush(ShadowColor), rect2, new XSize(dEllipse + 8, dEllipse + 8));
            var brush = new XLinearGradientBrush(rect, BackColor, BackColor2, XLinearGradientMode.Vertical);
            gfx.DrawRoundedRectangle(BorderPen, brush, rect, new XSize(dEllipse, dEllipse));
            rect.Inflate(-5, -5);

           

            rect.Inflate(-10, -5);
            rect.Y += 20;
            rect.Height -= 20;
            //gfx.DrawRectangle(XPens.Red, rect);

            _state = gfx.Save();
            gfx.TranslateTransform(rect.X, rect.Y);
        }

        public void EndBox(XGraphics gfx)
        {
            gfx.Restore(_state);
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Pii()
        {
            return View();
        }

        public IActionResult PiiRedaction()
        {
            return View();
        }

        public IActionResult DownloadPiiRedaction()
        {
            return View();
        }

        public IActionResult DownloadPiiRedactionAll()
        {
            return View();
        }

        [HttpPost("post-file")]
        public IActionResult IndexPost([FromQuery] IFormFile file)
        {
            AnalyzeResult fsresult = GetFSResultsWordsAndLinesFromFile(file);

            var output = JsonConvert.SerializeObject(fsresult,Formatting.Indented);

            ViewData["FormsRecognzierOutput"] = output;

            return View();
        }

        [HttpPost("home/post-pii-detection")]
        public IActionResult PiiPost([FromQuery] IFormFile file)
        {
            StringBuilder text = GetFsCombinedTextFromFile(file);

            StringBuilder output = ExtractPii(text.ToString());

            ViewData["LangServiceOutput"] = output;

            return View();
        }

        [HttpPost("home/post-pii-redaction")]
        public IActionResult PiiRedactionPost([FromQuery] IFormFile file)
        {
            StringBuilder text = GetFsCombinedTextFromFile(file);


            AnalyzeResult fsresult = GetFSResultsWordsAndLinesFromFile(file);

            

            AzureKeyCredential langcredentials = new AzureKeyCredential("5fcf39fa9e934af79601a1a525f1e2ef");
            Uri langendpoint = new Uri("https://formsrecognizerlang062023.cognitiveservices.azure.com/");

            var langclient = new TextAnalyticsClient(langendpoint, langcredentials);

            RecognizePiiEntitiesOptions options = new RecognizePiiEntitiesOptions();

            options.CategoriesFilter.Add(PiiEntityCategory.AUTaxFileNumber);

            PiiEntityCollection entities = langclient.RecognizePiiEntities(text.ToString(), null, options).Value;

            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"Redacted Text: {entities.RedactedText}");

            var polygonList = new List<List<PointF>>();

            if (entities.Count > 0)
            {
                sb.AppendLine($"Recognized {entities.Count} PII entit{(entities.Count > 1 ? "ies" : "y")}:");
                foreach (PiiEntity entity in entities)
                {
                    sb.AppendLine($"Text: {entity.Text}, Category: {entity.Category}, SubCategory: {entity.SubCategory}, Confidence score: {entity.ConfidenceScore}");

                    foreach (var fsPage in fsresult.Pages)
                    {

                        int wordCounter = 0;
                        foreach (var word in fsPage.Words)
                        {
                            wordCounter++;
                            if (word.Content.ToLower() == entity.Text.ToLower())
                            {
                                polygonList.Add((List<PointF>)word.BoundingPolygon);
                                foreach (var point in word.BoundingPolygon)
                                {

                                    sb.Append(point.X);
                                    sb.Append(",");
                                    sb.Append(point.Y);
                                    sb.AppendLine("");


                                }
                            }
                        }
                    }
                }
            }
            else
            {
                sb.AppendLine("No entities were found.");
            }

            ViewData["PiiRedaction"] = sb;
            return View();
        }

        [HttpPost("home/post-pii-redaction-download")]
        public FileResult DownloadPiiRedactionPost([FromQuery] IFormFile file)
        {
            var doc = PdfReader.Open(file.OpenReadStream());
            var gfx = new XGraphics[doc.PageCount];
            for (var page = 0; page < doc.PageCount; page++)
            {
                gfx[page] = XGraphics.FromPdfPage(doc.Pages[page]);

            }

            StringBuilder text = GetFsCombinedTextFromFile(file);
            AnalyzeResult fsresult = GetFSResultsWordsAndLinesFromFile(file);
            PiiEntityCollection entities = GetPiiEntitiesFromFileText(text);

            StringBuilder sb = new StringBuilder();


            var polygonList = new List<List<PointF>>();

            if (entities.Count > 0)
            {


                foreach (PiiEntity entity in entities)
                {

                    foreach (var fsPage in fsresult.Pages)
                    {
                        RedactUsingFsExtractedTextAndSharpe(gfx[fsPage.PageNumber - 1], sb, entity, fsPage);
                    }
                }
            }

            ViewData["PiiRedaction"] = sb;

            var stream = new MemoryStream();
            doc.Save(stream);
            return File(stream, file.ContentType);
        }

        [HttpPost("home/post-pii-redaction-download-all")]
        public FileResult DownloadPiiRedactionPostAll([FromQuery] IFormFile file)
        {
            var doc = PdfReader.Open(file.OpenReadStream());
            var gfx = new XGraphics[doc.PageCount];
            for (var page = 0; page < doc.PageCount; page++)
            {
                gfx[page] = XGraphics.FromPdfPage(doc.Pages[page]);

            }

            StringBuilder text = GetFsCombinedTextFromFile(file);
            AnalyzeResult fsresult = GetFSResultsWordsAndLinesFromFile(file);
            PiiEntityCollection entities = GetPiiEntitiesFromFileText(text, false);

            StringBuilder sb = new StringBuilder();


            var polygonList = new List<List<PointF>>();

            if (entities.Count > 0)
            {


                foreach (PiiEntity entity in entities)
                {

                    foreach (var fsPage in fsresult.Pages)
                    {
                        RedactUsingFsExtractedTextAndSharpe(gfx[fsPage.PageNumber - 1], sb, entity, fsPage);
                    }
                }
            }

            ViewData["PiiRedaction"] = sb;

            var stream = new MemoryStream();
            doc.Save(stream);
            return File(stream, file.ContentType);
        }
        private static PiiEntityCollection GetPiiEntitiesFromFileText(StringBuilder text, bool onlyTFN = true)
        {
            AzureKeyCredential langcredentials = new AzureKeyCredential("5fcf39fa9e934af79601a1a525f1e2ef");
            Uri langendpoint = new Uri("https://formsrecognizerlang062023.cognitiveservices.azure.com/");

            var langclient = new TextAnalyticsClient(langendpoint, langcredentials);


            RecognizePiiEntitiesOptions options = new RecognizePiiEntitiesOptions();

            if(onlyTFN)
            {
                options.CategoriesFilter.Add(PiiEntityCategory.AUTaxFileNumber);

            }

            PiiEntityCollection entities = langclient.RecognizePiiEntities(text.ToString(), null, options).Value;
            return entities;
        }

        private static AnalyzeResult GetFSResultsWordsAndLinesFromFile(IFormFile file)
        {
            string fsendpoint = "https://formsrecognizer062023.cognitiveservices.azure.com/";
            string fskey = "912d18096e2a4e1680778012c1fc614a";
            AzureKeyCredential fs = new AzureKeyCredential(fskey);
            DocumentAnalysisClient fsclient = new DocumentAnalysisClient(new Uri(fsendpoint), fs);


            AnalyzeDocumentOperation operation = fsclient.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-document",
                file.OpenReadStream()).Result;

            AnalyzeResult fsresult = operation.Value;
            return fsresult;
        }

        private void RedactUsingSharpeFindReplace(XGraphics gfx, StringBuilder sb, PiiEntity entity, 
            DocumentPage fsPage, PdfDocument doc)
        {
            var page = doc.Pages[0];
            var contents = ContentReader.ReadContent(page);

            var result = contents.FirstOrDefault(x => x.ToString() == entity.Text);
            
            
            page.Contents.ReplaceContent(contents);
        }

        private void RedactUsingFsExtractedTextAndSharpe(XGraphics gfx, StringBuilder sb, PiiEntity entity, DocumentPage? fsPage)
        {
            sb.AppendLine($"Unit {fsPage.Unit}");
            int multiplier = 1;
            if (fsPage.Unit == DocumentPageLengthUnit.Inch)
            {
                multiplier = 72;
            }

            int wordCounter = 0;

            foreach (var word in fsPage.Words)
            {
                if (word.Content.ToLower() == entity.Text.ToLower())
                {
                    wordCounter++;

                    //polygonList.Add((List<PointF>)word.BoundingPolygon);
                    sb.AppendLine(entity.Text);

                    //BeginBox(gfx, wordCounter, "DrawPolygon");

                    var pen = new XPen(XColors.Navy, Math.PI);

                    var pointsArray = word.BoundingPolygon.Select(bp => new XPoint()
                    {
                        X = bp.X * multiplier,
                        Y = bp.Y * multiplier
                    }).ToArray();

                    gfx.DrawPolygon(pen, XBrushes.Navy, pointsArray, XFillMode.Winding);

                    //EndBox(gfx);
                }
            }

            wordCounter = 0;

            foreach (var line in fsPage.Lines)
            {
                if (line.Content.ToLower().IndexOf(entity.Text.ToLower()) > -1)
                {
                    wordCounter++;

                    if (entity.Text.ToLower() != line.Content.ToLower())
                    {
                        var wordsInLine = entity.Text.Split(' ');
                        foreach(var word in wordsInLine) {
                            var wordLocations = fsPage.Words.Where(x => x.Content.ToLower() == word.ToLower());
                            foreach(var wordLocation in wordLocations)
                            {
                                var pen = new XPen(XColors.Navy, Math.PI);

                                var pointsArray = wordLocation.BoundingPolygon.Select(bp => new XPoint()
                                {
                                    X = bp.X * multiplier,
                                    Y = bp.Y * multiplier
                                }).ToArray();

                                gfx.DrawPolygon(pen, XBrushes.Navy, pointsArray, XFillMode.Winding);
                            }
                        }
                    }
                    else
                    {
                        //polygonList.Add((List<PointF>)word.BoundingPolygon);
                        sb.AppendLine(entity.Text);

                        //BeginBox(gfx, wordCounter, "DrawPolygon");

                        var pen = new XPen(XColors.Navy, Math.PI);

                        var pointsArray = line.BoundingPolygon.Select(bp => new XPoint()
                        {
                            X = bp.X * multiplier,
                            Y = bp.Y * multiplier
                        }).ToArray();

                        gfx.DrawPolygon(pen, pointsArray);

                    }
                   

                    

                    //EndBox(gfx);
                }
            }
        }

        private static StringBuilder GetFsCombinedTextFromFile(IFormFile file)
        {
            string endpoint = "https://formsrecognizer062023.cognitiveservices.azure.com/";
            string key = "912d18096e2a4e1680778012c1fc614a";
            AzureKeyCredential credential = new AzureKeyCredential(key);
            DocumentAnalysisClient client = new DocumentAnalysisClient(new Uri(endpoint), credential);

            //Uri fileUri = new Uri("https://raw.githubusercontent.com/Azure-Samples/cognitive-services-REST-api-samples/master/curl/form-recognizer/sample-layout.pdf");

            AnalyzeDocumentOperation operation = client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-document", file.OpenReadStream()).Result;

            AnalyzeResult result = operation.Value;

            StringBuilder output = new StringBuilder();


            //output.AppendLine("Detected key-value pairs:");

            //foreach (DocumentKeyValuePair kvp in result.KeyValuePairs)
            //{
            //    if (kvp.Value == null)
            //    {
            //        output.AppendLine($"  Found key with no value: '{kvp.Key.Content}'");
            //    }
            //    else
            //    {
            //        output.AppendLine($"  Found key-value pair: '{kvp.Key.Content}' and '{kvp.Value.Content}'");
            //    }
            //}

            foreach (DocumentPage page in result.Pages)
            {

                //output.AppendLine($"Document Page {page.PageNumber} has {page.Lines.Count} line(s), {page.Words.Count} word(s),");
                //output.AppendLine($"and {page.SelectionMarks.Count} selection mark(s).");

                for (int i = 0; i < page.Lines.Count; i++)
                {
                    DocumentLine line = page.Lines[i];
                    //output.AppendLine($"  Line {i} has content: '{line.Content}'.");
                    output.AppendLine(line.Content);

                    //output.AppendLine($"    Its bounding box is:");
                    //output.AppendLine($"      Upper left => X: {line.BoundingPolygon[0].X}, Y= {line.BoundingPolygon[0].Y}");
                    //output.AppendLine($"      Upper right => X: {line.BoundingPolygon[1].X}, Y= {line.BoundingPolygon[1].Y}");
                    //output.AppendLine($"      Lower right => X: {line.BoundingPolygon[2].X}, Y= {line.BoundingPolygon[2].Y}");
                    //output.AppendLine($"      Lower left => X: {line.BoundingPolygon[3].X}, Y= {line.BoundingPolygon[3].Y}");
                }

                //for (int i = 0; i < page.SelectionMarks.Count; i++)
                //{
                //    DocumentSelectionMark selectionMark = page.SelectionMarks[i];

                //    output.AppendLine($"  Selection Mark {i} is {selectionMark.State}.");
                //    output.AppendLine($"    Its bounding box is:");
                //    output.AppendLine($"      Upper left => X: {selectionMark.BoundingPolygon[0].X}, Y= {selectionMark.BoundingPolygon[0].Y}");
                //    output.AppendLine($"      Upper right => X: {selectionMark.BoundingPolygon[1].X}, Y= {selectionMark.BoundingPolygon[1].Y}");
                //    output.AppendLine($"      Lower right => X: {selectionMark.BoundingPolygon[2].X}, Y= {selectionMark.BoundingPolygon[2].Y}");
                //    output.AppendLine($"      Lower left => X: {selectionMark.BoundingPolygon[3].X}, Y= {selectionMark.BoundingPolygon[3].Y}");
                //}
            }

            foreach (DocumentStyle style in result.Styles)
            {
                // Check the style and style confidence to see if text is handwritten.
                // Note that value '0.8' is used as an example.

                bool isHandwritten = style.IsHandwritten.HasValue && style.IsHandwritten == true;

                if (isHandwritten && style.Confidence > 0.8)
                {
                    //output.AppendLine($"Handwritten content found:");

                    foreach (DocumentSpan span in style.Spans)
                    {
                        //output.AppendLine($"  Content: {result.Content.Substring(span.Index, span.Length)}");
                        output.AppendLine(result.Content.Substring(span.Index, span.Length));

                    }
                }
            }

            //output.AppendLine("The following tables were extracted:");

            for (int i = 0; i < result.Tables.Count; i++)
            {
                DocumentTable table = result.Tables[i];
                //output.AppendLine($"  Table {i} has {table.RowCount} rows and {table.ColumnCount} columns.");

                foreach (DocumentTableCell cell in table.Cells)
                {
                    //output.AppendLine($"    Cell ({cell.RowIndex}, {cell.ColumnIndex}) has kind '{cell.Kind}' and content: '{cell.Content}'.");
                    output.AppendLine(cell.Content);

                }
            }

            return output;
        }

        [HttpPost("post-lang-service")]
        public IActionResult FormTextPost(string formText)
        {
            if (string.IsNullOrEmpty(formText))
            {
                return View();
            }

            StringBuilder sb = ExtractPii(formText);

            ViewData["LangServiceOutput"] = sb;

            return View();
        }

        private static StringBuilder ExtractPii(string formText)
        {
            AzureKeyCredential credentials = new AzureKeyCredential("5fcf39fa9e934af79601a1a525f1e2ef");
            Uri endpoint = new Uri("https://formsrecognizerlang062023.cognitiveservices.azure.com/");

            var client = new TextAnalyticsClient(endpoint, credentials);

            RecognizePiiEntitiesOptions options = new RecognizePiiEntitiesOptions();

            //options.CategoriesFilter.Add(PiiEntityCategory.AUTaxFileNumber);

            PiiEntityCollection entities = client.RecognizePiiEntities(formText,null, options).Value;

            StringBuilder sb = new StringBuilder();

            if (entities.Count > 0)
            {
                sb.AppendLine($"Recognized {entities.Count} PII entit{(entities.Count > 1 ? "ies" : "y")}:");
                foreach (PiiEntity entity in entities)
                {
                    sb.AppendLine($"Text: {entity.Text}, Category: {entity.Category}, SubCategory: {entity.SubCategory}, Confidence score: {entity.ConfidenceScore}");
                }
            }
            else
            {
                sb.AppendLine("No entities were found.");
            }

            sb.AppendLine($"Redacted Text: {entities.RedactedText}");


            return sb;
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }


    }
}