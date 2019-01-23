using System.Net.Http;
using NUnit.Framework;
using Vostok.Clusterclient.Transport.SystemNetHttp.Header;
using Vostok.Logging.Console;

namespace Vostok.Clusterclient.Transport.SystemNetHttp.Tests.Header
{
    [TestFixture]
    internal class RequestHeadersUnlocker_Tests
    {
        [TearDown]
        public void TearDown()
        {
            ConsoleLog.Flush();
        }

        [Test]
        public void Should_successfully_unlock_headers_of_HttpRequestMessage()
        {
            var message = new HttpRequestMessage();

            RequestHeadersUnlocker.TryUnlockRestrictedHeaders(message.Headers, new ConsoleLog());
        }
    }
}