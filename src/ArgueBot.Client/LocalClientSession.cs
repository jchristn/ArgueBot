namespace ArgueBot.Client
{
    using System;

    internal sealed class LocalClientSession
    {
        private string _Endpoint = "";
        private string _HomeId = "";
        private string _Token = "";
        public int FormatVersion { get; set; } = 1;
        public string Endpoint { get { return _Endpoint; } set { ArgumentNullException.ThrowIfNull(value); _Endpoint = value; } }
        public string HomeId { get { return _HomeId; } set { ArgumentNullException.ThrowIfNull(value); _HomeId = value; } }
        public string Token { get { return _Token; } set { ArgumentNullException.ThrowIfNull(value); _Token = value; } }
    }
}
