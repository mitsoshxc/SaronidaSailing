namespace SaronidaSailing.Core.Interfaces;

public interface IContentSeedSource
{
    IReadOnlyList<ContentSeedGroup> GetGroups();
}
