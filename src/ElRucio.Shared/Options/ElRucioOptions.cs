using System.ComponentModel.DataAnnotations;

namespace ElRucio.Shared.Options;

public sealed class ElRucioOptions
{
    [Required]
    public string DataDir { get; set; } = "data";

    public List<string> WorkspaceRoots { get; set; } = [];

    [Required]
    public string RiskLevel { get; set; } = "Conservative";

    [Required]
    public string InstructionFileName { get; set; } = "COPILOT.md";

    public List<string> AllowedChatIds { get; set; } = [];

    public bool OwnerBinding { get; set; }

    [Range(256, 32768)]
    public int MaxResponseChars { get; set; } = 4096;
}
