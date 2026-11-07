namespace SRMS.Desktop.Models.Reports;

public sealed class TopPerformerDto
{
    public int Rank { get; set; }
    public int StudentId { get; set; }
    public string FullName { get; set; } = "";
    public string AdmissionNumber { get; set; } = "";
    public int ClassId { get; set; }
    public string? ClassName { get; set; }
    public decimal AnnualAverage { get; set; }
    public string? Grade { get; set; }
}

public sealed class TopPerformerListResponse
{
    public List<TopPerformerDto> Performers { get; set; } = new();
    public int Count { get; set; }
}

public sealed class BatchReportRequest
{
    public int ClassId { get; set; }
    public int Term { get; set; }
    public string AcademicYear { get; set; } = "";
    public string ReportType { get; set; } = "Annual";
    public int Count { get; set; }
}

public sealed class BatchReportResult
{
    public string? Url { get; set; }
    public int Count { get; set; }
    public string? Error { get; set; }
}

public sealed class BECECandidateDto
{
    public int StudentId { get; set; }
    public string FullName { get; set; } = "";
    public string IndexNumber { get; set; } = "";
    public string Gender { get; set; } = "";
}

public sealed class BECECandidateListResponse
{
    public List<BECECandidateDto> Candidates { get; set; } = new();
}