namespace MOBANet.Client.Settings
{
    /// <summary>
    /// Ability cast mode. Determines how ability targeting works.
    /// </summary>
    public enum CastMode
    {
        /// <summary>Press = cast immediate at cursor position.</summary>
        QuickCast,
        /// <summary>Hold = show indicator, Release = cast.</summary>
        QuickCastWithIndicator,
        /// <summary>Press = show indicator, Press again = cast.</summary>
        NormalCast
    }
}
