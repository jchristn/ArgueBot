namespace ArgueBot.Tui
{
    /// <summary>Values typed on the sign-in page. The password is used once and never stored.</summary>
    internal sealed class SignInForm
    {
        internal string Server { get; set; } = "";
        internal string Email { get; set; } = "";
        internal string Password { get; set; } = "";
        internal string Token { get; set; } = "";
        internal bool AllowInsecureHttp { get; set; }
    }
}
