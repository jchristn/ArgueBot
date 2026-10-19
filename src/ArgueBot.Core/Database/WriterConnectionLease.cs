namespace ArgueBot.Core.Database
{
    using System;
    using System.Data.Common;

    internal sealed class WriterConnectionLease : IDisposable
    {
        private readonly bool _DisposeConnection;
        internal DbConnection Connection { get; }
        internal WriterConnectionLease(DbConnection connection, bool disposeConnection) { Connection = connection; _DisposeConnection = disposeConnection; }
        public void Dispose() { if (_DisposeConnection) Connection.Dispose(); }
    }
}
