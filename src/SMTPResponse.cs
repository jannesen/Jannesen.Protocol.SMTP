using System;

namespace Jannesen.Protocol.SMTP
{
    public sealed class SMTPResponse
    {
        private readonly    int         _code;
        private readonly    string[]    _responses;

        public              int         Code            => _code;
        public              string[]    Responses       => _responses;

        public              int         Status
        {
            get {
                return _code/100;
            }
        }
        public              string      Response
        {
            get {
                string  rtn;

                if (_responses.Length > 0) {
                    rtn = Responses[0];

                    for(var i = 1 ; i < Responses.Length ; ++i)
                        rtn += "\r\n" + Responses[i];
                }
                else
                    rtn = "";

                return rtn;
            }
        }

        internal                        SMTPResponse(string[] responses)
        {
            _responses = responses;
            var lastline = responses[^1];
            _code      = (lastline[0] - '0') * 100 +
                         (lastline[1] - '0') *  10 +
                         (lastline[2] - '0');
        }

        public  override    string      ToString()
        {
            return _responses.Length > 0 ? _responses[^1] : "Empty response.";
        }
    }
}
