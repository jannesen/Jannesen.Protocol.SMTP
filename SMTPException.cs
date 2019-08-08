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

        public                              SMTPBadReplyException(SMTPConnection smtp, string cmd, SMTPResponse response): base("SMTP to '"+smtp.RemoteEndPoint.ToString()+"' command '"+cmd+"' failed: "+(response.Responses.Length>0? response.Responses[0]:""))
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
}
