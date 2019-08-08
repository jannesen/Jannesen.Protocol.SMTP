using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Permissions;


namespace Jannesen.Protocol.SMTP
{
    [Serializable]
    public sealed class SMTPResponse
    {
        public              int         Code            { get ; }
        public              string[]    Responses       { get ; }

        public              int         Status
        {
            get {
                return Code/100;
            }
        }
        public              string      Response
        {
            get {
                string  rtn;

                if (Responses.Length > 0) {
                    rtn = Responses[0];

                    for(int i = 1 ; i < Responses.Length ; ++i)
                        rtn += "\r\n" + Responses[i];
                }
                else
                    rtn = "";

                return rtn;
            }
        }

        internal                        SMTPResponse(StreamReader StreamInput)
        {
            string      line;

            try {
                line = StreamInput.ReadLine();
#if DEBUG
                System.Diagnostics.Debug.WriteLine("SMTP recv: "+line);
#endif
                if (line.Length > 3 && line[3]=='-') {
                    List<string>    ReadList = new List<string>();

                    ReadList.Add(line);

                    do {
                        line = StreamInput.ReadLine();
#if DEBUG
                        System.Diagnostics.Debug.WriteLine("SMTP recv: "+line);
#endif
                        ReadList.Add(line);
                    }
                    while (line.Length > 3 && line[3]=='-');

                    Code      = int.Parse(line.Substring(0,3), System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture);
                    Responses = ReadList.ToArray();
                }
                else {
                    Code         = int.Parse(line.Substring(0,3), System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture);
                    Responses    = new string[1];
                    Responses[0] = line;
                }
            }
            catch(IOException Exception) {
                Code         = 599;
                Responses    = new string[1];
                Responses[0] = "Command stream read error: "+Exception.Message;
            }
        }
    }
}
