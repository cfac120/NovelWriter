namespace NovelWriter.Core.Dtos;

public class VolumeCompressionReport
{
    public int VolumeNumber { get; set; }
    public int OriginalTokenCount { get; set; }
    public int CompressedTokenCount { get; set; }
    public double CompressionRatio => OriginalTokenCount > 0 ? (double)CompressedTokenCount / OriginalTokenCount : 0;
}
