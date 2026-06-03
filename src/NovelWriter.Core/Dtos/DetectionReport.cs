using NovelWriter.Core.Enums;

namespace NovelWriter.Core.Dtos;

public class DetectionReport
{
    public AiRiskLevel OverallRisk { get; set; }
    public double StatisticalScore { get; set; }
    public double? ExternalScore { get; set; }
    public string? ExternalProvider { get; set; }
    public List<DetectionDetail> Details { get; set; } = [];
}

public class DetectionDetail
{
    public string Method { get; set; } = string.Empty;
    public double Score { get; set; }
    public string? Explanation { get; set; }
}
