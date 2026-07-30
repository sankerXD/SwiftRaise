using Dalamud.Configuration;

namespace SwiftRaise;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    /// <summary>自动复活功能总开关.</summary>
    public bool Enabled { get; set; } = true;
}
