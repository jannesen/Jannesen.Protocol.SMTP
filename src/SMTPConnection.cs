using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jannesen.Protocol.SMTP
{
    public sealed class SMTPConnection: IDisposable
    {
        public      const   int                 DefaultPort = 25;

        private             IPEndPoint                          _localEndPoint;
        private             IPEndPoint                          _remoteEndPoint;
        private             int                                 _connectTimeout;
        private             int                                 _timeout;
        private             Socket                              _socket;
        private volatile    SMTPResponse                        _lastResponse;
        private             byte[]                              _receiveBuffer;
        private             string                              _receiveString;
        private             List<string>                        _receiceLines;
        private volatile    TaskCompletionSource<SMTPResponse>  _receiceRespone;
        private readonly    object                              _lockObject;

        public              IPEndPoint                          LocalEndPoint
        {
            get {
                return _localEndPoint;
            }
            set {
                if (_socket != null)
                    throw new SMTPException("Not allowed to change LocalEndPoint after connection is made.");

                _localEndPoint = value;
            }
        }
        public              IPEndPoint                          RemoteEndPoint
        {
            get {
                return _remoteEndPoint;
            }
            set {
                if (_socket != null)
                    throw new SMTPException("Not allowed to change RemoteEndPoint after connection is made.");

                _remoteEndPoint = value;
            }
        }
        public              int                                 ConnectTimeout
        {
            get {
                return _connectTimeout;
            }
            set {
                if (_socket != null)
                    throw new SMTPException("Not allowed to change ConnectTimeout after connection is made.");

                _connectTimeout = value;
            }
        }
        public              int                                 Timeout
        {
            get {
                return _timeout;
            }
            set {
                _timeout = value;
            }
        }
        public              SMTPResponse                        LastResponse
        {
            get {
                return _lastResponse;
            }
        }
        public              bool                                isConnected
        {
            get {
                lock(_lockObject) {
                    return _socket != null && _socket.Connected;
                }
            }
        }

        public                                                  SMTPConnection()
        {
            _timeout        = 60000;
            _connectTimeout = 15000;
            _lockObject     = new object();
        }
        public              void                                Dispose()
        {
            Close();
        }

        public      async   Task                                OpenAsync(CancellationToken cancellationToken)
        {
            Close();
#if DEBUG
            System.Diagnostics.Debug.WriteLine("SMTP open: "+_remoteEndPoint.ToString());
#endif
            var socket   = new Socket(SocketType.Stream, ProtocolType.Tcp);

            if (_localEndPoint != null) {
                socket.Bind(_localEndPoint);
            }

            socket.NoDelay        = false;

            lock(_lockObject) {
                _socket = socket;
            }

            var openTask = new TaskCompletionSource<object>();

            using(var x = cancellationToken.Register(() => {
                              openTask.TrySetException(new TaskCanceledException());
                              Close();
                          })) {
                using (var t = new Timer((state) => {
                                    openTask.TrySetException(new TimeoutException());
                                    Close();
                               }, null, _connectTimeout, 0)) {
                    socket.BeginConnect(_remoteEndPoint, (ar) => {
                                            try {
                                                socket.EndConnect(ar);
                                                openTask.TrySetResult(null);
                                            }
                                            catch(Exception err) {
                                                openTask.TrySetException(err);
                                            }
                                        },
                                        null);

                    await openTask.Task;

                    _receiveBuffer  = new byte[1024];
                    _receiveString  = null;
                    _receiceLines   = new List<string>();
                    _receiceRespone = new TaskCompletionSource<SMTPResponse>();
                    socket.BeginReceive(_receiveBuffer, 0, _receiveBuffer.Length, SocketFlags.None, _receiveCallback, socket);

                    var response = await _readResponseAsync();
                    if (response.Code != 220) {
                        throw new SMTPBadReplyException("Received invalid welcom received from SMTP-server '" + _remoteEndPoint.ToString() + "'. Response: " + response.ToString(), response);
                    }
                }
            }
        }
        public              void                                Close()
        {
            Socket  socket;

            lock(_lockObject) {
                if ((socket = _socket) != null) {
                    _socket   = null;
                }
            }

            if (socket != null) {
                try {
                    socket.Close();
                }
                catch(Exception) {
                }
            }
        }

        public      async   Task<SMTPResponse>                  EHLO_HELO_Async(string domain, CancellationToken cancellationToken)
        {
            if (domain is null) throw new ArgumentNullException(nameof(domain));

            var response = await CmdAsync("EHLO " + domain, _timeout, cancellationToken);

            if (response.Code != 250) {
                response = await CmdAsync("HELO " + domain, _timeout, cancellationToken);
            }

            if (response.Code != 250) {
                throw new SMTPBadReplyException("SMTP Command 'HELO' failed to '" + _remoteEndPoint.ToString() + "'. Response: " + response.ToString(), response);
            }

            return response;
        }
        public              Task<SMTPResponse>                  HELO_Async(string domain, CancellationToken cancellationToken)
        {
            if (domain is null) throw new ArgumentNullException(nameof(domain));

            return CmdAsyncAnd250("HELO " + domain, _timeout, cancellationToken);
        }
        public              Task<SMTPResponse>                  EHLO_Async(string domain, CancellationToken cancellationToken)
        {
            if (domain is null) throw new ArgumentNullException(nameof(domain));

            return CmdAsyncAnd250("EHLO "+domain, _timeout, cancellationToken);
        }
        public              Task<SMTPResponse>                  MAIL_FROM_Async(string address, CancellationToken cancellationToken)
        {
            if (address is null) throw new ArgumentNullException(nameof(address));

            return CmdAsyncAnd250("MAIL FROM: <"+address+">", _timeout, cancellationToken);
        }
        public              Task<SMTPResponse>                  RCPT_TO_Async(string address, CancellationToken cancellationToken)
        {
            if (address is null) throw new ArgumentNullException(nameof(address));

            return CmdAsyncAnd250("RCPT TO: <"+address+">", _timeout, cancellationToken);
        }
        public              Task<SMTPResponse>                  DATA_Async(string message, CancellationToken cancellationToken)
        {
            if (message is null) throw new ArgumentNullException(nameof(message));

            return DATA_Async(Encoding.ASCII.GetBytes(message), cancellationToken);
        }
        public              Task<SMTPResponse>                  DATA_Async(byte[] message, CancellationToken cancellationToken)
        {
            if (message is null) throw new ArgumentNullException(nameof(message));

            return DATA_Async(message, message.Length, cancellationToken);
        }
        public      async   Task<SMTPResponse>                  DATA_Async(byte[] message, int length, CancellationToken cancellationToken)
        {
            if (message is null) throw new ArgumentNullException(nameof(message));

            using (var timeoutcts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                timeoutcts.CancelAfter(_timeout);

                using (var x = timeoutcts.Token.Register(Dispose)) {
                    try {
                        await _sendAsync("DATA");
                        var response = await _readResponseAsync();

                        if (response.Code != 354) {
                            throw new SMTPBadReplyException("SMTP Command 'DATA' failed to '" + _remoteEndPoint.ToString() + "'. Response: " + response.ToString(), response);
                        }

                        var bsize  = 0x1000;
                        while (bsize < 0x40000 && bsize < length + 16) {
                            bsize <<= 1;
                        }

                        var buffer  = new byte[bsize + 8];
                        var bufferp = 0;
                        var prev    = (byte)'\n';

                        for (int i = 0 ; i < length ; ++i) {
                            byte chr = message[i];

                            if ((chr < ' ' || chr > 127) && chr != '\r' & chr != '\n' && chr != '\t')
                                chr = (byte)'?';

                            if (chr == '.' && prev == '\n') {
                                buffer[bufferp++] = (byte)'.';
                            }

                            buffer[bufferp++] = prev = chr;

                            if (bufferp >= bsize) {
                                await _sendAsync(buffer, bufferp);
                                bufferp = 0;
                            }
                        }

                        if (length <= 2 || message[length - 2] != '\r' || message[length - 1] != '\n') {
                            buffer[bufferp++] = (byte)'\r';
                            buffer[bufferp++] = (byte)'\n';
                        }

                        buffer[bufferp++] = (byte)'.';
                        buffer[bufferp++] = (byte)'\r';
                        buffer[bufferp++] = (byte)'\n';

                        await _sendAsync(buffer, bufferp);

                        response = await _readResponseAsync();
                        if (response.Code != 250) {
                            throw new SMTPBadReplyException("SMTP Command 'DATA' failed to '" + _remoteEndPoint.ToString() + "'. Response: " + response.ToString(), response);
                        }

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
            var response = await CmdAsync("QUIT", _timeout, cancellationToken);

            if (response.Code != 221) {
                throw new SMTPBadReplyException("SMTP Command 'QUIT' failed to '" + _remoteEndPoint.ToString() + "'. Response: " + response.ToString(), response);
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
                throw new SMTPBadReplyException("SMTP Command '" + cmd + "' failed to '" + _remoteEndPoint.ToString() + "'. Response: " + response.ToString(), response);
            }

            return response;
        }

        private             Task<object>                        _sendAsync(string cmd)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("SMTP send: " + cmd);
#endif
            var data = Encoding.ASCII.GetBytes(cmd + "\r\n");
            return _sendAsync(data, data.Length);
        }
        private             Task<object>                        _sendAsync(byte[] data, int length)
        {
            var task = _receiceRespone.Task;
            if (task.IsCompleted) {
                var expection = task.Exception;
                if (expection != null) {
                    throw expection;
                }

                var response = _receiceRespone.Task.Result;
                throw new SMTPUnexpectedReplyException("Unexpected data receive from SMTP-Server '" + _remoteEndPoint.ToString() + "'. Response: " + response.ToString() , response);
            }

            Socket socket;

            lock(_lockObject) {
                socket = _socket;
            }

            if (socket == null) {
                throw new SMTPException("smtp session is closed.");
            }

            if (!socket.Connected) {
                throw new SMTPException("smtp session is closed by remote.");
            }

            var rtn    = new TaskCompletionSource<object>();

            socket.BeginSend(data, 0, length, SocketFlags.None,
                             (ar) => {
                                 try {
                                     socket.EndSend(ar);
                                     rtn.TrySetResult(null);
                                 }
                                 catch(Exception err) {
                                    rtn.TrySetException(err);
                                 }
                             },
                             null);

            return rtn.Task;
        }
        private             void                                _receiveCallback(IAsyncResult ar)
        {
            var socket = (Socket)ar.AsyncState;

            try {
                var size = socket.EndReceive(ar);
                if (size > 0) {
                    lock(_lockObject) {
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
                    throw new SMTPConnectionClosedException("SMTP Connection closed by remote.");
                }

                socket.BeginReceive(_receiveBuffer, 0, _receiveBuffer.Length, SocketFlags.None, _receiveCallback, socket);
            }
            catch(Exception err) {
                _receiceRespone.TrySetException(err);
            }
        }
        private     async   Task<SMTPResponse>                  _readResponseAsync()
        {
            var response = await _receiceRespone.Task;

            _lastResponse = response;
            _receiceRespone = new TaskCompletionSource<SMTPResponse>();

            return response;
        }

        private     static  bool                                _isdigit(char c)
        {
            return '0' <= c && c <= '9';
        }
    }
}
