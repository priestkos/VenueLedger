// The one Dalamud type Configuration.cs touches, so the harness can compile the real file.
namespace Dalamud.Configuration;

public interface IPluginConfiguration
{
    int Version { get; set; }
}
