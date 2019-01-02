using System;

namespace Jannesen.Protocol.SMTP
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2237:MarkISerializableTypesWithSerializable")] // Serialize not implmented
    public class SMTPException: Exception
    {
        public                              SMTPException(string message) : base(message)
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

        public                              SMTPBadReplyException(SMTPConnection smtp, string cmd, SMTPResponse response)   : base("SMTP to '"+smtp.RemoteEndPoint.ToString()+"' command '"+cmd+"' failed: "+(response.Responses.Length>0? response.Responses[0]:""))
        {
            this.Response = response;
        }
    }
}
