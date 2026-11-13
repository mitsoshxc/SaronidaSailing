namespace SaronidaSailing.Core.Entities;

public class IntegrationSetting : BaseEntity
{
    private IntegrationSetting()
    {
    }

    public required string Key { get; init; }

    public string Value { get; private set; } = "";

    public static IntegrationSetting Create(string key, string value, Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.CreateVersion7(),
            Key = key,
            Value = value,
        };

    public void SetValue(string value) => Value = value;
}
