namespace GenHub.Core;

public interface IGameRunner
{
    public IGameInstallation GameInstallation { get; }
    public List<IMod> ZeroHourMods { get; } // mods should be implemented later and ignored for now
    public List<IMod> VanillaMods { get; }
    
    public void RunZeroHourGame(string[] parameters);
    public void RunVanillaGame(string[] parameters);
}