using System;
using System.Windows;

namespace HowToFishTrainer.App;

// Read before BAML resolves StaticResource values. Replacing a Duration resource
// after a Storyboard was loaded cannot change the duration already stored in it.
public static class UiMotion
{
    public static bool ReduceMotion { get; internal set; } = !SystemParameters.ClientAreaAnimation;
    private static Duration For(double milliseconds) => new(TimeSpan.FromMilliseconds(ReduceMotion ? 0 : milliseconds));
    public static Duration Hover => For(140);
    public static Duration Press => For(90);
    public static Duration Release => For(160);
    public static Duration Focus => For(140);
    public static Duration Select => For(250);
    public static Duration Page => For(210);
    public static Duration Switch => For(190);
    public static Duration Status => For(150);
    public static Duration Expand => For(210);
    public static Duration Popup => For(170);
}
