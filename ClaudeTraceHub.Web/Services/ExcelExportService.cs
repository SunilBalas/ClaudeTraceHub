using ClaudeTraceHub.Web.Models;
using ClosedXML.Excel;

namespace ClaudeTraceHub.Web.Services;

public class ExcelExportService
{
    public byte[] ExportConversation(Conversation conversation)
    {
        using var wb = new XLWorkbook();

        // Summary sheet
        var wsSummary = wb.AddWorksheet("Summary");
        wsSummary.Cell(1, 1).Value = "Session ID";
        wsSummary.Cell(1, 2).Value = conversation.SessionId;
        wsSummary.Cell(2, 1).Value = "Project";
        wsSummary.Cell(2, 2).Value = conversation.ProjectName;
        wsSummary.Cell(3, 1).Value = "Created";
        wsSummary.Cell(3, 2).Value = conversation.Created?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
        wsSummary.Cell(4, 1).Value = "Git Branch";
        wsSummary.Cell(4, 2).Value = conversation.GitBranch ?? "";
        wsSummary.Cell(5, 1).Value = "Messages";
        wsSummary.Cell(5, 2).Value = conversation.Messages.Count;
        wsSummary.Cell(6, 1).Value = "Output Tokens";
        wsSummary.Cell(6, 2).Value = conversation.TotalOutputTokens;

        wsSummary.Column(1).Width = 15;
        wsSummary.Column(2).Width = 50;
        wsSummary.Range("A1:A6").Style.Font.Bold = true;

        // Messages sheet
        var wsMessages = wb.AddWorksheet("Messages");
        var headers = new[] { "#", "Timestamp", "Role", "Message", "Model", "Tokens", "Tools Used" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = wsMessages.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#4472C4");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        wsMessages.SheetView.FreezeRows(1);

        var userFill = XLColor.FromHtml("#E2EFDA");
        var assistantFill = XLColor.FromHtml("#DAEEF3");

        for (int i = 0; i < conversation.Messages.Count; i++)
        {
            var msg = conversation.Messages[i];
            var row = i + 2;
            var fill = msg.Role == "user" ? userFill : assistantFill;

            wsMessages.Cell(row, 1).Value = i + 1;
            wsMessages.Cell(row, 2).Value = msg.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            wsMessages.Cell(row, 3).Value = msg.Role == "user" ? "User" : "Assistant";
            wsMessages.Cell(row, 4).Value = TruncateForExcel(msg.Text);
            wsMessages.Cell(row, 5).Value = msg.Model ?? "";
            wsMessages.Cell(row, 6).Value = msg.OutputTokens?.ToString() ?? "";
            wsMessages.Cell(row, 7).Value = msg.ToolUsages.Count > 0
                ? string.Join(", ", msg.ToolUsages.Select(t => $"{t.ToolName}: {t.Summary}"))
                : "";

            var range = wsMessages.Range(row, 1, row, 7);
            range.Style.Fill.BackgroundColor = fill;
            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.OutsideBorderColor = XLColor.FromHtml("#D9D9D9");

            wsMessages.Cell(row, 4).Style.Alignment.WrapText = true;
            wsMessages.Cell(row, 7).Style.Alignment.WrapText = true;
        }

        // Column widths
        wsMessages.Column(1).Width = 5;
        wsMessages.Column(2).Width = 20;
        wsMessages.Column(3).Width = 10;
        wsMessages.Column(4).Width = 100;
        wsMessages.Column(5).Width = 25;
        wsMessages.Column(6).Width = 10;
        wsMessages.Column(7).Width = 40;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string TruncateForExcel(string text)
    {
        const int maxChars = 32767;
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Length <= maxChars) return text;
        return text[..(maxChars - 50)] + "\n\n[... TRUNCATED - exceeds Excel limit ...]";
    }
}
