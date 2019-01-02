using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Jannesen.Protocol.SMTP
{
    public sealed class SMTPConnection: IDisposable
    {
        public      const   int                 DefaultPort = 25;

        private             IPEndPoint          _localEndPoint;
        private             IPEndPoint          _remoteEndPoint;
        private             int                 _connectTimeout;
        private             int                 _timeout;
        private             TcpClient           _tcpClient;
        private             NetworkStream       _stream;
        private             StreamReader        _streamInput;
        private             SMTPResponse        _lastResponse;

        public              IPEndPoint          LocalEndPoint
        {
            get {
                return _localEndPoint;
            }
            set {
                if (_tcpClient != null)
                    new SMTPException("Not allowed to change LocalEndPoint after connection is made.");

                _localEndPoint = value;
            }
        }
        public              IPEndPoint          RemoteEndPoint
        {
            get {
                return _remoteEndPoint;
            }
            set {
                if (_tcpClient != null)
                    new SMTPException("Not allowed to change RemoteEndPoint after connection is made.");

                _remoteEndPoint = value;
            }
        }
        public              int                 ConnectTimeout
        {
            get {
                return _connectTimeout;
            }
            set {
                if (_tcpClient != null)
                    new SMTPException("Not allowed to change ConnectTimeout after connection is made.");

                _connectTimeout = value;
            }
        }
        public              int                 Timeout
        {
            get {
                return _timeout;
            }
            set {
                if (_tcpClient != null)
                    new SMTPException("Not allowed to change Timeout after connection is made.");

                _timeout = value;
            }
        }
        public              SMTPResponse        LastResponse
        {
            get {
                return _lastResponse;
            }
        }
        public              bool                isConnected
        {
            get {
                return _tcpClient != null && _tcpClient.Connected;
            }
        }

        public                                  SMTPConnection()
        {
            _localEndPoint  = null;
            _timeout        = 60000;
            _connectTimeout = 15000;
            _tcpClient      = null;
            _streamInput    = null;
            _lastResponse   = null;
        }
        public              void                Dispose()
        {
            Close();
        }

        public              void                Open()
        {
            Close();
#if DEBUG
            System.Diagnostics.Debug.WriteLine("SMTP open: "+RemoteEndPoint.ToString());
#endif
            _tcpClient   = new TcpClient();

            if (_localEndPoint != null)
                _tcpClient.Client.Bind(_localEndPoint);

            _tcpClient.SendTimeout    = ConnectTimeout;
            _tcpClient.ReceiveTimeout = ConnectTimeout;
            _tcpClient.NoDelay        = false;
            _tcpClient.Connect(RemoteEndPoint);
            _tcpClient.SendTimeout    = Timeout;
            _tcpClient.ReceiveTimeout = Timeout;

            _stream      = _tcpClient.GetStream();
            _streamInput = new StreamReader(_stream, System.Text.Encoding.ASCII, false, 1024);

            _lastResponse = new SMTPResponse(_streamInput);

            if(_lastResponse.Code != 220)
                throw new SMTPBadReplyException(this, "Invalid welcom received from SMTP-server.", _lastResponse);
        }
        public              void                Close()
        {
            if (_streamInput!=null) {
                _streamInput.Close();
                _streamInput = null;
            }

            if (_stream!=null) {
                _stream.Close();
                _stream = null;
            }

            if (_tcpClient!=null) {
                _tcpClient.Close();
                _tcpClient   = null;
            }
        }

        public              void                EHLO_HELO(string domain)
        {
            _cmd("EHLO "+domain);

            if (_lastResponse.Code != 250)
                _cmd("HELO "+domain);

            if (_lastResponse.Code != 250)
                throw new SMTPBadReplyException(this, "HELO", _lastResponse);
        }
        public              void                HELO(string domain)
        {
            _cmd("HELO "+domain, 250);
        }
        public              void                EHLO(string domain)
        {
            _cmd("EHLO "+domain, 250);
        }
        public              void                MAIL(string address)
        {
            _cmd("MAIL FROM: <"+address+">", 250);
        }
        public              void                RCPT(string address)
        {
            _cmd("RCPT TO: <"+address+">", 250);
        }
        public              void                DATA(byte[] message)
        {
            DATA(message, message.Length);
        }
        public              void                DATA(byte[] message, int length)
        {
            _cmd("DATA", 354);

            Stream stream = new BufferedStream(_stream, 6000);

            byte p = (byte)'\n';

            for (int i = 0 ; i < length ; ++i) {
                byte b = message[i];

                if ((b < ' ' || b > 127) && b != '\r' & b != '\n' && b != '\t')
                    b = (byte)'?';

                if (b == '.' && p == '\n')
                    stream.WriteByte((byte)'.');

                stream.WriteByte(b);

                p = b;
            }

            if (length <= 2 || message[length - 2] != '\r' || message[length - 1] != '\n') {
                stream.WriteByte((byte)'\r');
                stream.WriteByte((byte)'\n');
            }

            stream.WriteByte((byte)'.');
            stream.WriteByte((byte)'\r');
            stream.WriteByte((byte)'\n');
            stream.Flush();

            _lastResponse = new SMTPResponse(_streamInput);

            if (_lastResponse.Code != 250)
                throw new SMTPBadReplyException(this, "DATA", _lastResponse);
        }
        public              void                QUIT()
        {
            _cmd("QUIT");
        }

        private             void                _cmd(string cmd, int successfullCode)
        {
            _cmd(cmd);

            if (_lastResponse.Code != successfullCode)
                throw new SMTPBadReplyException(this, cmd, _lastResponse);
        }
        private             void                _cmd(string cmd)
        {
            if (_tcpClient == null)
                throw new SMTPException("smtp session is closed.");

            if (!_tcpClient.Connected)
                throw new SMTPException("smtp session is closed by remote.");
#if DEBUG
            System.Diagnostics.Debug.WriteLine("SMTP send: "+ cmd);
#endif
            _tcpClient.Client.Send(System.Text.Encoding.ASCII.GetBytes(cmd+"\r\n"));

            _lastResponse = new SMTPResponse(_streamInput);
        }
    }
}
