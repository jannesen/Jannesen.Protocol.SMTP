using System;

namespace Jannesen.Protocol.SMTP
{
    public class SMTPException: Exception
    {
        public                              SMTPException(string message): base(message)
        {
        }

        public  override    string          Source
        {
            get {
                return "Jannesen.Protocol.SMTP";
            }
        }

    }

    public class SMTPBadReplyException: SMTPException
    {
        public              SMTPResponse    Response            { get ; }

        public                              SMTPBadReplyException(string message, SMTPResponse response): base(message)
        {
            this.Response = response;
        }
    }

    public class SMTPUnexpectedReplyException: SMTPException
    {
        public              SMTPResponse    Response            { get ; }

        public                              SMTPUnexpectedReplyException(string message, SMTPResponse response): base(message)
        {
            this.Response = response;
        }
    }

    public class SMTPConnectionClosedException: SMTPException
    {
        public                              SMTPConnectionClosedException(string message): base(message)
        {
        }
    }
}
