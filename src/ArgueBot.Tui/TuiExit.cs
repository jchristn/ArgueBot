namespace ArgueBot.Tui
{
    /// <summary>Why the workspace closed, which decides whether the sign-in page opens next.</summary>
    internal enum TuiExit
    {
        /// <summary>The user detached; debates keep running.</summary>
        Quit,
        /// <summary>The user signed out.</summary>
        SignOut,
        /// <summary>The user wants to sign in to another server.</summary>
        SwitchServer,
        /// <summary>The service rejected the token (expired, revoked or disabled).</summary>
        SessionEnded
    }
}
