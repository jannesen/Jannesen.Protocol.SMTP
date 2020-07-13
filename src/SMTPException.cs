using System;
using System.Runtime.Serialization;
using System.Security.Permissions;

namespace Jannesen.Protocol.SMTP
{
    [Serializable]
    public class SMTPException: Exception
    {
        public                              SMTPException(string message) : base(message)
        {
        }
        protected                           SMTPException(SerializationInfo info, StreamingContext context): base(info, context)
        {
        }

        public  override    string          Source
        {
            get {
                return "Jannesen.Protocol.SMTP";
            }
        }

    }

    [Serializable]
    public class SMTPBadReplyException: SMTPException
    {
        public              SMTPResponse    Response            { get ; }

        public                              SMTPBadReplyException(string message, SMTPResponse response): base(message)
        {
            this.Response = response;
        }

        protected                           SMTPBadReplyException(SerializationInfo info, StreamingContext context): base(info, context)
        {
            Response = (SMTPResponse)info.GetValue(nameof(Response), typeof(SMTPResponse));
        }
        [SecurityPermissionAttribute(SecurityAction.Demand, SerializationFormatter = true)]
        public override     void            GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(Response), Response);
        }
    }

    [Serializable]
    public class SMTPUnexpectedReplyException: SMTPException
    {
        public              SMTPResponse    Response            { get ; }

        public                              SMTPUnexpectedReplyException(string message, SMTPResponse response): base(message)
        {
            this.Response = response;
        }

        protected                           SMTPUnexpectedReplyException(SerializationInfo info, StreamingContext context): base(info, context)
        {
            Response = (SMTPResponse)info.GetValue(nameof(Response), typeof(SMTPResponse));
        }
        [SecurityPermissionAttribute(SecurityAction.Demand, SerializationFormatter = true)]
        public override     void            GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(Response), Response);
        }
    }

    [Serializable]
    public class SMTPConnectionClosedException: SMTPException
    {
        public                              SMTPConnectionClosedException(string message): base(message)
        {
        }

        protected                           SMTPConnectionClosedException(SerializationInfo info, StreamingContext context): base(info, context)
        {
        }
        [SecurityPermissionAttribute(SecurityAction.Demand, SerializationFormatter = true)]
        public override     void            GetObjectData(SerializationInfo info, StreamingContext context)
        {
        }
    }
}
