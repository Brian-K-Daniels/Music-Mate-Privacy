using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Factory default is Make It Easy ON. Tests that assert strict onset gates,
/// immediate wrong-pitch marking, or catch-up/pause without the beginner
/// preset must turn Easy off after constructing <see cref="NoteSessionService"/>.
/// </summary>
internal static class TimingTestSession
{
    public static NoteSessionService UseStrictTiming(NoteSessionService session)
    {
        if (session.IsMakeItEasyActive)
            session.SetMakeItEasyActive(false);
        return session;
    }
}
