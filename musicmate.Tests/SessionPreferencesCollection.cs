namespace musicmate.Tests;

/// <summary>
/// Serializes tests that mutate the shared <see cref="Services.SessionPreferences.TestStore"/>
/// / ThemeService.TestStore statics.
/// </summary>
[CollectionDefinition("SessionPreferences")]
public sealed class SessionPreferencesCollection : ICollectionFixture<SessionPreferencesCollection.Marker>
{
    public sealed class Marker;
}
