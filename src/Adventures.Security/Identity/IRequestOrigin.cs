namespace Adventures.Security;

/// <summary>Whether the current request comes from this machine (a loopback address, no forwarding proxy). A web host supplies it; code that must never act for a remote caller asks it.</summary>
public interface IRequestOrigin
{
    bool IsLocal { get; }
}
