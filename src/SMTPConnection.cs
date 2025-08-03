using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Jannesen.Protocol.SMTP
{
    public sealed partial class SMTPConnection: IDisposable
    {
        [GeneratedRegex(@"^[a-zA-Z0-9\.!#$%&'*+\-\/=?^_`{\|}\~]+@[a-zA-Z0-9-_.]+\.[a-zA-Z]{2,}$")]
        private static partial Regex                               _emailValidator();

        private             Socket?                             _socket;
        private volatile    SMTPResponse?                       _lastResponse;
        private             byte[]                              _receiveBuffer;
        private             string?                             _receiveString;
        private             List<string>                        _receiceLines;
        private volatile    TaskCompletionSource<SMTPResponse>  _receiceRespone;
        private readonly    Lock                                _lock;

        public              IPEndPoint?                         LocalEndPoint               { get; init; }
        public  required    IPEndPoint                          RemoteEndPoint              { get; init; }
        public              int                                 ConnectTimeout              { get; init; }
        public              int                                 Timeout                     { get; init; }
        public              bool                                Idle                        { get; private set; }
        public              int                                 MessageCount                { get; private set; }
        public              SMTPResponse?                       LastResponse
        {
            get {
                return _lastResponse;
            }
        }
        public              bool                                isConnected
        {
            get {
                lock(_lock) {
                    return _socket != null && _socket.Connected;
                }
            }
        }

        public                                                  SMTPConnection()
        {
            Timeout        = 60000;
            ConnectTimeout = 15000;
            _lock          = new Lock();

            // Initialize in OpenAsync
            _receiveBuffer  = null!;
            _receiveString  = null!;
            _receiceLines   = null!;
            _receiceRespone = null!;
        }
        public              void                                Dispose()
        {
            Close();
        }

        public      async   Task                                OpenAsync(CancellationToken cancellationToken)
        {
            Close();
#if DEBUG
            System.Diagnostics.Debug.WriteLine("SMTP open: " + RemoteEndPoint.ToString());
#endif
            var socket   = new Socket(SocketType.Stream, ProtocolType.Tcp);

            if (LocalEndPoint != null) {
                socket.Bind(LocalEndPoint);
            }

            socket.NoDelay        = false;

            lock(_lock) {
                _socket = socket;
            }

            var openTask = new TaskCompletionSource();

            using(var x = cancellationToken.Register(() => {
                              openTask.TrySetException(new TaskCanceledException());
                              Close();
                          })) {
                using (var t = new Timer((state) => {
                                    openTask.TrySetException(new TimeoutException());
                                    Close();
                               }, null, ConnectTimeout, 0)) {
                    socket.BeginConnect(RemoteEndPoint, (ar) => {
                                            try {
                                                socket.EndConnect(ar);
                                                openTask.TrySetResult();
                                            }
                                            catch(Exception err) {
                                                openTask.TrySetException(err);
                                            }
                                        },
                                        null);

                    await openTask.Task;

                    lock(_lock) {
                        _receiveBuffer  = new byte[1024];
                        _receiveString  = null;
                        _receiceLines   = new List<string>();
                        _receiceRespone = new TaskCompletionSource<SMTPResponse>();
                    }

                    socket.BeginReceive(_receiveBuffer, 0, _receiveBuffer.Length, SocketFlags.None, _receiveCallback, socket);

                    var response = await _readResponseAsync();
                    if (response.Code != 220) {
                        throw new SMTPBadReplyException("Received invalid welcom received from SMTP-server '" + RemoteEndPoint.ToString() + "'. Response: " + response.ToString(), response);
                    }
                }
            }
        }
        public              void                                Close()
        {
            Socket? socket;

            lock(_lock) {
                if ((socket = _socket) != null) {
                    _socket   = null;
                }
            }

            Idle = false;

            if (socket != null) {
                try {
                    socket.Close();
                }
                catch(Exception) {
                }
            }
        }

        public      static  void                                ValidateEmailAddress(string address)
        {
            ArgumentNullException.ThrowIfNull(address);
            if (!_emailValidator().IsMatch(address)) {
                throw new SMTPBadEmailAddress("Invalid email address '" + address + "'.");
            }
        }

        public      async   Task<SMTPResponse>                  EHLO_HELO_Async(string domain, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(domain);

            var response = await CmdAsync("EHLO " + domain, Timeout, cancellationToken);

            if (response.Code != 250) {
                response = await CmdAsync("HELO " + domain, Timeout, cancellationToken);
            }

            if (response.Code != 250) {
                throw new SMTPBadReplyException("SMTP Command 'HELO' failed to '" + RemoteEndPoint.ToString() + "'. Response: " + response.ToString(), response);
            }

            Idle = true;

            return response;
        }
        public      async   Task<SMTPResponse>                  HELO_Async(string domain, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(domain);

            var response = await CmdAsyncAnd250("HELO " + domain, Timeout, cancellationToken);

            Idle = true;

            return response;
        }
        public      async   Task<SMTPResponse>                  EHLO_Async(string domain, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(domain);

            var response = await  CmdAsyncAnd250("EHLO "+domain, Timeout, cancellationToken);

            Idle = true;

            return response;
        }
        public              Task<SMTPResponse>                  MAIL_FROM_Async(string address, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(address);
            ValidateEmailAddress(address);
            if (!Idle) {
                throw new InvalidOperationException("SMTP Connection is not idle.");
            }
            Idle = false;
            return CmdAsyncAnd250("MAIL FROM: <"+address+">", Timeout, cancellationToken);
        }
        public              Task<SMTPResponse>                  RCPT_TO_Async(string address, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(address);
            ValidateEmailAddress(address);
            if (Idle) {
                throw new InvalidOperationException("SMTP Connection is idle.");
            }
            return CmdAsyncAnd250("RCPT TO: <"+address+">", Timeout, cancellationToken);
        }
        public              Task<SMTPResponse>                  DATA_Async(string message, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);

            return DATA_Async(Encoding.ASCII.GetBytes(message), cancellationToken);
        }
        public              Task<SMTPResponse>                  DATA_Async(byte[] message, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);

            return DATA_Async(new ReadOnlyMemory<byte>(message), cancellationToken);
        }
        public      async   Task<SMTPResponse>                  DATA_Async(ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
        {
            if (Idle) {
                throw new InvalidOperationException("SMTP Connection is idle.");
            }

            ++MessageCount;

            using (var timeoutcts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                timeoutcts.CancelAfter(Timeout);

                using (var x = timeoutcts.Token.Register(Dispose)) {
                    try {
                        await _sendAsync("DATA");
                        var response = await _readResponseAsync();

                        if (response.Code != 354) {
                            throw new SMTPBadReplyException("SMTP Command 'DATA' failed to '" + RemoteEndPoint.ToString() + "'. Response: " + response.ToString(), response);
                        }

                        var message_ptrx = message.Span;
                        var bsize       = 0x1000;
                        while (bsize < 0x10000 && bsize < message.Length + 8) {
                            bsize <<= 1;
                        }

                        var buffer      = new byte[bsize + 8];
                        var buffer_pos  = 0;
                        var message_pos = 0;
                        var prev     = (byte)'\n';

                        while (message_pos < message.Length) {
                            {
                                var message_ptr = message.Span;
                                var buffer_ptr  = buffer.AsSpan();

                                while (message_pos < message.Length && buffer_pos < bsize) {
                                    var chr = message_ptr[message_pos++];

                                    if ((chr < ' ' || chr > 127) && chr != '\r' & chr != '\n' && chr != '\t')
                                        chr = (byte)'?';

                                    if (chr == '.' && prev == '\n') {
                                        buffer_ptr[buffer_pos++] = (byte)'.';
                                    }

                                    buffer_ptr[buffer_pos++] = prev = chr;
                                }
                            }

                            if (buffer_pos >= bsize) {
                                await _sendAsync(buffer, buffer_pos);
                                buffer_pos = 0;
                            }
                        }

                        {
                            var message_ptr = message.Span;
                            var buffer_ptr = buffer.AsSpan();
                            if (message.Length <= 2 || message_ptr[^2] != '\r' || message_ptr[^1] != '\n') {
                                buffer_ptr[buffer_pos++] = (byte)'\r';
                                buffer_ptr[buffer_pos++] = (byte)'\n';
                            }

                            buffer_ptr[buffer_pos++] = (byte)'.';
                            buffer_ptr[buffer_pos++] = (byte)'\r';
                            buffer_ptr[buffer_pos++] = (byte)'\n';
                        }

                        await _sendAsync(buffer, buffer_pos);

                        response = await _readResponseAsync();
                        if (response.Code != 250) {
                            throw new SMTPBadReplyException("SMTP Command 'DATA' failed to '" + RemoteEndPoint.ToString() + "'. Response: " + response.ToString(), response);
                        }

                        Idle = true;

                        return response;
                    }
                    catch(Exception) {
                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                        if (timeoutcts.IsCancellationRequested)        throw new TimeoutException();
                        throw;
                    }
                }
            }
        }
        public      async   Task<SMTPResponse>                  QUIT_Async(CancellationToken cancellationToken)
        {
            Idle = false;
            var response = await CmdAsync("QUIT", Timeout, cancellationToken);

            if (response.Code != 221) {
                throw new SMTPBadReplyException("SMTP Command 'QUIT' failed to '" + RemoteEndPoint.ToString() + "'. Response: " + response.ToString(), response);
            }

            Close();

            return response;
        }
        public      async   Task<SMTPResponse>                  CmdAsync(string cmd, int timeout, CancellationToken cancellationToken)
        {
            using (var timeoutcts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                timeoutcts.CancelAfter(timeout);

                using (var x = timeoutcts.Token.Register(Dispose)) {
                    try {
                        await _sendAsync(cmd);
                        return await _readResponseAsync();
                    }
                    catch(Exception) {
                        if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException();
                        if (timeoutcts.IsCancellationRequested)        throw new TimeoutException();
                        throw;
                    }
                }
            }
        }
        private     async   Task<SMTPResponse>                  CmdAsyncAnd250(string cmd, int timeout, CancellationToken cancellationToken)
        {
            var response = await CmdAsync(cmd, timeout, cancellationToken);

            if (response.Code != 250) {
                throw new SMTPBadReplyException("SMTP Command '" + cmd + "' failed to '" + RemoteEndPoint.ToString() + "'. Response: " + response.ToString(), response);
            }

            return response;
        }

        private             Task                                _sendAsync(string cmd)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("SMTP send: " + cmd);
#endif
            var data = Encoding.ASCII.GetBytes(cmd + "\r\n");
            return _sendAsync(data, data.Length);
        }
        private             Task                                _sendAsync(byte[] data, int length)
        {
            var task = _receiceRespone.Task;
            if (task.IsCompleted) {
                var expection = task.Exception;
                if (expection != null) {
                    throw expection;
                }

                var response = _receiceRespone.Task.Result;
                throw new SMTPUnexpectedReplyException("Unexpected data receive from SMTP-Server '" + RemoteEndPoint.ToString() + "'. Response: " + response.ToString() , response);
            }

            Socket? socket;

            lock(_lock) {
                socket = _socket;
            }

            if (socket == null) {
                throw new SMTPException("smtp session is closed.");
            }

            if (!socket.Connected) {
                throw new SMTPConnectionClosedException();
            }

            var rtn    = new TaskCompletionSource();

            socket.BeginSend(data, 0, length, SocketFlags.None,
                             (ar) => {
                                 try {
                                     socket.EndSend(ar);
                                     rtn.TrySetResult();
                                 }
                                 catch(Exception err) {
                                     if (err is SocketException sockerr && sockerr.ErrorCode == 10054) {
                                         rtn.TrySetException(new SMTPConnectionClosedException());
                                     }
                                     else {
                                        rtn.TrySetException(err);
                                     }
                                 }
                             },
                             null);

            return rtn.Task;
        }
        private             void                                _receiveCallback(IAsyncResult ar)
        {
            var socket = (Socket)ar.AsyncState!;

            try {
                var size = socket.EndReceive(ar);
                if (size > 0) {
                    lock(_lock) {
                        var r = Encoding.ASCII.GetString(_receiveBuffer, 0, size);

                        _receiveString = (_receiveString != null) ? _receiveString + r : r;

                        int l;
                        while (_receiveString.Length > 0 && (l = _receiveString.IndexOf("\r\n", StringComparison.Ordinal)) >= 0) {
                            var line = _receiveString.Substring(0, l);
#if DEBUG
                            System.Diagnostics.Debug.WriteLine("SMTP recv: "+line);
#endif
                            _receiceLines.Add(line);
                            _receiveString = _receiveString.Substring(l + 2);

                            if (line.Length > 3 && _isdigit(line[0]) && _isdigit(line[1]) && _isdigit(line[2]) && line[3] == ' ') {
                                _receiceRespone.TrySetResult(new SMTPResponse(_receiceLines.ToArray()));
                                _receiceLines.Clear();
                            }
                        }
                    }
                }
                else {
                    throw new SMTPConnectionClosedException();
                }

                socket.BeginReceive(_receiveBuffer, 0, _receiveBuffer.Length, SocketFlags.None, _receiveCallback, socket);
            }
            catch(Exception err) {
                if (err is SocketException sockerr && sockerr.ErrorCode == 10054) {
                    _receiceRespone.TrySetException(new SMTPConnectionClosedException());
                }
                else {
                    _receiceRespone.TrySetException(err);
                }
            }
        }
        private     async   Task<SMTPResponse>                  _readResponseAsync()
        {
            var response = await _receiceRespone.Task;

            _lastResponse = response;

            lock(_lock) {
                _receiceRespone = new TaskCompletionSource<SMTPResponse>();
            }

            return response;
        }

        private     static  bool                                _isdigit(char c)
        {
            return '0' <= c && c <= '9';
        }
    }
}
