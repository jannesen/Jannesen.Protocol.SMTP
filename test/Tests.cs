using System;
using System.Threading;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jannesen.Protocol.SMTP.UnitTest
{
    [TestClass]
    public class Tests
    {
        private static  readonly    IPEndPoint      _remoteEndPoint = new IPEndPoint(IPAddress.Parse("192.168.203.4"), 25);

        [TestMethod]
        public  async               Task            OpenTest()
        {
            using (var connection = new SMTPConnection()) {
                connection.RemoteEndPoint = _remoteEndPoint;
                await connection.OpenAsync(CancellationToken.None);
            }

            await Task.Delay(50);
        }

        [TestMethod]
        public  async               Task            OpenTestTimeout()
        {
            var start = DateTime.UtcNow;

            try {
                using (var connection = new SMTPConnection()) {
                    connection.RemoteEndPoint = new IPEndPoint(IPAddress.Parse("192.168.203.1"), 25);
                    connection.ConnectTimeout = 1000;
                    await connection.OpenAsync(CancellationToken.None);
                }
            }
            catch(TimeoutException) {
                var d = (DateTime.UtcNow - start).Ticks / TimeSpan.TicksPerMillisecond;
                Assert.IsTrue(900 < d && d < 1100);
            }
        }

        [TestMethod]
        public  async               Task            QuitTest()
        {
            using (var connection = new SMTPConnection()) {
                connection.RemoteEndPoint = _remoteEndPoint;
                connection.ConnectTimeout = 1000;
                await connection.OpenAsync(CancellationToken.None);
                await connection.QUIT_Async(CancellationToken.None);
            }
        }

        [TestMethod]
        public  async               Task            SendTest()
        {
            using (var timer = new CancellationTokenSource(30000)) {
                using (var connection = new SMTPConnection()) {
                    connection.RemoteEndPoint = _remoteEndPoint;
                    connection.ConnectTimeout = 5000;
                    connection.Timeout = 5000;
                    await connection.OpenAsync(timer.Token);
                    await connection.MAIL_FROM_Async("peter@jannesen.com", timer.Token);
                    await connection.RCPT_TO_Async("peter@jannesen.com", timer.Token);
                    await connection.DATA_Async("Description: Just a test\r\n" +
                                                "\r\n" +
                                                "Test from Jannesen.Protocol.SMTP.UnitTest",
                                                timer.Token);
                    await connection.QUIT_Async(timer.Token);
                }
            }
        }
    }
}
